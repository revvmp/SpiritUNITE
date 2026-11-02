using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class StartupHandlers
{
    private const int LastResourceList = 1;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidServerSyncResListReq)]
    public GamePacket[] OnServerSyncResListReq(GamePacket request)
    {
        var response = new pbSCServerSyncResListNtf { IsLast = LastResourceList };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidServerSyncResListNtf, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetTaskInfoReq)]
    public GamePacket[] OnGetTaskInfoReq(GamePacket request)
    {
        var query = pbCSGetTaskInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetTaskInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            ModuleType = query.ModuleType,
            TaskSysExtraInfo = new pbCOMDT_TASK_SYSTEM_EXTRA_INFO(),
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetTaskInfoRsp, response)];
    }
}
