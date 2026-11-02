using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class HeartbeatHandlers
{
    [PacketHandler(Connector.Lobby, PB_ENUM_CS_MSGID_DEF.PbCsidGameSvrPing)]
    public GamePacket[] AnswerPing(GamePacket request)
    {
        var ping = pbCSGameSvrPing.Parser.ParseFrom(request.Payload);
        var response = new pbSCGameSvrPingRsp { Time = ping.Time, Seq = ping.Seq };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGameSvrPingRsp, response)];
    }

    [PacketHandler(Connector.Lobby, PB_ENUM_CS_MSGID_DEF.PbCsidHeartBeatNtf)]
    public GamePacket[] AcceptHeartbeat(GamePacket request) => [];
}
