using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Session;

public sealed class HeartBeatHandlers(TimeProvider clock)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGameSvrPing)]
    public GamePacket[] OnGameSvrPing(GamePacket request)
    {
        var ping = pbCSGameSvrPing.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGameSvrPingRsp { Time = clock.GetUtcNow().ToUnixTimeSeconds(), Seq = ping.Seq };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGameSvrPingRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidHeartBeatNtf)]
    public GamePacket[] OnHeartBeatNtf(GamePacket request) => [];
}
