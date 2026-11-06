using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Tasks;

public sealed record BattleMonsterKill(int MonsterClassType, int KillCnt);

public sealed record BattleTaskEvent(
    bool Won,
    int OccupationType,
    int BreakCnt,
    int PickBerryCnt,
    IReadOnlyList<BattleMonsterKill> JoinKills);

public sealed record StoredTaskProcess(int TaskId, long PeriodStart, int TaskProcess, long CompleteTime);

public sealed record TaskProcessIncrement(int TaskId, long PeriodStart, int Delta, int TargetProcess);

public static class BattleTasks
{
    private const int OccupationParamIndex = 0;
    private const int OneBattle = 1;

    private static readonly IReadOnlySet<PB_ENUM_RES_CONDITION_ID> BattleConditions = new HashSet<PB_ENUM_RES_CONDITION_ID>
    {
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdWin,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdPokemonOccupation,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotoalBreakScoreCnt,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdScenePickRecoverOrMoveBerry,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdJoinKillMonsterClassType,
    };

    public static bool IsBattleCondition(pbFinishCondition condition) =>
        BattleConditions.Contains((PB_ENUM_RES_CONDITION_ID)condition.ID) && !IsLimitedToOtherGames(condition);

    public static int? MeasureBattle(pbFinishCondition condition, BattleTaskEvent battle) => !IsBattleCondition(condition)
        ? null
        : (PB_ENUM_RES_CONDITION_ID)condition.ID switch
        {
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdWin => battle.Won ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinish => OneBattle,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdPokemonOccupation =>
                condition.ParamB.Count > OccupationParamIndex && condition.ParamB[OccupationParamIndex] == battle.OccupationType
                    ? OneBattle
                    : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotoalBreakScoreCnt => battle.BreakCnt > 0 ? battle.BreakCnt : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdScenePickRecoverOrMoveBerry => battle.PickBerryCnt,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdJoinKillMonsterClassType => battle.JoinKills
                .Where(kill => condition.ParamA.Contains(kill.MonsterClassType))
                .Aggregate(0, (total, kill) => AddCounts(total, kill.KillCnt)),
            _ => null,
        };

    public static int AddCounts(int first, int second) =>
        (int)Math.Clamp((long)Math.Max(0, first) + Math.Max(0, second), 0, int.MaxValue);

    public static int FindStoredProcess(
        IReadOnlyDictionary<(int TaskId, long PeriodStart), StoredTaskProcess> stored,
        int taskId,
        long periodStart) =>
        stored.TryGetValue((taskId, periodStart), out var process) ? process.TaskProcess : 0;

    private static bool IsLimitedToOtherGames(pbFinishCondition condition) =>
        condition.ParamA.Count > 0
        && condition.ID is (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdWin or (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinish;
}
