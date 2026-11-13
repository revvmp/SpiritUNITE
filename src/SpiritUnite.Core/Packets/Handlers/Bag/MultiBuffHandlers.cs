using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Bag;

public sealed class MultiBuffHandlers(RoleInfoStore roles, MultiBuffManager multiBuffs, TimeProvider clock)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAllMultiBufInfoReq)]
    public async Task<GamePacket[]> OnGetAllMultiBufInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetAllMultiBufInfoReq.Parser.ParseFrom(request.PkgData);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var response = new pbSCGetAllMultiBufInfoRsp
        {
            MultiBuffDetail = await multiBuffs.BuildMultiBuffInfoAsync(role.GameUlongUID, now),
            CurTimeStamp = now,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAllMultiBufInfoRsp, response)];
    }
}
