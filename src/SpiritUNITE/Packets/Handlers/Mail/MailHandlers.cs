using Microsoft.Extensions.Logging;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Game.PokemonAvatar;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Mail;

public sealed class MailHandlers(
    RoleInfoStore roles,
    MailManager mails,
    StarRoadManager starRoad,
    GameData gameData,
    ILogger<MailHandlers> logger)
{
    private const int BatchDel = 1;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMailListReq)]
    public async Task<GamePacket[]> OnMailListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var mailType = pbCSMailListReq.Parser.ParseFrom(request.PkgData).MailType;
        if (!MailBox.IsMailType(mailType))
        {
            return
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailListRsp, new pbSCMailListRsp
                {
                    ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrMailInvalidType },
                    MailList = new pbCOMDT_MAIL_LIST { MailType = mailType },
                }),
            ];
        }

        var response = new pbSCMailListRsp { MailList = await mails.GetMailListAsync(request.Uid, mailType) };
        return
        [
            .. await BuildUnreadRedDotsAsync(request, mailType),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailListRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMailReadReq)]
    public async Task<GamePacket[]> OnMailReadReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var mailRead = pbCSMailReadReq.Parser.ParseFrom(request.PkgData).MailRead ?? new pbCOMDT_MAIL_READ();
        var response = new pbSCMailReadRsp { ResultInfo = new pbResultInfo() };
        var detail = MailBox.IsMailType(mailRead.MailType)
            ? await mails.ReadMailAsync(request.Uid, mailRead.MailType, mailRead.MailIndex)
            : null;
        if (detail is null)
        {
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrMailNotExist;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailReadRsp, response)];
        }

        response.MailDetail = detail;
        return
        [
            .. await BuildUnreadRedDotsAsync(request, mailRead.MailType),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailReadRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMailBatchReadReq)]
    public async Task<GamePacket[]> OnMailBatchReadReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var mailType = pbCSMailBatchReadReq.Parser.ParseFrom(request.PkgData).MailType;
        var response = new pbSCMailBatchReadRsp
        {
            ResultInfo = new pbResultInfo(),
            MailReadList = new pbCOMDT_MAIL_INDEX_LIST { MailType = mailType },
        };
        if (!MailBox.IsMailType(mailType))
        {
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrMailInvalidType;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailBatchReadRsp, response)];
        }

        response.MailReadList.MailIndex.Add(await mails.BatchReadAsync(request.Uid, mailType));
        return
        [
            .. await BuildUnreadRedDotsAsync(request, mailType),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailBatchReadRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMailDelReq)]
    public async Task<GamePacket[]> OnMailDelReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var mailDel = pbCSMailDelReq.Parser.ParseFrom(request.PkgData).MailDel ?? new pbCOMDT_MAIL_DEL();
        var response = new pbSCMailDelRsp
        {
            ResultInfo = new pbResultInfo(),
            MailDelList = new pbCOMDT_MAIL_INDEX_LIST { MailType = mailDel.MailType },
        };
        if (!MailBox.IsMailType(mailDel.MailType))
        {
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrMailInvalidType;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailDelRsp, response)];
        }

        var (result, deleted) = mailDel.BatchDel == BatchDel
            ? await mails.BatchDeleteMailAsync(request.Uid, mailDel.MailType)
            : await mails.DeleteMailAsync(request.Uid, mailDel.MailType, mailDel.MailIndex);
        response.ResultInfo.ResultId = (int)result;
        response.MailDelList.MailIndex.Add(deleted);
        logger.LogInformation("Uid {Uid} deleted {Count} mails of type {MailType}: {Result}", request.Uid, deleted.Length, mailDel.MailType, result);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailDelRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMailGetAccessReq)]
    public async Task<GamePacket[]> OnMailGetAccessReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var getAccess = pbCSMailGetAccessReq.Parser.ParseFrom(request.PkgData).MailGetAccess ?? new pbCOMDT_MAIL_GETACCESS();
        var response = new pbSCMailGetAccessRsp { ResultInfo = new pbResultInfo() };
        if (!MailBox.IsMailType(getAccess.MailType))
        {
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrMailInvalidType;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailGetAccessRsp, response)];
        }

        var (result, claim) = await mails.GetAccessAsync(role, getAccess.MailType, getAccess.MailIndex);
        response.ResultInfo.ResultId = (int)result;
        if (claim is null)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailGetAccessRsp, response)];
        }

        response.MailAccessDetail = MailManager.BuildAccessDetail(claim.Mail);
        return
        [
            .. await BuildClaimUpdatesAsync(request, role, [claim]),
            .. await BuildUnreadRedDotsAsync(request, getAccess.MailType),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailGetAccessRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMailBatchGetAccessReq)]
    public async Task<GamePacket[]> OnMailBatchGetAccessReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var mailType = pbCSMailBatchGetAccessReq.Parser.ParseFrom(request.PkgData).MailType;
        var response = new pbSCMailBatchGetAccessRsp { ResultInfo = new pbResultInfo() };
        if (!MailBox.IsMailType(mailType))
        {
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrMailInvalidType;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailBatchGetAccessRsp, response)];
        }

        var (result, claims) = await mails.BatchGetAccessAsync(role, mailType);
        response.ResultInfo.ResultId = (int)result;
        response.MailAccessDetail.Add(claims.Select(claim => MailManager.BuildAccessDetail(claim.Mail)));
        return
        [
            .. await BuildClaimUpdatesAsync(request, role, claims),
            .. claims.Length > 0 ? await BuildUnreadRedDotsAsync(request, mailType) : Array.Empty<GamePacket>(),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailBatchGetAccessRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMailSendReq)]
    public GamePacket[] OnMailSendReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var mailSend = pbCSMailSendReq.Parser.ParseFrom(request.PkgData).MailSend ?? new pbCOMDT_MAIL_SEND_INFO();
        logger.LogInformation("Uid {Uid} tried to send mail id {MailID} to uid {ToUid}", request.Uid, mailSend.MailID, mailSend.ToUid);
        var response = new pbSCMailSendRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrMailInvalidMailId },
            MailType = (int)PB_ENUM_RES_MAIL_TYPE.PbResMailTypePerson,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMailSendRsp, response)];
    }

    private async Task<GamePacket[]> BuildUnreadRedDotsAsync(GamePacket request, int mailType) =>
        [
            .. (await mails.BuildUnreadRedDotPacketsAsync(request.Uid, mailType))
                .Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
        ];

    private async Task<GamePacket[]> BuildClaimUpdatesAsync(GamePacket request, LMNRoleInfo role, IReadOnlyList<MailClaim> claims)
    {
        if (claims.Count == 0)
        {
            return [];
        }

        var packets = new List<GamePacket>();
        foreach (var item in claims.SelectMany(claim => claim.Grant.Items).Where(item => item.ItemType == RoleItemType.AvatarItem))
        {
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateAvatarNtf, new pbSCUpdateAvatarNtf
            {
                UpdateType = (int)PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeGain,
                AvatarInfo = item.ToAvatarInfo(),
            }));
        }

        if (PokemonAvatarManager.BuildGainNtf(claims.SelectMany(claim => claim.Grant.Items)) is { } pokemonAvatarNtf)
        {
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonAvatarNtf, pokemonAvatarNtf));
        }

        if (claims.SelectMany(claim => claim.ChangedBagItems).ToArray() is { Length: > 0 } changedBagItems)
        {
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(changedBagItems)));
        }

        if (claims.SelectMany(claim => claim.Grant.Items).Any(item => item.ItemType == RoleItemType.LobbyBg))
        {
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetLobbyBackgroundInfoRsp, new pbSCGetLobbyBackgroundInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                LobbyBackgroundInfo = role.BuildLobbyBackgroundInfo(gameData),
            }));
        }

        var wallet = claims[^1].Wallet;
        var specialCoinsBefore = FindSpecialCoinsBefore(claims);
        foreach (var (coinID, beforeCoinNum) in specialCoinsBefore)
        {
            packets.Add(request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, beforeCoinNum, wallet.GetCoinVal(coinID))));
        }

        var pokemonIds = claims.SelectMany(claim => claim.Grant.PokemonIds).ToArray();
        foreach (var pokemonId in pokemonIds)
        {
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemonId),
            }));
        }

        if (pokemonIds.Length > 0)
        {
            packets.AddRange(await starRoad.BuildPokemonGainPacketsAsync(role));
            packets.Add(StarRoadManager.BuildAddPokemonRedDot(role));
        }
        else if (specialCoinsBefore.TryGetValue(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, out var licensePointsBefore))
        {
            packets.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, licensePointsBefore));
        }

        if (claims.Any(claim => claim.Grant.Coins.Keys.Any(CoinID.WalletCoins.Contains)))
        {
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() }));
        }

        if (claims.Any(claim => claim.Grant.Coins.ContainsKey(CoinID.Coupons)))
        {
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp()));
        }

        packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, MailManager.BuildResourcesAwardNotify(claims)));
        return [.. packets.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq })];
    }

    private static Dictionary<int, long> FindSpecialCoinsBefore(IEnumerable<MailClaim> claims)
    {
        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var (coinID, beforeCoinNum) in claims.SelectMany(claim => claim.BeforeCoinNums).Where(coin => CoinID.IsSpecialCoin(coin.Key)))
        {
            specialCoinsBefore.TryAdd(coinID, beforeCoinNum);
        }

        return specialCoinsBefore;
    }
}
