using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class PokemonHandlers(RoleInfoStore roles)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetPokemonListReq)]
    public GamePacket[] OnGetPokemonListReq(GamePacket request)
    {
        var role = roles.Find(request.Uid) ?? throw new InvalidDataException("Message requires a logged in role");
        pbCSGetPokemonListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetPokemonListRsp
        {
            ResultInfo = new pbResultInfo(),
            PokemonList = { role.BuildPokemonList() },
            PasterInfo = new pbCOMDT_POKEMON_PASTER_INFO(),
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetPokemonListRsp, response)];
    }
}
