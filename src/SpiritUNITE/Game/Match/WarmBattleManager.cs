using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Match;

public sealed class WarmBattleManager(GameData gameData, WarmBattleRepository repository, RoleInfoStore roles, RankedManager ranked, IOptionsMonitor<MatchOptions> matchOptions)
{
    private const int RaceNormal = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceNormal;
    private const int RaceRank = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank;
    private const int RelaxNormal = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRelaxNormal;
    private const int SinglePlayer = 1;

    private static readonly WarmBattleCount NoGames = new(0, 0, 0, 0);

    private readonly ConcurrentDictionary<ulong, IReadOnlyDictionary<int, WarmBattleCount>> countsByUid = [];

    public static bool IsWarmBattleSubMode(int mapSubMode) => mapSubMode is RaceNormal or RaceRank or RelaxNormal;

    public async Task LoadAsync(ulong uid) => countsByUid[uid] = await repository.FindAsync(uid);

    public int WarmBattleTypeOf(int mapSubMode, IReadOnlyCollection<ulong> memberUids) =>
        AdmitsRookie(mapSubMode, memberUids) ? MatchGroup.Rookie
        : AdmitsContLose(mapSubMode, memberUids) ? MatchGroup.ContLose
        : MatchGroup.NoWarmBattle;

    private bool AdmitsContLose(int mapSubMode, IReadOnlyCollection<ulong> memberUids) =>
        mapSubMode == RaceRank
        && memberUids.Any(uid => !ranked.Ladder.IsMaster(ranked.PositionOf(uid).RankedDivision)
            && CountOf(uid, mapSubMode).ContLoseCnt >= matchOptions.CurrentValue.ContLoseWarmBattleLosses);

    private bool AdmitsRookie(int mapSubMode, IReadOnlyCollection<ulong> memberUids)
    {
        if (!IsWarmBattleSubMode(mapSubMode) || memberUids.Count == 0)
        {
            return false;
        }

        ulong[] newbies = [.. memberUids.Where(uid => IsWithinWarmBattleGate(mapSubMode, uid))];
        if (newbies.Length == 0)
        {
            return false;
        }

        if (newbies.Any(uid => IsWithinFirstNGames(uid, mapSubMode)))
        {
            return true;
        }

        return memberUids.Count == SinglePlayer
            ? newbies.Any(uid => (mapSubMode == RaceRank || LevelOf(uid) <= gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalSinglewarmLevel))
                && CountsOf(uid).Sum(count => count.SingleWarmCnt) < gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalNewBieSingleWarmCntMax))
            : newbies.Any(uid =>
                CountsOf(uid).Sum(count => count.MultWarmCnt) < gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalNewBieMultWarmCntMax));
    }

    public bool IsWithinFirstNGames(ulong uid, int mapSubMode) =>
        CountOf(uid, mapSubMode).GameCnt < gameData.GetGlobalIntData(WarmbattleFirstngameOf(mapSubMode));

    public TimeSpan PickSuccDelay(int partySize) => partySize == SinglePlayer
        ? RandomSeconds(
            PB_ENUM_RES_RESGLOBAL.PbResglobalSinglewarmbattleSuccDelayLowerlimit,
            PB_ENUM_RES_RESGLOBAL.PbResglobalSinglewarmbattleSuccDelayUpperlimit)
        : RandomSeconds(
            PB_ENUM_RES_RESGLOBAL.PbResglobalWarmbattleSuccDelayLowerlimit,
            PB_ENUM_RES_RESGLOBAL.PbResglobalWarmbattleSuccDelayUpperlimit);

    public int WaitTime => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalWarmbattleSuccDelayUpperlimit);

    private bool IsWithinWarmBattleGate(int mapSubMode, ulong uid) => mapSubMode == RaceRank
        ? ranked.Ladder.BigOf(ranked.PositionOf(uid).RankedDivision)
            <= gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalUseWarmbattleFromLowestPlayerRank)
        : LevelOf(uid) <= gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalUseWarmbattleFromLowestPlayerLevel);

    private static PB_ENUM_RES_RESGLOBAL WarmbattleFirstngameOf(int mapSubMode) => mapSubMode switch
    {
        RaceNormal => PB_ENUM_RES_RESGLOBAL.PbResglobalRaceWarmbattleFirstngame,
        RaceRank => PB_ENUM_RES_RESGLOBAL.PbResglobalRankWarmbattleFirstngame,
        _ => PB_ENUM_RES_RESGLOBAL.PbResglobalRelaxWarmbattleFirstngame,
    };

    private int LevelOf(ulong uid) => roles.Find(uid) is { } role ? RoleProfile.GetLevel(gameData, role) : int.MaxValue;

    private IEnumerable<WarmBattleCount> CountsOf(ulong uid) =>
        countsByUid.TryGetValue(uid, out var counts) ? counts.Values : [];

    private WarmBattleCount CountOf(ulong uid, int mapSubMode) =>
        countsByUid.TryGetValue(uid, out var counts) ? counts.GetValueOrDefault(mapSubMode, NoGames) : NoGames;

    private TimeSpan RandomSeconds(PB_ENUM_RES_RESGLOBAL lowerLimit, PB_ENUM_RES_RESGLOBAL upperLimit) =>
        TimeSpan.FromSeconds(Random.Shared.Next(gameData.GetGlobalIntData(lowerLimit), gameData.GetGlobalIntData(upperLimit) + 1));
}
