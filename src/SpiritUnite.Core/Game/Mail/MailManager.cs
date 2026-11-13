using System.Collections.Concurrent;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Recharge;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Online;

namespace SpiritUnite.Game.Mail;

public record GrantOutcome(MailGrant Grant, RoleWallet Wallet, IReadOnlyDictionary<int, long> BeforeCoinNums)
{
    public IReadOnlyList<BagItem> ChangedBagItems { get; init; } = [];
}

public sealed record MailClaim(RoleMail Mail, MailGrant Grant, RoleWallet Wallet, IReadOnlyDictionary<int, long> BeforeCoinNums)
    : GrantOutcome(Grant, Wallet, BeforeCoinNums);

public sealed record MailSender(LMNRoleInfo Role, ProfileStyle Style);

public sealed class MailManager(
    MailRepository repository,
    RoleInfoStore roles,
    ProfileRepository profiles,
    LobbyRoles lobbyRoles,
    GameData gameData,
    BagManager bag,
    TimeProvider clock,
    ILogger<MailManager> logger)
{
    private const int SecondsPerDay = 86400;
    private const int AwardedPokemonCount = 1;
    private const int ProbeItemCount = 1;
    private const int WelcomeMailID = 5;
    private const ulong NoSender = 0;

    private static readonly SystemMail WelcomeMail = new(
        Subject: "Mail_TextTitle_5_001",
        Content: "Mail_TextContent_5_001",
        Attachments: [],
        MailID: WelcomeMailID);

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> claimGates = new();

    public async Task<RoleMail> SendSystemMailAsync(ulong uid, SystemMail mail)
    {
        var saved = await InsertSystemMailAsync(uid, mail);
        await PushNewMailsAsync([saved]);
        return saved;
    }

    public async Task<bool> SendSystemMailBatchAsync(ulong uid, int mailType, IReadOnlyList<SystemMail> mails)
    {
        if (mails.Any(mail => mail.MailType != mailType))
        {
            throw new ArgumentException($"Every mail of one batch has mail type {mailType}", nameof(mails));
        }

        var savedMails = new List<RoleMail>();
        foreach (var mail in mails)
        {
            savedMails.Add(await InsertSystemMailAsync(uid, mail));
        }

        return await PushNewMailsAsync(savedMails);
    }

    public Task<bool> PushMailNewAsync(RoleMail saved) => PushNewMailsAsync([saved]);

    public async Task<bool> PushSystemMailBatchAsync(ulong uid, int mailType, IReadOnlyList<RoleMail> savedMails)
    {
        if (savedMails.Any(saved => saved.GameUlongUID != uid || saved.MailType != mailType))
        {
            throw new ArgumentException("Mail batch recipient or type does not match", nameof(savedMails));
        }

        return await PushNewMailsAsync(savedMails);
    }

    public async Task<bool> PushNewMailsAsync(IReadOnlyList<RoleMail> savedMails)
    {
        var pushed = false;
        foreach (var recipient in savedMails.GroupBy(saved => saved.GameUlongUID))
        {
            if (!lobbyRoles.IsOnline(recipient.Key))
            {
                continue;
            }

            try
            {
                pushed |= await PushRecipientNewMailsAsync(recipient.Key, [.. recipient]);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "New mail push to uid {Uid} failed; its {Count} committed mails show at the next mail red dot sync or login",
                    recipient.Key, recipient.Count());
            }
        }

        return pushed;
    }

    private async Task<bool> PushRecipientNewMailsAsync(ulong uid, IReadOnlyList<RoleMail> delivered)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var unread = await repository.CountUnreadAsync(uid, now);
        var unannounced = new int[MailBox.MailTypeCount];
        foreach (var saved in delivered.Where(saved => IsCountedUnread(saved, now)))
        {
            unannounced[saved.MailType]++;
        }

        var packets = new List<GamePacket>();
        foreach (var delivery in delivered.GroupBy(saved => saved.MailType).OrderBy(delivery => delivery.Key))
        {
            var items = new List<pbCOMDT_MAIL_ITEM>();
            foreach (var saved in delivery)
            {
                var item = BuildMailItem(saved);
                if (await FindSenderAsync(saved.FromUid) is { } sender)
                {
                    item.UserPlantInfo = sender.Style.BuildUserPlantInfo();
                }

                items.Add(item);
            }

            var unreadBefore = Subtract(unread, unannounced);
            if (MailBox.IsMailType(delivery.Key))
            {
                unannounced[delivery.Key] -= delivery.Count(saved => IsCountedUnread(saved, now));
            }

            var mailNewNtf = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMailNewNtf, new pbSCMailNewNtf
            {
                MailList = new pbCOMDT_MAIL_LIST { MailType = delivery.Key, MailInfo = { items } },
            });
            packets.AddRange(BuildNewMailPackets(unreadBefore, Subtract(unread, unannounced), delivery.Key, mailNewNtf));
        }

        return await lobbyRoles.PushAsync(uid, [.. packets]);
    }

    private static bool IsCountedUnread(RoleMail saved, long now) =>
        saved.MailState == MailBox.Unread && saved.ExpireTime > now && MailBox.IsMailType(saved.MailType);

    private static int[] Subtract(int[] unread, int[] unannounced) =>
        [.. unread.Select((count, mailType) => Math.Max(0, count - unannounced[mailType]))];

    public static GamePacket[] BuildNewMailPackets(int[] unreadBefore, int[] unreadAfter, int mailType, GamePacket mailNewNtf)
    {
        var packets = new List<GamePacket>();
        for (var walkedType = 0; walkedType < MailBox.MailTypeCount; walkedType++)
        {
            if (walkedType == mailType)
            {
                packets.Add(mailNewNtf);
                packets.AddRange(BuildMailTypeRedDotPackets(unreadAfter, walkedType));
            }
            else if ((walkedType < mailType ? unreadBefore : unreadAfter) is var walkedUnread && walkedUnread[walkedType] > 0)
            {
                packets.AddRange(BuildMailTypeRedDotPackets(walkedUnread, walkedType));
            }
        }

        return [.. packets];
    }

    public static GamePacket[] BuildUnreadRedDotPackets(int[] unread) =>
        [.. Enumerable.Range(0, MailBox.MailTypeCount).Where(mailType => unread[mailType] > 0).SelectMany(mailType => BuildMailTypeRedDotPackets(unread, mailType))];

    public static GamePacket[] BuildUnreadRedDotStatePackets(int[] unread) =>
    [
        .. Enumerable.Range(0, MailBox.MailTypeCount).Select(MailBox.FindUnreadRedDotID).OfType<int>().Distinct()
            .Select(redDotID => BuildUnreadRedDotPacket(redDotID, CountUnread(unread, redDotID))),
        BuildUnreadRedDotPacket(MailBox.UnreadAllMailRedDotID, CountUnread(unread, MailBox.UnreadAllMailRedDotID)),
    ];

    private static IEnumerable<GamePacket> BuildMailTypeRedDotPackets(int[] unread, int mailType) =>
        MailBox.FindUnreadRedDotID(mailType) is { } redDotID
            ?
            [
                BuildUnreadRedDotPacket(redDotID, CountUnread(unread, redDotID)),
                BuildUnreadRedDotPacket(MailBox.UnreadAllMailRedDotID, CountUnread(unread, MailBox.UnreadAllMailRedDotID)),
            ]
            : [];

    public Task<RoleMail> SendWelcomeMailAsync(ulong uid) => InsertSystemMailAsync(uid, WelcomeMail);

    public async Task<GamePacket[]> BuildUnreadRedDotPacketsAsync(ulong uid, int mailType)
    {
        var unread = await repository.CountUnreadAsync(uid, clock.GetUtcNow().ToUnixTimeSeconds());
        int?[] redDotIDs = [MailBox.FindUnreadRedDotID(mailType), MailBox.UnreadAllMailRedDotID];
        return
        [
            .. redDotIDs.OfType<int>().Select(redDotID => BuildUnreadRedDotPacket(redDotID, CountUnread(unread, redDotID))),
        ];
    }

    public async Task<pbCOMDT_RED_DOT_DETAIL[]> BuildUnreadRedDotDetailsAsync(ulong uid) =>
        [.. (await BuildAllUnreadRedDotCountsAsync(uid)).Where(detail => detail.RedDotBadgeNum > 0)];

    public async Task<pbCOMDT_RED_DOT_DETAIL[]> BuildAllUnreadRedDotCountsAsync(ulong uid)
    {
        var unread = await repository.CountUnreadAsync(uid, clock.GetUtcNow().ToUnixTimeSeconds());
        int[] redDotIDs =
        [
            .. Enumerable.Range(0, MailBox.MailTypeCount).Select(MailBox.FindUnreadRedDotID).OfType<int>().Distinct(),
            MailBox.UnreadAllMailRedDotID,
        ];
        return [.. redDotIDs.Select(redDotID => new pbCOMDT_RED_DOT_DETAIL { RedDotID = redDotID, RedDotBadgeNum = CountUnread(unread, redDotID) })];
    }

    private static int CountUnread(int[] unread, int redDotID) =>
        redDotID == MailBox.UnreadAllMailRedDotID
            ? unread.Sum()
            : Enumerable.Range(0, unread.Length).Where(mailType => MailBox.FindUnreadRedDotID(mailType) == redDotID).Sum(mailType => unread[mailType]);

    private static GamePacket BuildUnreadRedDotPacket(int redDotID, int unreadCount) => unreadCount > 0
        ? GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = redDotID, RedDotBadgeNum = unreadCount } },
            },
        })
        : GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = redDotID });

    public async Task<RoleMail> InsertSystemMailAsync(ulong uid, SystemMail mail)
    {
        var saved = await repository.InsertAsync(CreateSystemMail(uid, mail));
        LogSystemMailSent(saved, mail);
        return saved;
    }

    public void LogSystemMailSent(RoleMail saved, SystemMail mail) =>
        logger.LogInformation(
            "Mail {MailIndex} of type {MailType} with {AttachmentCount} attachments sent to uid {Uid}",
            saved.MailIndex,
            saved.MailType,
            mail.Attachments.Count,
            saved.GameUlongUID);

    public RoleMail CreateSystemMail(ulong uid, SystemMail mail)
    {
        ValidateSystemMail(mail);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var accessInfo = new pbCOMDT_MAILACCESS_INFO
        {
            Access = { mail.Attachments.Select(award => new pbCOMDT_MAILACCESS { ResInfo = award.Clone() }) },
        };
        return new RoleMail
        {
            GameUlongUID = uid,
            MailType = mail.MailType,
            MailSubType = (int)(mail.Attachments.Count > 0
                ? PB_ENUM_RES_MAIL_SUB_TYPE.PbResMailSubTypeAcess
                : PB_ENUM_RES_MAIL_SUB_TYPE.PbResMailSubTypeNormal),
            MailID = mail.MailID,
            MailState = MailBox.Unread,
            FromUid = mail.FromUid,
            FromName = mail.FromName,
            Subject = mail.Subject,
            Content = mail.Content,
            ContentParam = BuildContentParam(mail).ToByteArray(),
            SendTime = now,
            ExpireTime = now + ((long)mail.ValidDays * SecondsPerDay),
            AccessInfo = accessInfo.ToByteArray(),
            AccessState = mail.Attachments.Count > 0 ? MailBox.AccessUnget : MailBox.AccessNone,
        };
    }

    public async Task<pbCOMDT_MAIL_LIST> GetMailListAsync(ulong uid, int mailType)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        await repository.DeleteExpiredAsync(uid, now);
        var mails = await repository.GetMailsAsync(uid, mailType, now, MailBox.MaxMailNum);
        var senders = new Dictionary<ulong, MailSender?>();
        var list = new pbCOMDT_MAIL_LIST { MailType = mailType };
        foreach (var mail in mails)
        {
            var item = BuildMailItem(mail);
            if (!senders.TryGetValue(mail.FromUid, out var sender))
            {
                sender = await FindSenderAsync(mail.FromUid);
                senders[mail.FromUid] = sender;
            }

            if (sender is not null)
            {
                item.FromGender = sender.Role.Gender;
                item.FromHeadId = RoleImage.GetHeadID(gameData, sender.Role);
                item.RoleAvatarInfo = sender.Role.RoleAvatarInfo.Clone();
                item.UserPlantInfo = sender.Style.BuildUserPlantInfo();
            }

            list.MailInfo.Add(item);
        }

        return list;
    }

    private async Task<MailSender?> FindSenderAsync(ulong fromUid) =>
        fromUid != NoSender && await roles.FindAsync(fromUid) is { } role
            ? new MailSender(role, await profiles.LoadAsync(fromUid))
            : null;

    public async Task<pbCOMDT_MAIL_DETAIL?> ReadMailAsync(ulong uid, int mailType, int mailIndex)
    {
        if (await repository.FindAsync(uid, mailType, mailIndex, clock.GetUtcNow().ToUnixTimeSeconds()) is not { } mail)
        {
            return null;
        }

        if (mail.MailState != MailBox.Haveread)
        {
            await repository.MarkReadAsync(uid, [mail.MailIndex]);
            mail.MailState = MailBox.Haveread;
        }

        return BuildMailDetail(mail);
    }

    public async Task<int[]> BatchReadAsync(ulong uid, int mailType)
    {
        var mails = await repository.GetMailsAsync(uid, mailType, clock.GetUtcNow().ToUnixTimeSeconds(), MailBox.MaxMailNum);
        int[] unread = [.. mails.Where(mail => mail.MailState == MailBox.Unread).Select(mail => mail.MailIndex)];
        if (unread.Length > 0)
        {
            await repository.MarkReadAsync(uid, unread);
        }

        return unread;
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, int[] Deleted)> DeleteMailAsync(ulong uid, int mailType, int mailIndex)
    {
        if (await repository.FindAsync(uid, mailType, mailIndex, clock.GetUtcNow().ToUnixTimeSeconds()) is not { } mail)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrMailNotExist, []);
        }

        if (mail.AccessState == MailBox.AccessUnget)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrMailNotReady, []);
        }

        return await repository.DeleteAsync(uid, [mail.MailIndex]) > 0
            ? (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, [mail.MailIndex])
            : (PB_ENUM_RES_ERROR_CODE.PbErrMailNotExist, []);
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, int[] Deleted)> BatchDeleteMailAsync(ulong uid, int mailType)
    {
        var mails = await repository.GetMailsAsync(uid, mailType, clock.GetUtcNow().ToUnixTimeSeconds(), MailBox.MaxMailNum);
        int[] deletable = [.. mails
            .Where(mail => mail.MailState == MailBox.Haveread && mail.AccessState != MailBox.AccessUnget)
            .Select(mail => mail.MailIndex)];
        if (deletable.Length == 0)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrMailNoBatchDel, []);
        }

        await repository.DeleteAsync(uid, deletable);
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, deletable);
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, MailClaim? Claim)> GetAccessAsync(LMNRoleInfo role, int mailType, int mailIndex)
    {
        var gate = claimGates.GetOrAdd(role.GameUlongUID, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await ClaimLockedAsync(role, mailType, mailIndex);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<GrantOutcome?> GrantDirectAsync(LMNRoleInfo role, IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_INFO> awards)
    {
        var uid = role.GameUlongUID;
        var gate = claimGates.GetOrAdd(uid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            var accessInfo = new pbCOMDT_MAILACCESS_INFO();
            accessInfo.Access.Add(awards.Select(award => new pbCOMDT_MAILACCESS { ResInfo = award }));
            if (BuildGrant(role, accessInfo, now) is not { } grant)
            {
                return null;
            }

            var (result, wallet, beforeCoinNums, changedBagItems) = await repository.GrantAsync(uid, grant, now);
            if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || wallet is null)
            {
                logger.LogInformation("Direct grant refused for uid {Uid}: {Result}", uid, result);
                return null;
            }

            ApplyToRole(role, grant, now);
            logger.LogInformation(
                "Direct grant to uid {Uid}: {ItemCount} items, {PokemonCount} Pokemon, {BagCount} bag items",
                uid,
                grant.Items.Count,
                grant.PokemonIds.Count,
                grant.BagGrants.Count);
            return new GrantOutcome(grant, wallet, beforeCoinNums) { ChangedBagItems = changedBagItems };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, MailClaim[] Claims)> BatchGetAccessAsync(LMNRoleInfo role, int mailType)
    {
        var mails = await repository.GetMailsAsync(role.GameUlongUID, mailType, clock.GetUtcNow().ToUnixTimeSeconds(), MailBox.MaxMailNum);
        var claims = new List<MailClaim>();
        var result = PB_ENUM_RES_ERROR_CODE.PbErrMailNoAccess;
        foreach (var mail in mails.Where(mail => mail.AccessState == MailBox.AccessUnget))
        {
            var (claimResult, claim) = await GetAccessAsync(role, mailType, mail.MailIndex);
            result = claim is not null ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : claimResult;
            if (claim is not null)
            {
                claims.Add(claim);
            }
        }

        return (claims.Count > 0 ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : result, [.. claims]);
    }

    public static pbCOMDT_MAIL_ACCESS_DETAIL BuildAccessDetail(RoleMail mail) => new()
    {
        MailType = mail.MailType,
        MailIndex = mail.MailIndex,
        AccessInfo = BuildAccessInfo(mail),
        MailState = mail.MailState,
    };

    public static pbSCResourcesAwardNotify BuildResourcesAwardNotify(IEnumerable<GrantOutcome> claims)
    {
        var notify = new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonMail,
        };
        notify.ResourcesList.Add(claims.SelectMany(claim => claim.Grant.Awards));
        return notify;
    }

    private async Task<(PB_ENUM_RES_ERROR_CODE Result, MailClaim? Claim)> ClaimLockedAsync(LMNRoleInfo role, int mailType, int mailIndex)
    {
        var uid = role.GameUlongUID;
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (await repository.FindAsync(uid, mailType, mailIndex, now) is not { } mail)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrMailNotExist, null);
        }

        if (mail.AccessState != MailBox.AccessUnget)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrMailNoAccess, null);
        }

        if (BuildGrant(role, pbCOMDT_MAILACCESS_INFO.Parser.ParseFrom(mail.AccessInfo), now) is not { } grant)
        {
            logger.LogWarning("Mail {MailIndex} claim refused for uid {Uid}: a Unite license attachment has no grant rule", mailIndex, uid);
            return (PB_ENUM_RES_ERROR_CODE.PbErrMailGetAccessFail, null);
        }

        var (result, wallet, beforeCoinNums, changedBagItems) = await repository.ClaimAsync(uid, mail.MailIndex, grant, now);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || wallet is null)
        {
            logger.LogInformation("Mail {MailIndex} claim refused for uid {Uid}: {Result}", mailIndex, uid, result);
            return (result, null);
        }

        ApplyToRole(role, grant, now);
        mail.AccessState = MailBox.AccessGeted;
        mail.MailState = MailBox.Haveread;
        logger.LogInformation(
            "Mail {MailIndex} claimed by uid {Uid}: coins {Coins}, {ItemCount} items, Pokemon {PokemonIds}",
            mail.MailIndex,
            uid,
            string.Join(',', grant.Coins.Select(coin => $"{coin.Key}x{coin.Value}")),
            grant.Items.Count,
            string.Join(',', grant.PokemonIds));
        return (result, new MailClaim(mail, grant, wallet, beforeCoinNums) { ChangedBagItems = changedBagItems });
    }

    private static void ApplyToRole(LMNRoleInfo role, MailGrant grant, long now)
    {
        foreach (var item in grant.Items)
        {
            role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
        }

        foreach (var pokemonId in grant.PokemonIds)
        {
            role.GrantPokemon(pokemonId, now, (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonMail);
        }
    }

    private MailGrant? BuildGrant(LMNRoleInfo role, pbCOMDT_MAILACCESS_INFO accessInfo, long now)
    {
        var coins = new Dictionary<int, long>();
        var items = new List<RoleItemInfo>();
        var pokemonIds = new List<int>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        var bagGrants = new List<BagGrant>();
        foreach (var award in accessInfo.Access.Select(access => access.ResInfo).OfType<pbCOMDT_ONE_RESOURCE_AWARD_INFO>())
        {
            if ((PB_ENUM_RES_RESOURCES_TYPE)award.ResType == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem)
            {
                if (!UniteLicense.IsUniteLicense(gameData, award.ResID))
                {
                    if (bag.CreateOutSideItemGrant(award.ResID, award.ResCnt) is not { } bagGrant)
                    {
                        return null;
                    }

                    bagGrants.Add(bagGrant);
                    awards.Add(new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO { FinallyAwardInfo = award });
                    continue;
                }

                if (UniteLicense.PlanGrant(gameData, role, award.ResID, award.ResCnt, pokemonIds) is not { } license)
                {
                    return null;
                }

                license.AddTo(pokemonIds, coins, awards);
                continue;
            }

            if ((PB_ENUM_RES_RESOURCES_TYPE)award.ResType == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesBadge)
            {
                if (bag.CreateBadgeGrant(award.ResID, award.ResCnt) is not { } badgeGrant)
                {
                    return null;
                }

                bagGrants.Add(badgeGrant);
                awards.Add(new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO { FinallyAwardInfo = award });
                continue;
            }

            if ((PB_ENUM_RES_RESOURCES_TYPE)award.ResType == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPaster)
            {
                if (PasterGrant.Plan(gameData, bag, role, award.ResID, award.ResCnt, now, items) is not { } paster)
                {
                    return null;
                }

                paster.AddTo(items, bagGrants, coins, awards);
                continue;
            }

            if (IsAvatarSuitAward(award))
            {
                foreach (var avatarID in RoleAvatar.FindSuitParts(gameData, award.ResID) ?? [])
                {
                    if (!role.OwnsItem(RoleItemType.AvatarItem, avatarID)
                        && items.All(item => item.ItemType != RoleItemType.AvatarItem || item.ItemId != avatarID))
                    {
                        var part = new RoleItemInfo(RoleItemType.AvatarItem, avatarID, now, RoleItemInfo.Permanent, RoleItemInfo.ShowNewTag);
                        items.Add(part);
                        awards.Add(part.ToAwardReplaceInfo());
                    }
                }

                continue;
            }

            if (IsCosmeticResourceType((PB_ENUM_RES_RESOURCES_TYPE)award.ResType))
            {
                if (CosmeticGrant.Plan(gameData, bag, role, award.ResID, award.ResCnt, now, items) is not { } cosmetic)
                {
                    return null;
                }

                cosmetic.AddTo(items, bagGrants, coins, awards);
                continue;
            }

            awards.Add(new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO { FinallyAwardInfo = award });
            switch ((PB_ENUM_RES_RESOURCES_TYPE)award.ResType)
            {
                case PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin:
                case PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr:
                    coins[award.ResID] = coins.GetValueOrDefault(award.ResID) + award.ResCnt;
                    break;
                case PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesLobbyBackground:
                    AddItemGrant(role, RoleItemType.LobbyBg, award.ResID, now, items);
                    break;
                case PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon:
                    if (!role.OwnsPokemon(award.ResID) && !pokemonIds.Contains(award.ResID))
                    {
                        pokemonIds.Add(award.ResID);
                    }

                    break;
            }
        }

        return new MailGrant(coins, items, pokemonIds, awards)
        {
            BagGrants = bagGrants,
            CouponsFlow = CouponsFlow.GainFromMail(coins, DateTimeOffset.FromUnixTimeSeconds(now)),
        };
    }

    private static void AddItemGrant(LMNRoleInfo role, RoleItemType itemType, int itemId, long now, List<RoleItemInfo> items)
    {
        if (!role.OwnsItem(itemType, itemId) && items.All(item => item.ItemType != itemType || item.ItemId != itemId))
        {
            items.Add(new RoleItemInfo(itemType, itemId, now, RoleItemInfo.Permanent, RoleItemInfo.ShowNewTag));
        }
    }

    private void ValidateSystemMail(SystemMail mail)
    {
        if (!MailBox.IsMailType(mail.MailType)
            || mail.ValidDays is < 1 or > MailBox.MaxMailDays
            || mail.Attachments.Count > MailBox.MaxMailAccess
            || Encoding.UTF8.GetByteCount(mail.Subject) > MailBox.MaxMailSubject
            || Encoding.UTF8.GetByteCount(mail.Content) > MailBox.MaxMailContent
            || Encoding.UTF8.GetByteCount(mail.FromName) > MailBox.MaxFromName
            || (mail.ContentParams ?? []).Count > MailBox.MaxMailContentParamNum
            || (mail.ContentParams ?? []).Any(param => Encoding.UTF8.GetByteCount(param.Param) > MailBox.MaxMailParamLen))
        {
            throw new ArgumentException("System mail does not fit the client mail limits", nameof(mail));
        }

        foreach (var award in mail.Attachments)
        {
            if (!CanAttach(award))
            {
                throw new ArgumentException($"System mail attachment {award.ResType}:{award.ResID}x{award.ResCnt} cannot be granted", nameof(mail));
            }
        }
    }

    public pbCOMDT_ONE_RESOURCE_AWARD_INFO[] BuildDropAttachments(IEnumerable<int> sDropIDs)
    {
        var attachments = new List<pbCOMDT_ONE_RESOURCE_AWARD_INFO>();
        foreach (var sDropID in sDropIDs)
        {
            foreach (var drop in gameData.DropStatic.Rows.Where(drop => drop.SDropID == sDropID).OrderBy(drop => drop.SDropSeq))
            {
                var award = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
                {
                    ResID = drop.ResID,
                    ResCnt = drop.ResNum,
                    ResType = (int)(CoinID.IsGrantableCoin(drop.ResID) ? CoinID.GetCoinResourceType(drop.ResID) : CoinID.GetResIDType(drop.ResID)),
                };
                if (!CanAttach(award))
                {
                    logger.LogWarning("Drop {SDropID} resource {ResID} x{ResNum} cannot be attached to a mail", sDropID, drop.ResID, drop.ResNum);
                    continue;
                }

                if (attachments.FirstOrDefault(attachment => attachment.ResID == award.ResID && attachment.ResType == award.ResType) is { } same)
                {
                    same.ResCnt += award.ResCnt;
                }
                else
                {
                    attachments.Add(award);
                }
            }
        }

        return [.. attachments];
    }

    public bool CanAttach(pbCOMDT_ONE_RESOURCE_AWARD_INFO award) =>
        award.ResCnt > 0 && (PB_ENUM_RES_RESOURCES_TYPE)award.ResType switch
        {
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin => award.ResID > 0,
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr => CoinID.IsSpecialCoin(award.ResID),
            var cosmeticType when IsCosmeticResourceType(cosmeticType) => CanAttachCosmetic(award) || IsAvatarSuitAward(award),
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPaster =>
                gameData.PokemonAvatarBase.Contains(award.ResID) && award.ResCnt == AwardedPokemonCount,
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesLobbyBackground =>
                gameData.LobbyBg.Contains(award.ResID) && award.ResID != gameData.DefaultLobbyBgId,
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon => award.ResID > 0 && award.ResCnt == AwardedPokemonCount,
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem =>
                UniteLicense.IsUniteLicense(gameData, award.ResID) || IsBagOutSideItem(award.ResID),
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesBadge => gameData.BadgeBase.Contains(award.ResID),
            _ => false,
        };

    private static bool IsCosmeticResourceType(PB_ENUM_RES_RESOURCES_TYPE resType) => resType
        is PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar
        or PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadicon
        or PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadFrame
        or PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesContestantCardRecoration
        or PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesSkilleffectsexpandpack
        or PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCardFace
        or PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCanopy
        or PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesNewPersonaliseButton
        or PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesGloryAnnounce;

    private bool CanAttachCosmetic(pbCOMDT_ONE_RESOURCE_AWARD_INFO award) =>
        (int)CoinID.GetResIDType(award.ResID) == award.ResType
        && CosmeticGrant.FindItemType(gameData, award.ResID) is { } itemType
        && (award.ResCnt == ProbeItemCount || CosmeticGrant.FindTransmute(gameData, itemType, award.ResID) is { TransmuteNum: > 0 });

    private bool IsAvatarSuitAward(pbCOMDT_ONE_RESOURCE_AWARD_INFO award) =>
        (PB_ENUM_RES_RESOURCES_TYPE)award.ResType == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar
        && award.ResCnt == ProbeItemCount
        && RoleAvatar.FindSuitParts(gameData, award.ResID) is { Length: > 0 };

    private bool IsBagOutSideItem(int outSideItemId) =>
        CoinID.GetResIDType(outSideItemId) == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem
        && bag.CreateOutSideItemGrant(outSideItemId, ProbeItemCount) is not null;

    private static pbCOMDT_MAIL_ITEM BuildMailItem(RoleMail mail) => new()
    {
        MailSubType = mail.MailSubType,
        MailIndex = mail.MailIndex,
        MailState = mail.MailState,
        FromUid = mail.FromUid,
        FromName = mail.FromName,
        Subject = mail.Subject,
        SendTime = mail.SendTime,
        AccessInfo = BuildAccessInfo(mail),
        ValidTime = (int)((mail.ExpireTime - mail.SendTime) / SecondsPerDay),
        UserPlantInfo = new pbCOMDT_ROLE_HEAD_URL_INFO
        {
            CurPersonStyleInfo = new pbCOMDT_PLAYER_PERSON_STYLE_CUR_INFO(),
            NPSetting = new pbCOMDT_NOBLE_PREVILEG_GAME_SETTING_INFO(),
        },
        MailID = mail.MailID,
    };

    private static pbCOMDT_MAIL_DETAIL BuildMailDetail(RoleMail mail) => new()
    {
        MailType = mail.MailType,
        MailIndex = mail.MailIndex,
        MailState = mail.MailState,
        Content = mail.Content,
        AccessInfo = BuildAccessInfo(mail),
        HyperLink = new pbCOMDT_MAIL_HYPERLINK_INFO { LinkData = new pbCOMDT_MAIL_HYPERLINK_DETAIL() },
        ParamInfo = mail.ContentParam.Length > 0
            ? pbCOMDT_MAIL_CONTENT_PARAM.Parser.ParseFrom(mail.ContentParam)
            : new pbCOMDT_MAIL_CONTENT_PARAM(),
    };

    private static pbCOMDT_MAIL_CONTENT_PARAM BuildContentParam(SystemMail mail) => new()
    {
        ParamList =
        {
            (mail.ContentParams ?? []).Select(param => new pbCOMDT_MAIL_PARAM_DETAIL { Param = param.Param, Type = param.Type }),
        },
    };

    private static pbCOMDT_MAILACCESS_INFO BuildAccessInfo(RoleMail mail)
    {
        var accessInfo = pbCOMDT_MAILACCESS_INFO.Parser.ParseFrom(mail.AccessInfo);
        foreach (var access in accessInfo.Access)
        {
            access.Geted = mail.AccessState;
        }

        return accessInfo;
    }
}
