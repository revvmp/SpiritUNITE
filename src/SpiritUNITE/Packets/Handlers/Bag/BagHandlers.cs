using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Bag;

public sealed class BagHandlers(RoleInfoStore roles, BagManager bag, ILogger<BagHandlers> logger)
{
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
        var result = await bag.ValidateUseOutSideItemAsync(role.GameUlongUID, use.ResID, use.GridUniqueID, use.ResCnt);
        logger.LogInformation(
            "Use of {ResCnt} outside item {ResID} refused for uid {Uid}: {Result}",
            use.ResCnt,
            use.ResID,
            role.GameUlongUID,
            result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseOutSideItemRsp, new pbSCUseOutSideItemRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                ResID = use.ResID,
                GridUniqueID = use.GridUniqueID,
                ResCnt = use.ResCnt,
            }),
        ];
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
