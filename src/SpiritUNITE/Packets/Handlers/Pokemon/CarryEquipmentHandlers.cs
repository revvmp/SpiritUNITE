using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Pokemon;

public sealed class CarryEquipmentHandlers(RoleInfoStore roles, PokemonManager pokemon, ILogger<CarryEquipmentHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChgPokemonCarryEquipmentReq)]
    public async Task<GamePacket[]> OnChgPokemonCarryEquipmentReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var chg = pbCSChgPokemonCarryEquipmentReq.Parser.ParseFrom(request.PkgData);
        var result = await pokemon.ChgPokemonCarryEquipmentAsync(role, chg.PokemonId, chg.CarryEquipmentPlanInfo);
        logger.LogInformation(
            "Carry equipment plan for Pokemon {PokemonId} of uid {Uid}: {Result}",
            chg.PokemonId,
            role.GameUlongUID,
            result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChgPokemonCarryEquipmentRsp, new pbSCChgPokemonCarryEquipmentRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                PokemonId = chg.PokemonId,
                CarryEquipmentPlanInfo = await pokemon.FindCarryEquipmentPlanAsync(role.GameUlongUID, chg.PokemonId),
            }),
        ];
    }
}
