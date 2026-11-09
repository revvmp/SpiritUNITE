using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Session;

public sealed class TssHandlers
{
    private const int AntiDataNtfVersion = 1;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAntiDataReq)]
    public GamePacket[] OnAntiDataReq(GamePacket request)
    {
        var antiData = pbCSAntiDataReq.Parser.ParseFrom(request.PkgData);
        var antiDataNtf = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAntiDataNtf, new pbSCAntiDataNtf { Buff = antiData.Buff });
        return [antiDataNtf with { Version = AntiDataNtfVersion }];
    }
}
