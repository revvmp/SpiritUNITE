using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Room;

public sealed class MatchRoomHandlers(
    RoleInfoStore roles,
    ActivitySchedule schedule,
    ActivityManager activityManager,
    TimeProvider clock)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomDailyActivityRewardReq)]
    public async Task<GamePacket[]> OnRoomDailyActivityRewardReq(GamePacket request)
    {
        pbCSRoomDailyActivityRewardReq.Parser.ParseFrom(request.PkgData);
        var dailyActivityInfo = await RoomDailyActivity.BuildDailyActivityInfoAsync(
            schedule,
            activityManager,
            roles.Require(request.Uid),
            clock.GetUtcNow());
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomDailyActivityRewardRsp, new pbSCRoomDailyActivityRewardRsp
            {
                DailyActivityInfo = dailyActivityInfo,
            }),
        ];
    }
}
