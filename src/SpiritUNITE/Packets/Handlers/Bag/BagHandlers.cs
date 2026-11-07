using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Bag;

public sealed class BagHandlers(
    RoleInfoStore roles,
    BagManager bag,
    RankedManager ranked,
    StarRoadManager starRoad,
    TaskUpdates taskUpdates,
    ILogger<BagHandlers> logger)
{
    private const int OpenAwardBoxReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonOpenawardbox;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidOutSideItemPackageAllInfoReq)]
    public async Task<GamePacket[]> OnOutSideItemPackageAllInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSOutSideItemPackageAllInfoReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemPackageAllInfoRsp,
                await bag.BuildOutSideItemPackageAllInfoRspAsync(role.GameUlongUID)),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSellOutSideItemReq)]
    public async Task<GamePacket[]> OnSellOutSideItemReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var sell = pbCSSellOutSideItemReq.Parser.ParseFrom(request.PkgData);
        var outcome = await bag.SellOutSideItemAsync(role.GameUlongUID, sell.ResID, sell.GridUniqueID, sell.ResCnt);
        logger.LogInformation(
            "Sell {ResCnt} of outside item {ResID} for uid {Uid}: {Result}",
            sell.ResCnt,
            sell.ResID,
            role.GameUlongUID,
            outcome.Result);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSellOutSideItemRsp, new pbSCSellOutSideItemRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            ResID = sell.ResID,
            GridUniqueID = sell.GridUniqueID,
            ResCnt = sell.ResCnt,
        });
        return outcome.Succeeded ? [.. BuildBagUpdates(request, outcome), reply] : [reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUseOutSideItemReq)]
    public async Task<GamePacket[]> OnUseOutSideItemReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var use = pbCSUseOutSideItemReq.Parser.ParseFrom(request.PkgData);
        if (bag.IsFreeTicketCard(use.ResID))
        {
            return await UseFreeTicketCardAsync(request, role, use);
        }

        var outcome = await bag.UseOutSideItemAsync(role, use.ResID, use.GridUniqueID, use.ResCnt);
        logger.LogInformation(
            "Use of {ResCnt} outside item {ResID} by uid {Uid}: {Result}",
            use.ResCnt,
            use.ResID,
            role.GameUlongUID,
            outcome.Result);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseOutSideItemRsp, new pbSCUseOutSideItemRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            ResID = use.ResID,
            GridUniqueID = use.GridUniqueID,
            ResCnt = use.ResCnt,
        });
        return outcome.Succeeded ? [.. await BuildUseAwardPacketsAsync(request, role, outcome), reply] : [reply];
    }

    private async Task<GamePacket[]> UseFreeTicketCardAsync(GamePacket request, LMNRoleInfo role, pbCSUseOutSideItemReq use)
    {
        var cardUse = await ranked.UseFreeTicketCardAsync(role.GameUlongUID, use.ResID, use.GridUniqueID, use.ResCnt);
        logger.LogInformation(
            "Use of {ResCnt} free ticket card {ResID} by uid {Uid}: {Result}",
            use.ResCnt,
            use.ResID,
            role.GameUlongUID,
            cardUse.Result);
        var packets = new List<GamePacket>();
        if (cardUse.ChangedItems.Count > 0)
        {
            packets.Add(request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify,
                BagManager.BuildOutSideItemUpdateNotify(cardUse.ChangedItems)));
        }

        if (cardUse.InfoNtf is { } infoNtf)
        {
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRankedMatchInfoNtf, infoNtf));
        }

        packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseOutSideItemRsp, new pbSCUseOutSideItemRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)cardUse.Result },
            ResID = use.ResID,
            GridUniqueID = use.GridUniqueID,
            ResCnt = use.ResCnt,
        }));
        return [.. packets];
    }

    private async Task<GamePacket[]> BuildUseAwardPacketsAsync(GamePacket request, LMNRoleInfo role, BagOutcome outcome)
    {
        var packets = new List<GamePacket>();
        if (outcome.ChangedItems.Any(item => item.ItemType == RoleItemType.OutSideItem))
        {
            packets.Add(request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify,
                BagManager.BuildOutSideItemUpdateNotify(outcome.ChangedItems)));
        }

        packets.Add(request.Reply(
            PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf,
            await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)));
        if (outcome.Wallet is { } specialCoinWallet)
        {
            foreach (var specialCoinID in outcome.GrantedSpecialCoinIDs)
            {
                packets.Add(request.Reply(
                    PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                    SpecialCoin.BuildSpecialCoinChangeNtf(
                        specialCoinID,
                        outcome.BeforeCoinNums[specialCoinID],
                        specialCoinWallet.GetCoinVal(specialCoinID))));
            }

            if (outcome.GrantedPokemonIds.Count == 0
                && outcome.BeforeCoinNums.TryGetValue(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, out var licensePointsBefore))
            {
                packets.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, licensePointsBefore));
            }
        }

        foreach (var pokemonId in outcome.GrantedPokemonIds)
        {
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemonId),
            }));
        }

        if (outcome.GrantedPokemonIds.Count > 0)
        {
            packets.AddRange(await starRoad.BuildPokemonGainPacketsAsync(role));
            packets.Add(StarRoadManager.BuildAddPokemonRedDot(role));
        }

        if (outcome is { GrantedWalletCoins: true, Wallet: { } wallet })
        {
            packets.Add(request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf,
                new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() }));
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp()));
        }

        packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = OpenAwardBoxReason,
            ResourcesList = { outcome.Awards },
        }));
        packets.Add(request.Reply(
            PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf,
            await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)));
        return [.. packets.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearOutSideItemRedDotReq)]
    public async Task<GamePacket[]> OnClearOutSideItemRedDotReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var gridUniqueID = pbCSClearOutSideItemRedDotReq.Parser.ParseFrom(request.PkgData).GridUniqueID;
        var result = await bag.ClearOutSideItemRedDotAsync(role.GameUlongUID, gridUniqueID);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearOutSideItemRedDotRsp, new pbSCClearOutSideItemRedDotRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                GridUniqueID = gridUniqueID,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUseInfoReq)]
    public GamePacket[] OnUseInfoReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var useInfo = pbCSUseInfoReq.Parser.ParseFrom(request.PkgData);
        var info = BagManager.BuildUseInfo(useInfo.Type);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseInfoRsp, new pbSCUseInfoRsp
            {
                ResultInfo = new pbResultInfo
                {
                    ResultId = (int)(info is null ? PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc),
                },
                Type = useInfo.Type,
                ID = useInfo.ID,
                Info = info ?? new pbCOMDT_REQ_INFO(),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUseRenameCardReq)]
    public async Task<GamePacket[]> OnUseRenameCardReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var use = pbCSUseRenameCardReq.Parser.ParseFrom(request.PkgData);
        var outcome = await bag.UseRenameCardAsync(role, use.ResID, use.GridUniqueID, use.NewUserName);
        logger.LogInformation(
            "Rename card {ResID} used by uid {Uid}: {Result}",
            use.ResID,
            role.GameUlongUID,
            outcome.Result);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseRenameCardRsp, new pbSCUseRenameCardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            NewUserName = use.NewUserName,
        });
        return outcome.Succeeded ? [.. BuildBagUpdates(request, outcome), reply] : [reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidOpenBagEventReq)]
    public GamePacket[] OnOpenBagEventReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSOpenBagEventReq.Parser.ParseFrom(request.PkgData);
        return [];
    }

    public static IEnumerable<GamePacket> BuildBagUpdates(GamePacket request, BagOutcome outcome)
    {
        if (outcome.ChangedItems.Any(item => item.ItemType == RoleItemType.OutSideItem))
        {
            yield return request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify,
                BagManager.BuildOutSideItemUpdateNotify(outcome.ChangedItems));
        }

        if (outcome.Wallet is { } wallet)
        {
            yield return request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf,
                new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() });
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp());
        }
    }
}
