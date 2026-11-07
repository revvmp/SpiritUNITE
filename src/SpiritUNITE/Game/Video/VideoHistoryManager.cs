using System.Globalization;
using System.Text.RegularExpressions;
using Google.Protobuf;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Video;

public sealed record VideoHistoryView(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_VIDEO_HISTORY? VideoInfo, int IsVideoDelByVersion, int IsDelVideoGuideTip)
{
    public static VideoHistoryView Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null, 0, 0);
}

public sealed partial class VideoHistoryManager(
    VideoHistoryRepository repository,
    FightHistoryRepository fightHistory,
    GameData gameData,
    TimeProvider clock)
{
    public const int Collected = 1;
    public const int NotCollected = 0;
    public const int MaxAbsIdLength = 64;

    private const int NormalSave = 0;
    private const int TipShown = 1;
    private const int NoTip = 0;
    private const int ReservedUncollectedSlots = 1;

    private readonly long videoSaveContinueTime = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalVideoSaveContinueTime);
    private readonly int normalBattlevideoLimitMax = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalNormalBattlevideoLimitMax);
    private readonly int maxCollected = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalSubscriptionBattlevideoLimitMax) - ReservedUncollectedSlots;

    public async Task<VideoHistoryView> BuildHistoryAsync(ulong uid)
    {
        var videos = await LoadCurrentAsync(uid);
        var tip = await repository.FindTipAsync(uid);
        if (tip.VideoDelByVersion != NoTip)
        {
            await repository.SaveTipAsync(uid, tip with { VideoDelByVersion = NoTip });
        }

        return new VideoHistoryView(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, BuildVideoInfo(videos, tip), tip.VideoDelByVersion, tip.DelVideoGuideTip);
    }

    public async Task EndGuideAsync(ulong uid)
    {
        var tip = await repository.FindTipAsync(uid);
        if (tip.DelVideoGuideTip != NoTip)
        {
            await repository.SaveTipAsync(uid, tip with { DelVideoGuideTip = NoTip });
        }
    }

    public async Task<VideoHistoryView> SaveAsync(ulong uid, string absId, pbCOMDT_FIGHT_HISTORY_RECORD? battleInfo)
    {
        if (battleInfo is null || ParseAbsDeskSeq(absId) is not { } absDeskSeq || absDeskSeq != battleInfo.DeskSeq)
        {
            return VideoHistoryView.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        if (await FindOwnFightRecordAsync(uid, battleInfo.DeskID, battleInfo.DeskSeq) is not { } record)
        {
            return VideoHistoryView.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        var videos = await LoadCurrentAsync(uid);
        if (videos.All(video => video.AbsId != absId))
        {
            var replaced = videos
                .OrderBy(video => video.SaveTime)
                .Take(Math.Max(0, videos.Length - normalBattlevideoLimitMax + 1))
                .Select(video => video.AbsId)
                .ToArray();
            var version = BattleVersion.Build();
            await repository.SaveAsync(
                new RoleVideoHistory
                {
                    GameUlongUID = uid,
                    AbsId = absId,
                    SaveTime = clock.GetUtcNow().ToUnixTimeSeconds(),
                    CollectFlag = NotCollected,
                    BattleVersionSection1 = version.Section1,
                    BattleVersionSection2 = version.Section2,
                    FightHistoryRecord = record,
                },
                replaced);
        }

        return await BuildHistoryAsync(uid);
    }

    public async Task<VideoHistoryView> CollectAsync(ulong uid, string absId, int isCollect)
    {
        if (isCollect is not (Collected or NotCollected) || !IsValidAbsId(absId))
        {
            return VideoHistoryView.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        var videos = await LoadCurrentAsync(uid);
        if (videos.FirstOrDefault(video => video.AbsId == absId) is not { } target)
        {
            return VideoHistoryView.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        if (isCollect == Collected && target.CollectFlag != Collected && videos.Count(video => video.CollectFlag == Collected) >= maxCollected)
        {
            return VideoHistoryView.Refused(PB_ENUM_RES_ERROR_CODE.PbErrVideoHistoryCollectFull);
        }

        await repository.SetCollectFlagAsync(uid, absId, isCollect);
        return await BuildHistoryAsync(uid);
    }

    public async Task<VideoHistoryView> DeleteAsync(ulong uid, string absId)
    {
        if (!IsValidAbsId(absId))
        {
            return VideoHistoryView.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        await repository.DeleteAsync(uid, [absId]);
        return await BuildHistoryAsync(uid);
    }

    public static bool IsValidAbsId(string absId) => absId.Length <= MaxAbsIdLength && AbsFileName().IsMatch(absId);

    private static long? ParseAbsDeskSeq(string absId) =>
        absId.Length <= MaxAbsIdLength && AbsFileName().Match(absId) is { Success: true } match
            && long.TryParse(match.Groups["DeskSeq"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var deskSeq)
            ? deskSeq
            : null;

    private async Task<RoleVideoHistory[]> LoadCurrentAsync(ulong uid)
    {
        var videos = await repository.ListAsync(uid);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var version = BattleVersion.Build();
        var expired = videos.Where(video => video.SaveTime + videoSaveContinueTime <= now).Select(video => video.AbsId).ToHashSet();
        var outdated = videos
            .Where(video => !expired.Contains(video.AbsId) && (video.BattleVersionSection1 != version.Section1 || video.BattleVersionSection2 != version.Section2))
            .Select(video => video.AbsId)
            .ToHashSet();
        if (expired.Count == 0 && outdated.Count == 0)
        {
            return videos;
        }

        await repository.DeleteAsync(uid, [.. expired, .. outdated]);
        var tip = await repository.FindTipAsync(uid);
        await repository.SaveTipAsync(uid, new VideoHistoryTip(
            expired.Count > 0 ? TipShown : tip.DelVideoGuideTip,
            outdated.Count > 0 ? TipShown : tip.VideoDelByVersion));
        return [.. videos.Where(video => !expired.Contains(video.AbsId) && !outdated.Contains(video.AbsId))];
    }

    private async Task<byte[]?> FindOwnFightRecordAsync(ulong uid, int deskID, long deskSeq)
    {
        foreach (var stored in await fightHistory.ListAsync(uid))
        {
            var record = pbCOMDT_FIGHT_HISTORY_RECORD.Parser.ParseFrom(stored.FightHistoryRecord);
            if (record.DeskID == deskID && record.DeskSeq == deskSeq)
            {
                return stored.FightHistoryRecord;
            }
        }

        return null;
    }

    private static pbCOMDT_VIDEO_HISTORY BuildVideoInfo(IEnumerable<RoleVideoHistory> videos, VideoHistoryTip tip) => new()
    {
        VideoList =
        {
            videos.Select(video => new pbCOMDT_VIDEO_HISTORY_ITEM
            {
                AbsId = video.AbsId,
                SaveTime = video.SaveTime,
                CollectFlag = video.CollectFlag,
                BattleInfo = pbCOMDT_FIGHT_HISTORY_RECORD.Parser.ParseFrom(ByteString.CopyFrom(video.FightHistoryRecord)),
                BattleVersion = new pbCOMDT_BATTLE_VERSION { Section1 = video.BattleVersionSection1, Section2 = video.BattleVersionSection2 },
                IsTssSave = NormalSave,
            }),
        },
        DelVideoGuideTip = tip.DelVideoGuideTip,
    };

    [GeneratedRegex(@"^[0-9]{1,5}_MD5_(?<DeskSeq>-?[0-9]{1,19})_Seq\.abs$", RegexOptions.CultureInvariant)]
    private static partial Regex AbsFileName();
}
