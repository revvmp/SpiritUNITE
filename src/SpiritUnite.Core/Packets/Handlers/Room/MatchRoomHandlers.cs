using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Room;
using SpiritUnite.Online;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Room;

public sealed class MatchRoomHandlers(
    RoleInfoStore roles,
    RoomManager rooms,
    LobbyRoles lobbyRoles,
    ActivitySchedule schedule,
    ActivityManager activityManager,
    IOptions<GVoiceOptions> gVoice,
    TimeProvider clock,
    ILogger<MatchRoomHandlers> logger)
{
    private const int ManualIdc = 0;
    private const int AutoIdc = 1;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSelectIdcReq)]
    public async Task<GamePacket[]> OnSelectIdcReq(GamePacket request)
    {
        var selection = pbCSSelectIdcReq.Parser.ParseFrom(request.PkgData);
        var accepted = selection.IdcTag == gVoice.Value.IdcTag && selection.IsAuto is ManualIdc or AutoIdc;
        logger.LogInformation("IDC selection by uid {Uid}: tag {IdcTag}, auto {IsAuto}, accepted {Accepted}",
            request.Uid, selection.IdcTag, selection.IsAuto, accepted);
        if (!accepted)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSelectIdcRsp, new pbSCSelectIdcRsp { ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid } })];
        }

        foreach (var push in rooms.SelectIdc(request.Uid, selection.IdcTag).Pushes)
        {
            await lobbyRoles.PushAsync(push.Uid, [GamePacket.Create(push.Message.MsgID, push.Message.Message)]);
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSelectIdcRsp, new pbSCSelectIdcRsp { ResultInfo = new pbResultInfo(), IdcTag = selection.IdcTag, IsAuto = selection.IsAuto })];
    }

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
