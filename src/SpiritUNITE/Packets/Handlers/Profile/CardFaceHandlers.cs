using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Profile;

public sealed class CardFaceHandlers(RoleInfoStore roles, ProfileManager profiles, ILogger<CardFaceHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetCardFaceInfoReq)]
    public GamePacket[] OnGetCardFaceInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetCardFaceInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetCardFaceInfoNtf
        {
            ResultInfo = new pbResultInfo(),
            CardFaceList = profiles.BuildCardFaceList(role),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetCardFaceInfoNtf, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidWearCardFaceReq)]
    public async Task<GamePacket[]> OnWearCardFaceReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var wear = pbCSWearCardFaceReq.Parser.ParseFrom(request.PkgData);
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = profiles.WearCardFace(role, style, wear.PokemonID, wear.CardFaceID);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await profiles.SaveWearCardFaceAsync(role.GameUlongUID, style, wear.PokemonID);
        }

        logger.LogInformation(
            "Card face {CardFaceID} on Pokemon {PokemonID} for uid {Uid}: {Result}",
            wear.CardFaceID,
            wear.PokemonID,
            role.GameUlongUID,
            result);
        var response = new pbSCWearCardFaceRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            PokemonID = wear.PokemonID,
            CardFaceID = style.GetWearCardFaceID(wear.PokemonID),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidWearCardFaceRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearCardFaceNewTagReq)]
    public async Task<GamePacket[]> OnClearCardFaceNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var cardFaceID = pbCSClearCardFaceNewTagReq.Parser.ParseFrom(request.PkgData).CardFaceID;
        var result = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        if (!role.TryClearOwnedNewTag(RoleItemType.CardFace, cardFaceID, out var cleared))
        {
            logger.LogInformation("Card face new tag clear refused for uid {Uid}: {CardFaceID} not owned", role.GameUlongUID, cardFaceID);
            result = PB_ENUM_RES_ERROR_CODE.PbErrCardFaceNotExist;
        }
        else if (cleared.Length > 0)
        {
            await roles.SaveAsync(role);
        }

        var response = new pbSCClearCardFaceNewTagRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            CardFaceID = cardFaceID,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearCardFaceNewTagRsp, response)];
    }
}
