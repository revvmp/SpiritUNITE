using SpiritUnite.Game.Role;
using SpiritUnite.Game.Video;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Video;

public sealed class VideoHistoryHandlers(RoleInfoStore roles, VideoHistoryManager videoHistory)
{
    private const int VideoPlaying = 1;
    private const int VideoStopped = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidVideoHistoryReq)]
    public async Task<GamePacket[]> OnVideoHistoryReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSVideoHistoryReq.Parser.ParseFrom(request.PkgData);
        var view = await videoHistory.BuildHistoryAsync(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidVideoHistoryRsp, new pbSCVideoHistoryRsp
        {
            ResultInfo = BuildResultInfo(view),
            VideoInfo = view.VideoInfo,
            IsVideoDelByVersion = view.IsVideoDelByVersion,
            IsDelVideoGuideTip = view.IsDelVideoGuideTip,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidVideoHistoryGuideEndReq)]
    public async Task<GamePacket[]> OnVideoHistoryGuideEndReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSVideoHistoryGuideEndReq.Parser.ParseFrom(request.PkgData);
        await videoHistory.EndGuideAsync(request.Uid);
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidVideoItemSaveReq)]
    public async Task<GamePacket[]> OnVideoItemSaveReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var save = pbCSVideoItemSaveReq.Parser.ParseFrom(request.PkgData);
        var view = await videoHistory.SaveAsync(request.Uid, save.AbsId, save.BattleInfo);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidVideoItemSaveRsp, new pbSCVideoItemSaveRsp { ResultInfo = BuildResultInfo(view), VideoInfo = view.VideoInfo })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidVideoItemCollectReq)]
    public async Task<GamePacket[]> OnVideoItemCollectReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var collect = pbCSVideoItemCollectReq.Parser.ParseFrom(request.PkgData);
        var view = await videoHistory.CollectAsync(request.Uid, collect.AbsId, collect.IsCollect);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidVideoItemCollectRsp, new pbSCVideoItemCollectRsp { ResultInfo = BuildResultInfo(view), VideoInfo = view.VideoInfo })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidVideoItemDeleteReq)]
    public async Task<GamePacket[]> OnVideoItemDeleteReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var delete = pbCSVideoItemDeleteReq.Parser.ParseFrom(request.PkgData);
        var view = await videoHistory.DeleteAsync(request.Uid, delete.AbsId);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidVideoItemDeleteRsp, new pbSCVideoItemDeleteRsp { ResultInfo = BuildResultInfo(view), VideoInfo = view.VideoInfo })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidVideoPlayStatusReq)]
    public GamePacket[] OnVideoPlayStatusReq(GamePacket request)
    {
        var status = pbCSVideoPlayStatusReq.Parser.ParseFrom(request.PkgData).VideoPlayStatus;
        var result = status is VideoPlaying or VideoStopped ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidVideoPlayStatusRsp, new pbSCVideoPlayStatusRsp { ResultInfo = new pbResultInfo { ResultId = (int)result } })];
    }

    private static pbResultInfo BuildResultInfo(VideoHistoryView view) => new() { ResultId = (int)view.Result };
}
