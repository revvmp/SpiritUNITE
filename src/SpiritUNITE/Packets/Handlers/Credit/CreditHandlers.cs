using SpiritUNITE.Game.Credit;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Credit;

public sealed class CreditHandlers(RoleInfoStore roles, CreditManager credit)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidReduceCreditPointListReq)]
    public async Task<GamePacket[]> OnReduceCreditPointListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSReduceCreditPointListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCReduceCreditPointListRsp
        {
            ResultInfo = new pbResultInfo(),
            RecordList = await credit.BuildReduceRecordListAsync(request.Uid),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidReduceCreditPointListRsp, response)];
    }
}
