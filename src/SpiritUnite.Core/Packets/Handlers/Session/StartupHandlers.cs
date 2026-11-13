using Microsoft.Extensions.Logging;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Resource;

namespace SpiritUnite.Packets.Handlers.Session;

public sealed class StartupHandlers(ServerSyncResources serverSyncResources, ILogger<StartupHandlers> logger)
{
    private const int LastResourceList = 1;
    private const int SingleSegment = 1;
    private const int MaxResNameLength = 64;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidServerSyncResListReq)]
    public GamePacket[] OnServerSyncResListReq(GamePacket request)
    {
        pbCSServerSyncResListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCServerSyncResListNtf
        {
            IsLast = LastResourceList,
            SvrResVersion = ServerSyncResources.OfficialSvrResVersion,
        };
        response.ResList.AddRange(serverSyncResources.Resources.Select(resource => resource.ToResInfo(withResType: true)));
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidServerSyncResListNtf, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidResBytesReq)]
    public GamePacket[] OnResBytesReq(GamePacket request)
    {
        var resBytes = pbCSResBytesReq.Parser.ParseFrom(request.PkgData);
        var resource = resBytes.ResName.Length <= MaxResNameLength ? serverSyncResources.Find(resBytes.ResName) : null;
        if (resource is null || resBytes.CurPos != 0)
        {
            logger.LogWarning(
                "ResBytesReq from uid {Uid} names no server sync resource segment (name length {Length}, CurPos {CurPos})",
                request.Uid,
                resBytes.ResName.Length,
                resBytes.CurPos);
            return [];
        }

        var response = new pbSCResBytesRsp
        {
            TotalNum = SingleSegment,
            ResInfo = resource.ToResInfo(withResType: false),
            ResBytes = new pbCOMDT_RES_BYTES { Bytes = resource.Bytes },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidResBytesRsp, response)];
    }
}
