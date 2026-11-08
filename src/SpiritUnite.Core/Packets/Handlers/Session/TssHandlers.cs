using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Session;

public sealed class TssHandlers
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAntiDataReq)]
    public GamePacket[] OnAntiDataReq(GamePacket request)
    {
        var antiData = pbCSAntiDataReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAntiDataNtf, new pbSCAntiDataNtf { Buff = antiData.Buff })];
    }
}
