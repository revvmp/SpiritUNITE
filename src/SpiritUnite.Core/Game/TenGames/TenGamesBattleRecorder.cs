using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Databin;
using SpiritUnite.Game.Activity;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.TenGames;

public sealed record TenGamesBattlePlayer(ulong Uid, int CampClient);

public sealed record TenGamesBattle(
    int DeskID,
    long DeskCrtSeq,
    int MapId,
    IReadOnlyList<TenGamesBattlePlayer> Humans,
    int WinCampClient,
    bool IsDraw);

public sealed record TenGamesBattleScore(int IncTenGamesScore, int TotalTenGamesScore);

public sealed record TenGamesTeamBattlePlan(
    ulong TeamID, int JoinTenGamesID, int BeforeScore, int BeforeWins, int BeforeLosses,
    int Score, int Wins, int Losses, bool Won, IReadOnlyList<ulong> Uids);

public sealed record TenGamesSettlementPlan(Guid SettlementID, long CreatedAt, int BPActivityID, IReadOnlyList<TenGamesTeamBattlePlan> Teams);

internal sealed record TenGamesDeskScores(
    Lazy<Task<IReadOnlyDictionary<ulong, TenGamesBattleScore>>> Scores,
    ConcurrentDictionary<ulong, byte> PendingUids);

