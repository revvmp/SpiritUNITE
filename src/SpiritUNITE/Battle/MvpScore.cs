using SpiritUNITE.Databin;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Battle;

public sealed record MvpPlayer(int ObjType, int ObjID, int CampClient, int OccupationType, MvpStats Stats);

public sealed record MvpStats(
    int Level,
    int Damage,
    int DamageTank,
    int Score,
    int BreakCnt,
    int Cure,
    int KillCnt,
    int AssistCnt,
    int DeadCnt)
{
    public static MvpStats From(pbCSDT_MULTIGAMEOVER_INFO? acntInfo)
    {
        var pokemonData = acntInfo?.CommonData?.PokemonData;
        var statistic = pokemonData?.PokemonBattleStatistic;
        var baseStatistic = statistic?.BaseStatistic;
        return new MvpStats(
            Math.Max(0, pokemonData?.PokemonDetailInfo?.Level ?? 0),
            Math.Max(0, baseStatistic?.Damage ?? 0),
            Math.Max(0, baseStatistic?.DamageTank ?? 0),
            Math.Max(0, statistic?.GetScoreCnt ?? 0),
            Math.Max(0, statistic?.BreakCnt ?? 0),
            Math.Max(0, baseStatistic?.Cure ?? 0),
            Math.Max(0, baseStatistic?.KillCnt ?? 0),
            Math.Max(0, baseStatistic?.AssistCnt ?? 0),
            Math.Max(0, baseStatistic?.DeadCnt ?? 0));
    }
}

public sealed record MvpResult(IReadOnlyDictionary<int, int> MvpScoreTTHByObjID, int WinMvpObjID, int LoseMvpObjID)
{
    public const long WinMvpBattleFlag = 1L << (WinMvpMedalType - 1);
    public const long LoseMvpBattleFlag = 1L << (LoseMvpMedalType - 1);

    private const int WinMvpMedalType = (int)PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeWinMvp;
    private const int LoseMvpMedalType = (int)PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeLoseMvp;

    public long BattleFlagOf(int objID) =>
        (objID == WinMvpObjID ? WinMvpBattleFlag : 0) | (objID == LoseMvpObjID ? LoseMvpBattleFlag : 0);
}

public sealed class MvpScore
{
    private const int CatchGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeCatch;
    private const int FirstOccupationType = (int)PB_ENUM_RES_POKEMON_OCCUPATION_TYPE.PbResPokemonOccupationTypeSupport;
    private const int RefDivisor = 10000;
    private const int NoMvp = 0;

    private const int MaxLevelParam = 1;
    private const int MaxDamageParam = 2;
    private const int MaxDamageTankParam = 3;
    private const int MaxScoreParam = 4;
    private const int MaxBreakParam = 5;
    private const int MaxCureParam = 6;
    private const int MaxKillParam = 8;
    private const int MaxAssistParam = 9;
    private const int MaxDeadParam = 10;
    private const int MaxKdaParam = 11;
    private const int WinBaseParam = 12;
    private const int LoseBaseParam = 13;
    private const int KdaAssistRatioParam = 14;
    private const int KdaDeadRatioParam = 15;
    private const int LevelWeightParam = 16;
    private const int DamageWeightParam = 17;
    private const int DamageTankWeightParam = 18;
    private const int ScoreWeightParam = 19;
    private const int BreakWeightParam = 20;
    private const int CureWeightParam = 21;
    private const int KillWeightParam = 23;
    private const int AssistWeightParam = 24;
    private const int DeadWeightParam = 25;
    private const int KdaWeightParam = 26;
    private const int LevelRefParam = 27;
    private const int LevelFixParam = 28;
    private const int DamageRefParam = 29;
    private const int DamageFixParam = 30;
    private const int DamageTankRefParam = 31;
    private const int DamageTankFixParam = 32;
    private const int ScoreRefParam = 33;
    private const int ScoreFixParam = 34;
    private const int BreakRefParam = 35;
    private const int BreakFixParam = 36;
    private const int CureRefParam = 37;
    private const int CureFixParam = 38;
    private const int KillRefParam = 41;
    private const int KillFixParam = 42;
    private const int AssistRefParam = 43;
    private const int AssistFixParam = 44;
    private const int DeadRefParam = 45;
    private const int DeadFixParam = 46;

    private readonly IReadOnlyDictionary<int, int[]> paramVecByType;

