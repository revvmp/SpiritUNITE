using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Session;

public sealed class TssHandlers
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAntiDataReq)]
    public GamePacket[] OnAntiDataReq(GamePacket request)
    {
        pbCSAntiDataReq.Parser.ParseFrom(request.PkgData);
        return [];
    }
}
