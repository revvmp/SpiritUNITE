using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class PokemonHandlers(RoleInfoStore roles, ILogger<PokemonHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetPokemonListReq)]
    public GamePacket[] OnGetPokemonListReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetPokemonListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetPokemonListRsp
        {
            ResultInfo = new pbResultInfo(),
            PokemonList = { role.BuildPokemonList() },
            PasterInfo = new pbCOMDT_POKEMON_PASTER_INFO(),
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetPokemonListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetPokemonFollowReq)]
    public async Task<GamePacket[]> OnSetPokemonFollowReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var pokemonId = pbCSSetPokemonFollowReq.Parser.ParseFrom(request.PkgData).PokemonId;
        var result = role.SetPokemonFollow(pokemonId);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation(
                "Follow Pokemon {PokemonId} refused for uid {Uid}: {Result}",
                pokemonId,
                role.GameUlongUID,
                result);
        }
        else
        {
            await roles.SaveAsync(role);
            logger.LogInformation("Follow Pokemon set to {PokemonId} for uid {Uid}", pokemonId, role.GameUlongUID);
        }

        var response = new pbSCSetPokemonFollowRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            PokemonId = role.FollowPokemonId,
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetPokemonFollowRsp, response)];
    }
}
