using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed record CampBattleOutcome(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_CAMPBATTLE_INFO PersonalInfo);

public sealed class CampBattleManager(
    CampBattleRepository repository,
    ActivitySchedule schedule,
    TimeProvider clock,
    ILogger<CampBattleManager> logger)
{
    public const int BlueCamp = 1;
    public const int RedCamp = 2;

    private const int NoCamp = 0;
    private const int NoWinCamp = 0;

    private static readonly int[] Camps = [BlueCamp, RedCamp];

    public async Task<pbSCCampBattleActInfoRsp> InfoAsync(ulong uid)
    {
        if (FindOpenActivity() is not { } activity)
        {
            return new pbSCCampBattleActInfoRsp
            {
                ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit),
                PersonalInfo = new pbCOMDT_CAMPBATTLE_INFO(),
                ActivityInfo = new pbCOMDT_CAMPBATTLE_ACTIVITY_GLOBAL_INFO(),
            };
        }

        var activityID = activity.Row.ActivityID;
        var totals = (await repository.GetCampTotalsAsync(activityID)).ToDictionary(total => total.Camp);
        var campList = Camps.Select(camp => totals.TryGetValue(camp, out var total)
            ? new pbCOMDT_CAMPBATTLE_CAMP_INFO { Camp = camp, TotalScore = total.TotalScore, MemberNum = total.MemberNum, RealScore = total.TotalScore }
            : new pbCOMDT_CAMPBATTLE_CAMP_INFO { Camp = camp }).ToArray();
        return new pbSCCampBattleActInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            PersonalInfo = BuildPersonalInfo(await repository.FindAsync(uid, activityID)),
            ActivityInfo = new pbCOMDT_CAMPBATTLE_ACTIVITY_GLOBAL_INFO { WinCamp = NoWinCamp, CampList = { campList } },
            GuideCamp = campList.MinBy(camp => camp.MemberNum)!.Camp,
        };
    }

    public async Task<CampBattleOutcome> JoinAsync(ulong uid, int joinCamp)
    {
        if (FindOpenActivity() is not { } activity)
        {
            return new CampBattleOutcome(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, new pbCOMDT_CAMPBATTLE_INFO());
        }

        var activityID = activity.Row.ActivityID;
        if (!Camps.Contains(joinCamp))
        {
            return new CampBattleOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, BuildPersonalInfo(await repository.FindAsync(uid, activityID)));
        }

        var row = new RoleCampBattle { GameUlongUID = uid, ActivityID = activityID, Camp = joinCamp, JoinTime = clock.GetUtcNow().ToUnixTimeSeconds() };
        if (!await repository.JoinAsync(row))
        {
            return new CampBattleOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, BuildPersonalInfo(await repository.FindAsync(uid, activityID)));
        }

        logger.LogInformation("Uid {Uid} joined camp {Camp} of camp battle {ActivityID}", uid, joinCamp, activityID);
        return new CampBattleOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, BuildPersonalInfo(row));
    }

    public async Task<CampBattleOutcome> AwardAsync(ulong uid)
    {
        if (FindOpenActivity() is not { } activity)
        {
            return new CampBattleOutcome(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, new pbCOMDT_CAMPBATTLE_INFO());
        }

        var personal = await repository.FindAsync(uid, activity.Row.ActivityID);
        logger.LogInformation("Camp battle {ActivityID} award of uid {Uid} refused: no camp result exists", activity.Row.ActivityID, uid);
        return new CampBattleOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, BuildPersonalInfo(personal));
    }

    public async Task<bool> AddScoreAsync(ulong uid, long score)
    {
        if (score <= 0 || FindOpenActivity() is not { } activity)
        {
            return false;
        }

        return await repository.AddScoreAsync(uid, activity.Row.ActivityID, score);
    }

    public async Task<int> FindCampAsync(ulong uid) =>
        FindOpenActivity() is { } activity && await repository.FindAsync(uid, activity.Row.ActivityID) is { } row ? row.Camp : NoCamp;

    private ScheduledActivity? FindOpenActivity() =>
        schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeCampBattle, clock.GetUtcNow());

    private static pbCOMDT_CAMPBATTLE_INFO BuildPersonalInfo(RoleCampBattle? row) => row is null
        ? new pbCOMDT_CAMPBATTLE_INFO { Camp = NoCamp }
        : new pbCOMDT_CAMPBATTLE_INFO { Camp = row.Camp, IsAward = row.IsAward, Score = row.Score };

    private static pbResultInfo Result(PB_ENUM_RES_ERROR_CODE result) => new() { ResultId = (int)result };
}
