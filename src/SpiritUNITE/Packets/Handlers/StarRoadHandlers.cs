using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class StarRoadHandlers(GameData gameData, ILogger<StarRoadHandlers> logger)
{
    private readonly StarRoadProgress progress = StarRoadProgress.Starting(gameData);

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadGetInfoReq)]
    public GamePacket[] OnStarRoadGetInfoReq(GamePacket request)
    {
        pbCSStarRoadGetInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCStarRoadGetInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            StarRoadInfo = progress.BuildStarRoadInfo(isLoginNtf: false),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadGetInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadGetCustomPokemonListReq)]
    public GamePacket[] OnStarRoadGetCustomPokemonListReq(GamePacket request)
    {
        pbCSStarRoadGetCustomPokemonListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCStarRoadGetCustomPokemonListRsp
        {
            ResultInfo = new pbResultInfo(),
            CustomPokemonList = new pbCOMDT_STAR_ROAD_CUSTOM_POKEMON_LIST(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadGetCustomPokemonListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadSelectCustomPokemonReq)]
    public GamePacket[] OnStarRoadSelectCustomPokemonReq(GamePacket request)
    {
        var selection = pbCSStarRoadSelectCustomPokemonReq.Parser.ParseFrom(request.PkgData);
        var result = StarRoadProgress.SelectCustomPokemon();
        logger.LogInformation(
            "License Journey custom Pokemon {PokemonId} refused for uid {Uid}: {Result}",
            selection.PokemonID, request.Uid, result);
        var response = new pbSCStarRoadSelectCustomPokemonRsp { ResultInfo = ResultInfo(result) };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadSelectCustomPokemonRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadBuyScoreReq)]
    public GamePacket[] OnStarRoadBuyScoreReq(GamePacket request)
    {
        var purchase = pbCSStarRoadBuyScoreReq.Parser.ParseFrom(request.PkgData);
        var result = progress.BuyScore(purchase.SiteID, purchase.PokemonID);
        logger.LogInformation(
            "License Journey score purchase refused for uid {Uid}: site {SiteId}, Pokemon {PokemonId}, {Result}",
            request.Uid, purchase.SiteID, purchase.PokemonID, result);
        var response = new pbSCStarRoadBuyScoreRsp { ResultInfo = ResultInfo(result) };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadBuyScoreRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadReceivePokemonReq)]
    public GamePacket[] OnStarRoadReceivePokemonReq(GamePacket request)
    {
        var receipt = pbCSStarRoadReceivePokemonReq.Parser.ParseFrom(request.PkgData);
        var result = progress.ReceivePokemon(receipt.SiteID, receipt.PokemonID);
        logger.LogInformation(
            "License Journey Pokemon receipt refused for uid {Uid}: site {SiteId}, Pokemon {PokemonId}, {Result}",
            request.Uid, receipt.SiteID, receipt.PokemonID, result);
        var response = new pbSCStarRoadReceivePokemonRsp { ResultInfo = ResultInfo(result) };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadReceivePokemonRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStarRoadExchangeExclusiveScoreReq)]
    public GamePacket[] OnStarRoadExchangeExclusiveScoreReq(GamePacket request)
    {
        var exchange = pbCSStarRoadExchangeExclusiveScoreReq.Parser.ParseFrom(request.PkgData);
        var result = StarRoadProgress.ExchangeExclusiveScore();
        logger.LogInformation(
            "License Journey exclusive score exchange of {ExclusiveScoreNum} refused for uid {Uid}: {Result}",
            exchange.ExclusiveScoreNum, request.Uid, result);
        var response = new pbSCStarRoadExchangeExclusiveScoreRsp { ResultInfo = ResultInfo(result) };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadExchangeExclusiveScoreRsp, response)];
    }

    private static pbResultInfo ResultInfo(PB_ENUM_RES_ERROR_CODE result) => new() { ResultId = (int)result };
}
