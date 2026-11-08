using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.StarRoad;

public sealed class StarRoadHandlers(RoleInfoStore roles, StarRoadManager starRoad, ILogger<StarRoadHandlers> logger)
{
    private const PB_ENUM_RES_ERROR_CODE NoCustomPokemonOffered = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
    private const PB_ENUM_RES_ERROR_CODE NoGemPriceOffered = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
    private const PB_ENUM_RES_ERROR_CODE NoExclusiveScoreHeld = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadGetInfoReq)]
    public async Task<GamePacket[]> OnStarRoadGetInfoReq(GamePacket request)
    {
        pbCSStarRoadGetInfoReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var route = (await starRoad.SyncOwnedPokemonAsync(role)).Route;
        var response = new pbSCStarRoadGetInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            StarRoadInfo = await starRoad.BuildStarRoadInfoAsync(role, route, isLoginNtf: false),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadGetInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadPokemonTipsCloseReq)]
    public async Task<GamePacket[]> OnStarRoadPokemonTipsCloseReq(GamePacket request)
    {
        pbCSStarRoadPokemonTipsCloseReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        await starRoad.ClosePokemonTipsTodayAsync(role);
        logger.LogInformation("Uid {Uid} closed the License Journey Pokemon tips for today", request.Uid);
        var response = new pbSCStarRoadPokemonTipsCloseRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadPokemonTipsCloseRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadGetCustomPokemonListReq)]
    public GamePacket[] OnStarRoadGetCustomPokemonListReq(GamePacket request)
    {
        pbCSStarRoadGetCustomPokemonListReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var response = new pbSCStarRoadGetCustomPokemonListRsp
        {
            ResultInfo = ResultInfo(NoCustomPokemonOffered),
            CustomPokemonList = new pbCOMDT_STAR_ROAD_CUSTOM_POKEMON_LIST(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadGetCustomPokemonListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadSelectCustomPokemonReq)]
    public GamePacket[] OnStarRoadSelectCustomPokemonReq(GamePacket request)
    {
        var selection = pbCSStarRoadSelectCustomPokemonReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation(
            "License Journey custom Pokemon {PokemonId} refused for uid {Uid}: {Result}",
            selection.PokemonID, request.Uid, NoCustomPokemonOffered);
        var response = new pbSCStarRoadSelectCustomPokemonRsp { ResultInfo = ResultInfo(NoCustomPokemonOffered) };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadSelectCustomPokemonRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadBuyScoreReq)]
    public GamePacket[] OnStarRoadBuyScoreReq(GamePacket request)
    {
        var purchase = pbCSStarRoadBuyScoreReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation(
            "License Journey score purchase refused for uid {Uid}: site {SiteId}, Pokemon {PokemonId}, {Result}",
            request.Uid, purchase.SiteID, purchase.PokemonID, NoGemPriceOffered);
        var response = new pbSCStarRoadBuyScoreRsp { ResultInfo = ResultInfo(NoGemPriceOffered) };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadBuyScoreRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadReceivePokemonReq)]
    public async Task<GamePacket[]> OnStarRoadReceivePokemonReq(GamePacket request)
    {
        var receipt = pbCSStarRoadReceivePokemonReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var receive = await starRoad.ReceivePokemonAsync(role, receipt.SiteID, receipt.PokemonID);
        if (receive.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await roles.SaveAsync(role);
        }

        var response = new pbSCStarRoadReceivePokemonRsp { ResultInfo = ResultInfo(receive.Result) };
        return
        [
            .. receive.Packets.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadReceivePokemonRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadExchangeExclusiveScoreReq)]
    public GamePacket[] OnStarRoadExchangeExclusiveScoreReq(GamePacket request)
    {
        var exchange = pbCSStarRoadExchangeExclusiveScoreReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation(
            "License Journey exclusive score exchange of {ExclusiveScoreNum} refused for uid {Uid}: {Result}",
            exchange.ExclusiveScoreNum, request.Uid, NoExclusiveScoreHeld);
        var response = new pbSCStarRoadExchangeExclusiveScoreRsp { ResultInfo = ResultInfo(NoExclusiveScoreHeld) };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadExchangeExclusiveScoreRsp, response)];
    }

    private static pbResultInfo ResultInfo(PB_ENUM_RES_ERROR_CODE result) =>
        result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc ? new pbResultInfo() : new pbResultInfo { ResultId = (int)result };
}
