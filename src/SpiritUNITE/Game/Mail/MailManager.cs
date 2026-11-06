using System.Collections.Concurrent;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Online;

namespace SpiritUNITE.Game.Mail;

public sealed record MailClaim(RoleMail Mail, MailGrant Grant, RoleWallet Wallet, IReadOnlyDictionary<int, long> BeforeCoinNums);

public sealed class MailManager(
    MailRepository repository,
    LobbyRoles lobbyRoles,
    GameData gameData,
    TimeProvider clock,
    ILogger<MailManager> logger)
{
    private const int SecondsPerDay = 86400;
    private const int AwardedPokemonCount = 1;
    private const int WelcomeMailID = 5;

    private static readonly SystemMail WelcomeMail = new(
        Subject: "Mail_TextTitle_5_001",
        Content: "Mail_TextContent_5_001",
        Attachments: [],
        MailID: WelcomeMailID);

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> claimGates = new();

    public async Task<RoleMail> SendSystemMailAsync(ulong uid, SystemMail mail)
    {
        var saved = await InsertSystemMailAsync(uid, mail);
        await lobbyRoles.PushAsync(uid, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMailNewNtf, new pbSCMailNewNtf
        {
            MailList = new pbCOMDT_MAIL_LIST { MailType = saved.MailType, MailInfo = { BuildMailItem(saved) } },
        }));
        return saved;
    }

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

    public async Task<pbCOMDT_RED_DOT_DETAIL[]> BuildUnreadRedDotDetailsAsync(ulong uid)
    {
        var unread = await repository.CountUnreadAsync(uid, clock.GetUtcNow().ToUnixTimeSeconds());
        int[] redDotIDs =
        [
            .. Enumerable.Range(0, MailBox.MailTypeCount).Select(MailBox.FindUnreadRedDotID).OfType<int>(),
            MailBox.UnreadAllMailRedDotID,
        ];
        return
        [
            .. redDotIDs
                .Select(redDotID => new pbCOMDT_RED_DOT_DETAIL { RedDotID = redDotID, RedDotBadgeNum = CountUnread(unread, redDotID) })
                .Where(detail => detail.RedDotBadgeNum > 0),
        ];
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

    private async Task<RoleMail> InsertSystemMailAsync(ulong uid, SystemMail mail)
    {
        ValidateSystemMail(mail);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var accessInfo = new pbCOMDT_MAILACCESS_INFO
        {
            Access = { mail.Attachments.Select(award => new pbCOMDT_MAILACCESS { ResInfo = award.Clone() }) },
        };
        var saved = await repository.InsertAsync(new RoleMail
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
            SendTime = now,
            ExpireTime = now + ((long)mail.ValidDays * SecondsPerDay),
            AccessInfo = accessInfo.ToByteArray(),
            AccessState = mail.Attachments.Count > 0 ? MailBox.AccessUnget : MailBox.AccessNone,
        });
        logger.LogInformation(
            "Mail {MailIndex} of type {MailType} with {AttachmentCount} attachments sent to uid {Uid}",
            saved.MailIndex,
            saved.MailType,
            mail.Attachments.Count,
            uid);
        return saved;
    }

    public async Task<pbCOMDT_MAIL_LIST> GetMailListAsync(ulong uid, int mailType)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        await repository.DeleteExpiredAsync(uid, now);
        var mails = await repository.GetMailsAsync(uid, mailType, now, MailBox.MaxMailNum);
        return new pbCOMDT_MAIL_LIST { MailType = mailType, MailInfo = { mails.Select(BuildMailItem) } };
    }

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

    public static pbSCResourcesAwardNotify BuildResourcesAwardNotify(IEnumerable<MailClaim> claims)
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

        var (result, wallet, beforeCoinNums) = await repository.ClaimAsync(uid, mail.MailIndex, grant, now);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || wallet is null)
        {
            logger.LogInformation("Mail {MailIndex} claim refused for uid {Uid}: {Result}", mailIndex, uid, result);
            return (result, null);
        }

        foreach (var item in grant.Items)
        {
            role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
        }

        foreach (var pokemonId in grant.PokemonIds)
        {
            role.GrantPokemon(pokemonId, now, (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonMail);
        }

        mail.AccessState = MailBox.AccessGeted;
        mail.MailState = MailBox.Haveread;
        logger.LogInformation(
            "Mail {MailIndex} claimed by uid {Uid}: coins {Coins}, {ItemCount} items, Pokemon {PokemonIds}",
            mail.MailIndex,
            uid,
            string.Join(',', grant.Coins.Select(coin => $"{coin.Key}x{coin.Value}")),
            grant.Items.Count,
            string.Join(',', grant.PokemonIds));
        return (result, new MailClaim(mail, grant, wallet, beforeCoinNums));
    }

    private MailGrant? BuildGrant(LMNRoleInfo role, pbCOMDT_MAILACCESS_INFO accessInfo, long now)
    {
        var coins = new Dictionary<int, long>();
        var items = new List<RoleItemInfo>();
        var pokemonIds = new List<int>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        foreach (var award in accessInfo.Access.Select(access => access.ResInfo).OfType<pbCOMDT_ONE_RESOURCE_AWARD_INFO>())
        {
            if ((PB_ENUM_RES_RESOURCES_TYPE)award.ResType == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem)
            {
                if (UniteLicense.PlanGrant(gameData, role, award.ResID, award.ResCnt, pokemonIds) is not { } license)
                {
                    return null;
                }

                license.AddTo(pokemonIds, coins, awards);
                continue;
            }

            awards.Add(new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO { FinallyAwardInfo = award });
            switch ((PB_ENUM_RES_RESOURCES_TYPE)award.ResType)
            {
                case PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin:
                    coins[award.ResID] = coins.GetValueOrDefault(award.ResID) + award.ResCnt;
                    break;
                case PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar:
                    AddItemGrant(role, RoleItemType.AvatarItem, award.ResID, now, items);
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

        return new MailGrant(coins, items, pokemonIds, awards);
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
            || Encoding.UTF8.GetByteCount(mail.FromName) > MailBox.MaxFromName)
        {
            throw new ArgumentException("System mail does not fit the client mail limits", nameof(mail));
        }

        foreach (var award in mail.Attachments)
        {
            var valid = award.ResCnt > 0 && (PB_ENUM_RES_RESOURCES_TYPE)award.ResType switch
            {
                PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin => award.ResID > 0,
                PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar => gameData.AvatarItem.Contains(award.ResID),
                PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesLobbyBackground =>
                    gameData.LobbyBg.Contains(award.ResID) && award.ResID != gameData.DefaultLobbyBgId,
                PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon => award.ResID > 0 && award.ResCnt == AwardedPokemonCount,
                PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem => UniteLicense.IsUniteLicense(gameData, award.ResID),
                _ => false,
            };
            if (!valid)
            {
                throw new ArgumentException($"System mail attachment {award.ResType}:{award.ResID}x{award.ResCnt} cannot be granted", nameof(mail));
            }
        }
    }

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
        ParamInfo = new pbCOMDT_MAIL_CONTENT_PARAM(),
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