public sealed class TenGamesBattleRecorder(
    TenGamesManager tenGames,
    TenGamesBPActivityManager bpActivity,
    GameData gameData,
    ILogger<TenGamesBattleRecorder> logger)
{
    private const int OnlineCompetitionMapSubMode = 7;
    private const double EloScale = 400;
    private const double EloBase = 10;
    private const double WinScore = 1;
    private const double LoseScore = 0;

    private static readonly PB_ENUM_RES_RESGLOBAL[] EloKByTeamSize =
    [
        PB_ENUM_RES_RESGLOBAL.PbResglobalPvpMatch1V1Elementk,
        PB_ENUM_RES_RESGLOBAL.PbResglobalPvpMatch2V2Elementk,
        PB_ENUM_RES_RESGLOBAL.PbResglobalPvpMatch3V3Elementk,
        PB_ENUM_RES_RESGLOBAL.PbResglobalPvpMatch4V4Elementk,
        PB_ENUM_RES_RESGLOBAL.PbResglobalPvpMatch5V5Elementk,
    ];

    private readonly ConcurrentDictionary<(int DeskID, long DeskCrtSeq), TenGamesDeskScores> desks = new();

    public static bool IsTenGamesMap(GameData gameData, int mapId) =>
        gameData.SceneMap.Find(mapId)?.MapSubMode == OnlineCompetitionMapSubMode;

    public async Task<TenGamesBattleScore?> RecordBattleAsync(TenGamesBattle battle, ulong uid)
    {
        if (!IsTenGamesMap(gameData, battle.MapId))
        {
            return null;
        }

        var key = (battle.DeskID, battle.DeskCrtSeq);
        var desk = desks.GetOrAdd(key, _ => new TenGamesDeskScores(
            new Lazy<Task<IReadOnlyDictionary<ulong, TenGamesBattleScore>>>(() => SettleDeskAsync(battle)),
            new ConcurrentDictionary<ulong, byte>(battle.Humans.Select(human => KeyValuePair.Create(human.Uid, (byte)0)))));
        var scores = await desk.Scores.Value;
        desk.PendingUids.TryRemove(uid, out _);
        if (desk.PendingUids.IsEmpty)
        {
            desks.TryRemove(key, out _);
        }

        return scores.GetValueOrDefault(uid);
    }

    public async Task<TenGamesSettlementPlan?> PrepareSettlementAsync(TenGamesBattle battle, Guid settlementID, long now)
    {
        if (!IsTenGamesMap(gameData, battle.MapId))
        {
            return null;
        }

        return await tenGames.PrepareBattleAsync(settlementID, () => BuildSettlementPlanAsync(battle, settlementID, now));
    }

    public async Task<TenGamesBattleScore?> RecordBattleAsync(TenGamesSettlementPlan plan, ulong uid)
    {
        var applied = await tenGames.ApplyBattleAsync(plan.SettlementID, uid);
        var team = applied.Teams.FirstOrDefault(entry => entry.Uids.Contains(uid));
        return team is null ? null : new TenGamesBattleScore(team.Score - team.BeforeScore, team.Score);
    }

    private async Task<TenGamesSettlementPlan> BuildSettlementPlanAsync(TenGamesBattle battle, Guid settlementID, long now)
    {
        if (battle.IsDraw)
        {
            return new TenGamesSettlementPlan(settlementID, now, 0, []);
        }

        var entries = await FindPlayingEntriesAsync(battle, DateTimeOffset.FromUnixTimeSeconds(now));
        var initScore = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTenGamesInitMmrscore);
        var lower = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTenGamesMmrLowerboundary);
        var upper = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTenGamesMmrUpperboundary);
        var teams = new List<TenGamesTeamBattlePlan>();
        foreach (var entry in entries)
        {
            var before = entry.Team;
            var won = entry.CampClient == battle.WinCampClient;
            var opponentScore = entries.Where(other => other.CampClient != entry.CampClient)
                .Select(other => (double)other.Team.CurScore).DefaultIfEmpty(initScore).Average();
            var k = gameData.GetGlobalIntData(EloKByTeamSize[Math.Clamp(entry.Uids.Count, 1, EloKByTeamSize.Length) - 1]);
            var expected = 1 / (1 + Math.Pow(EloBase, (opponentScore - before.CurScore) / EloScale));
            var score = Math.Clamp((int)Math.Round(before.CurScore + k * ((won ? WinScore : LoseScore) - expected), MidpointRounding.AwayFromZero), lower, upper);
            teams.Add(new TenGamesTeamBattlePlan(before.TeamID, before.JoinTenGamesID, before.CurScore, before.WinCount, before.LoseCount,
                score, before.WinCount + (won ? 1 : 0), before.LoseCount + (won ? 0 : 1), won, entry.Uids));
        }

        return new TenGamesSettlementPlan(settlementID, now, bpActivity.FindSettlementActivityID(now), teams);
    }

    private async Task<IReadOnlyDictionary<ulong, TenGamesBattleScore>> SettleDeskAsync(TenGamesBattle battle)
    {
        var entries = await FindPlayingEntriesAsync(battle);
        var scores = new Dictionary<ulong, TenGamesBattleScore>();
        if (battle.IsDraw)
        {
            return scores;
        }

        var initScore = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTenGamesInitMmrscore);
        var lower = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTenGamesMmrLowerboundary);
        var upper = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTenGamesMmrUpperboundary);
        var scoresBefore = entries.Select(entry => (entry.CampClient, Score: entry.Team.CurScore)).ToList();
        foreach (var entry in entries)
        {
            var team = entry.Team;
            var won = entry.CampClient == battle.WinCampClient;
            var opponentScore = scoresBefore
                .Where(other => other.CampClient != entry.CampClient)
                .Select(other => (double)other.Score)
                .DefaultIfEmpty(initScore)
                .Average();
            var k = gameData.GetGlobalIntData(EloKByTeamSize[Math.Clamp(entry.Uids.Count, 1, EloKByTeamSize.Length) - 1]);
            var expected = 1 / (1 + Math.Pow(EloBase, (opponentScore - team.CurScore) / EloScale));
            var newScore = Math.Clamp(
                (int)Math.Round(team.CurScore + k * ((won ? WinScore : LoseScore) - expected), MidpointRounding.AwayFromZero),
                lower,
                upper);
            var inc = newScore - team.CurScore;
            team.CurScore = newScore;
            if (won)
            {
                team.WinCount++;
            }
            else
            {
                team.LoseCount++;
            }

            await tenGames.SaveBattleAsync(team);
            logger.LogInformation(
                "Ten games team {TeamID} {Result} on desk {DeskID} camp {CampClient}: score {Inc:+0;-0;0} to {Score} ({WinCount}-{LoseCount})",
                team.TeamID, won ? "won" : "lost", battle.DeskID, entry.CampClient, inc, newScore, team.WinCount, team.LoseCount);
            foreach (var uid in entry.Uids)
            {
                scores[uid] = new TenGamesBattleScore(inc, newScore);
                if (won)
                {
                    await bpActivity.RecordWinAsync(uid);
                }
            }
        }

        return scores;
    }

    private async Task<List<TenGamesCampEntry>> FindPlayingEntriesAsync(TenGamesBattle battle, DateTimeOffset? when = null)
    {
        var playing = new List<(TenGamesBattlePlayer Human, TenGamesTeam Team)>();
        foreach (var human in battle.Humans)
        {
            if (await tenGames.FindPlayingTeamAsync(human.Uid, battle.MapId, when) is { } team)
            {
                playing.Add((human, team));
            }
        }

        var entries = new List<TenGamesCampEntry>();
        foreach (var teamPlayers in playing.GroupBy(player => player.Team.TeamID))
        {
            var campClients = teamPlayers.Select(player => player.Human.CampClient).Distinct().ToList();
            if (campClients.Count != 1)
            {
                logger.LogWarning(
                    "Ten games team {TeamID} played in {CampCount} camps on desk {DeskID}; its result is not scored",
                    teamPlayers.Key, campClients.Count, battle.DeskID);
                continue;
            }

            entries.Add(new TenGamesCampEntry(
                campClients[0],
                teamPlayers.First().Team,
                [.. teamPlayers.Select(player => player.Human.Uid)]));
        }

        return entries;
    }

    private sealed record TenGamesCampEntry(int CampClient, TenGamesTeam Team, IReadOnlyList<ulong> Uids);
}
