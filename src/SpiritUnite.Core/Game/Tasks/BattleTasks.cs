using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Tasks;

public sealed record BattleMonsterKill(int MonsterID, int MonsterClassType, int KillCnt);

public sealed record BattleReportCondition(int ConditionType, int Value);

public sealed record BattleTaskEvent(
    bool Won,
    int MapID,
    int MapSubMode,
    int OccupationType,
    int BreakCnt,
    int PickBerryCnt,
    bool UsedHeldItem,
    IReadOnlyList<BattleMonsterKill> JoinKills)
{
    public bool IsManMachine { get; init; }
    public int PokemonID { get; init; }
    public bool IsEeveelution { get; init; }
    public IReadOnlyList<int> CampPokemonIDs { get; init; } = [];
    public bool PlayedWithFriend { get; init; }
    public IReadOnlyList<BattleReportCondition> ReportConditions { get; init; } = [];
}

public sealed record StoredTaskProcess(int TaskId, long PeriodStart, int TaskProcess, long CompleteTime)
{
    public const int FirstCondition = 0;

    public IReadOnlyDictionary<int, int> ConditionProcesses { get; init; } = new Dictionary<int, int>();

    public int ProcessOf(int conditionIndex) =>
        conditionIndex == FirstCondition ? TaskProcess : ConditionProcesses.GetValueOrDefault(conditionIndex);
}

public sealed record TaskProcessIncrement(
    int TaskId,
    long PeriodStart,
    int Delta,
    int TargetProcess,
    int ConditionIndex = StoredTaskProcess.FirstCondition,
    int? CountedValue = null);

public static class BattleTasks
{
    private const int OccupationParamIndex = 0;
    private const int SingleBattleThresholdIndex = 0;
    private const int OneBattle = 1;
    private const int NoCondition = 0;

    private static readonly IReadOnlySet<PB_ENUM_RES_CONDITION_ID> BattleConditions = new HashSet<PB_ENUM_RES_CONDITION_ID>
    {
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdWin,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdPokemonOccupation,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotoalBreakScoreCnt,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdScenePickRecoverOrMoveBerry,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdJoinKillMonsterClassType,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotoalUseHelditem,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRaceFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRaceWin,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRelaxFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRelaxWin,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRankedMatchFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRankedMatchWin,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdJoinKillMonster,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdPlayWithFriendAnyMode,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdUseDiffEeive,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdPkmListSameCampPktypeForbidManmachine,
    };

    private static readonly IReadOnlySet<PB_ENUM_RES_CONDITION_ID> PokemonListConditions = new HashSet<PB_ENUM_RES_CONDITION_ID>
    {
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdPkmListSameCampPktypeAny,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdPkmListSameCampPktypeForbidManmachine,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdPkmListSameCampPktypeForbidRoomAndManmachine,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdPkmListSameCampPktypeForbidRoom,
    };

    private static readonly IReadOnlySet<PB_ENUM_RES_CONDITION_ID> MapListedConditions = new HashSet<PB_ENUM_RES_CONDITION_ID>
    {
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdWin,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRaceFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRaceWin,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRelaxFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRelaxWin,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdPlayWithFriendAnyMode,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdUseDiffEeive,
    };