    public MvpScore(GameData gameData)
    {
        paramVecByType = gameData.MvpConf.Rows
            .Where(row => row.GameType == CatchGameType)
            .GroupBy(row => row.ParamType)
            .ToDictionary(group => group.Key, group => group.First().ParamVec);
    }

    public MvpResult Compute(IReadOnlyList<MvpPlayer> players, int winCampClient)
    {
        var totals = new MvpStats(
            players.Sum(player => player.Stats.Level),
            Sum(players, player => player.Stats.Damage),
            Sum(players, player => player.Stats.DamageTank),
            Sum(players, player => player.Stats.Score),
            Sum(players, player => player.Stats.BreakCnt),
            Sum(players, player => player.Stats.Cure),
            Sum(players, player => player.Stats.KillCnt),
            Sum(players, player => player.Stats.AssistCnt),
            Sum(players, player => player.Stats.DeadCnt));
        var scores = players.ToDictionary(player => player.ObjID, player => ScoreOf(player, totals, player.CampClient == winCampClient));
        return new MvpResult(
            scores.ToDictionary(score => score.Key, score => (int)Math.Clamp(Math.Floor(score.Value), int.MinValue, int.MaxValue)),
            TopOf(players.Where(player => player.CampClient == winCampClient), scores),
            TopOf(players.Where(player => player.CampClient != winCampClient), scores));
    }

    private static int Sum(IEnumerable<MvpPlayer> players, Func<MvpPlayer, int> stat) => (int)Math.Min(int.MaxValue, players.Sum(player => (long)stat(player)));

    private static int TopOf(IEnumerable<MvpPlayer> camp, IReadOnlyDictionary<int, double> scores) =>
        camp.Where(player => scores[player.ObjID] > 0).MaxBy(player => scores[player.ObjID])?.ObjID ?? NoMvp;

    private double ScoreOf(MvpPlayer player, MvpStats totals, bool won)
    {
        var occupation = Math.Max(0, player.OccupationType - FirstOccupationType);
        var stats = player.Stats;
        var score = Param(won ? WinBaseParam : LoseBaseParam, 0)
            + Term(occupation, stats.Level, totals.Level, LevelRefParam, LevelFixParam, LevelWeightParam, MaxLevelParam)
            + Term(occupation, stats.Damage, totals.Damage, DamageRefParam, DamageFixParam, DamageWeightParam, MaxDamageParam)
            + Term(occupation, stats.DamageTank, totals.DamageTank, DamageTankRefParam, DamageTankFixParam, DamageTankWeightParam, MaxDamageTankParam)
            + Term(occupation, stats.Score, totals.Score, ScoreRefParam, ScoreFixParam, ScoreWeightParam, MaxScoreParam)
            + Term(occupation, stats.BreakCnt, totals.BreakCnt, BreakRefParam, BreakFixParam, BreakWeightParam, MaxBreakParam)
            + Term(occupation, stats.Cure, totals.Cure, CureRefParam, CureFixParam, CureWeightParam, MaxCureParam)
            + Term(occupation, stats.KillCnt, totals.KillCnt, KillRefParam, KillFixParam, KillWeightParam, MaxKillParam)
            + Term(occupation, stats.AssistCnt, totals.AssistCnt, AssistRefParam, AssistFixParam, AssistWeightParam, MaxAssistParam)
            - Term(occupation, stats.DeadCnt, totals.DeadCnt, DeadRefParam, DeadFixParam, DeadWeightParam, MaxDeadParam);
        var kda = (stats.KillCnt + stats.AssistCnt * Param(KdaAssistRatioParam, 0)) / (stats.DeadCnt * Param(KdaDeadRatioParam, 0) + 1);
        return score + Math.Min(kda * Param(KdaWeightParam, occupation), Param(MaxKdaParam, 0));
    }

    private double Term(int occupation, int value, int total, int refParam, int fixParam, int weightParam, int maxParam)
    {
        if (total <= 0)
        {
            return 0;
        }

        var saturation = value + Param(refParam, occupation) / RefDivisor;
        var fixedPart = saturation > 0 ? value / saturation * Param(fixParam, occupation) : 0;
        return Math.Min(fixedPart + (double)value / total * Param(weightParam, occupation), Param(maxParam, 0));
    }

    private double Param(int paramType, int index) =>
        paramVecByType.TryGetValue(paramType, out var paramVec) && index >= 0 && index < paramVec.Length ? paramVec[index] : 0;
}
