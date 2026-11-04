using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Session;

public sealed class StartupHandlers
{
    private const int LastResourceList = 1;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidServerSyncResListReq)]
    public GamePacket[] OnServerSyncResListReq(GamePacket request)
    {
        var response = new pbSCServerSyncResListNtf { IsLast = LastResourceList };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidServerSyncResListNtf, response)];
    }
}