    private static readonly IReadOnlySet<int> RaceSubModes = new HashSet<int>
    {
        (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceNormal,
        (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank,
    };

    private static readonly IReadOnlySet<int> RelaxSubModes = new HashSet<int>
    {
        (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRelaxNormal,
    };

    public static bool IsBattleCondition(pbFinishCondition condition) =>
        BattleConditions.Contains((PB_ENUM_RES_CONDITION_ID)condition.ID) && !IsLimitedToOtherGames(condition);

    public static int? MeasureBattle(pbFinishCondition condition, BattleTaskEvent battle) => !IsBattleCondition(condition)
        ? null
        : (PB_ENUM_RES_CONDITION_ID)condition.ID switch
        {
            _ when !IsPlayedOnListedMap(condition, battle) => null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdWin => battle.Won ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinish => OneBattle,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdRaceFinish => IsListedOrInSubMode(condition, battle, RaceSubModes) ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdRaceWin => battle.Won && IsListedOrInSubMode(condition, battle, RaceSubModes) ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdRelaxFinish => IsListedOrInSubMode(condition, battle, RelaxSubModes) ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdRelaxWin => battle.Won && IsListedOrInSubMode(condition, battle, RelaxSubModes) ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotoalUseHelditem => battle.UsedHeldItem ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdPokemonOccupation =>
                condition.ParamB.Count > OccupationParamIndex && condition.ParamB[OccupationParamIndex] == battle.OccupationType
                    ? OneBattle
                    : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotoalBreakScoreCnt => battle.BreakCnt > 0 ? battle.BreakCnt : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdScenePickRecoverOrMoveBerry => battle.PickBerryCnt,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdJoinKillMonsterClassType => battle.JoinKills
                .Where(kill => condition.ParamA.Contains(kill.MonsterClassType))
                .Aggregate(0, (total, kill) => AddCounts(total, kill.KillCnt)),
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdRankedMatchFinish => IsRankedMatch(battle) ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdRankedMatchWin => battle.Won && IsRankedMatch(battle) ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdJoinKillMonster =>
                condition.ParamB.Count == 0 || condition.ParamB.Contains(battle.MapID)
                    ? battle.JoinKills
                        .Where(kill => condition.ParamA.Count == 0 || condition.ParamA.Contains(kill.MonsterID))
                        .Aggregate(0, (total, kill) => AddCounts(total, kill.KillCnt))
                    : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdPlayWithFriendAnyMode => battle.PlayedWithFriend ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdUseDiffEeive => battle.IsEeveelution ? OneBattle : null,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdPkmListSameCampPktypeForbidManmachine =>
                !battle.IsManMachine && IsCampOfDistinctListedPokemon(condition, battle) ? OneBattle : null,
            _ => null,
        };

    public static int? MeasureReportedCondition(pbFinishCondition condition, int reportedConditionType, BattleTaskEvent battle)
    {
        var reportedValue = battle.ReportConditions
            .Where(report => report.ConditionType == reportedConditionType)
            .Aggregate(0, (total, report) => AddCounts(total, report.Value));
        if (reportedValue <= 0)
        {
            return null;
        }

        return condition.ParamB.Count > SingleBattleThresholdIndex
            ? reportedValue >= condition.ParamB[SingleBattleThresholdIndex] ? OneBattle : null
            : reportedValue;
    }

    public static IEnumerable<TaskProcessIncrement> FindBattleIncrements(pbTask_Base task, long periodStart, BattleTaskEvent battle) =>
        task.FinishCondition
            .Select((condition, conditionIndex) => (Condition: condition, ConditionIndex: conditionIndex))
            .Where(pair => pair.Condition.ID != NoCondition)
            .Select(pair => MeasureBattle(pair.Condition, battle) is { } delta
                ? new TaskProcessIncrement(
                    task.TaskID,
                    periodStart,
                    delta,
                    pair.Condition.TargetProcess,
                    pair.ConditionIndex,
                    CountedValueOf(pair.Condition, battle))
                : null)
            .OfType<TaskProcessIncrement>();

    public static IReadOnlySet<int> FindEeveelutionPokemonIDs(IEnumerable<pbTask_Base> tasks) =>
        tasks.SelectMany(task => task.FinishCondition)
            .Where(condition => PokemonListConditions.Contains((PB_ENUM_RES_CONDITION_ID)condition.ID))
            .SelectMany(condition => condition.ParamB)
            .ToHashSet();

    private static int? CountedValueOf(pbFinishCondition condition, BattleTaskEvent battle) =>
        condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdUseDiffEeive ? battle.PokemonID : null;

    private static bool IsRankedMatch(BattleTaskEvent battle) =>
        battle.MapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank;

    private static bool IsCampOfDistinctListedPokemon(pbFinishCondition condition, BattleTaskEvent battle) =>
        battle.CampPokemonIDs.Count > 0
        && battle.CampPokemonIDs.All(condition.ParamB.Contains)
        && battle.CampPokemonIDs.Distinct().Count() == battle.CampPokemonIDs.Count;

    public static int AddCounts(int first, int second) =>
        (int)Math.Clamp((long)Math.Max(0, first) + Math.Max(0, second), 0, int.MaxValue);

    public static int FindStoredProcess(
        IReadOnlyDictionary<(int TaskId, long PeriodStart), StoredTaskProcess> stored,
        int taskId,
        long periodStart,
        int conditionIndex = StoredTaskProcess.FirstCondition) =>
        stored.TryGetValue((taskId, periodStart), out var process) ? process.ProcessOf(conditionIndex) : 0;

    private static bool IsListedOrInSubMode(pbFinishCondition condition, BattleTaskEvent battle, IReadOnlySet<int> subModes) =>
        condition.ParamA.Count > 0 || subModes.Contains(battle.MapSubMode);

    private static bool IsPlayedOnListedMap(pbFinishCondition condition, BattleTaskEvent battle) =>
        !MapListedConditions.Contains((PB_ENUM_RES_CONDITION_ID)condition.ID)
        || condition.ParamA.Count == 0
        || condition.ParamA.Contains(battle.MapID);

    private static bool IsLimitedToOtherGames(pbFinishCondition condition) =>
        condition.ParamB.Count > 0
        && condition.ID is (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdWin
            or (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinish
            or (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotoalUseHelditem;
}
