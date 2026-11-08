using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Activity;

public sealed class WatchTheGameLiveHandlers(RoleInfoStore roles, ILogger<WatchTheGameLiveHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidWatchTheGameLiveReq)]
    public GamePacket[] OnWatchTheGameLiveReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var watch = pbCSWatchTheGameLiveReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation("Uid {Uid} opened the live stream of activity {ActivityID} with watch type {WatchGameType}", request.Uid, watch.ActivityID, watch.WatchGameType);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidWatchTheGameLiveRsp, new pbSCWatchTheGameLiveRsp { ResultInfo = new pbResultInfo() })];
    }
}
