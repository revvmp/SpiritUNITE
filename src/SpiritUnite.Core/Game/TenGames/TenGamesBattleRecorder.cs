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

    private async Task<IReadOnlyDictionary<ulong, TenGamesBattleScore>> SettleDeskAsync(TenGamesBattle battle)
    {
        var camps = new Dictionary<int, TenGamesTeam>();
        foreach (var camp in battle.Humans.GroupBy(human => human.CampClient))
        {
            if (await tenGames.FindPlayingTeamAsync(camp.First().Uid, battle.MapId) is { } team
                && await AllInTeamAsync(camp, team))
            {
                camps[camp.Key] = team;
            }
        }

        var scores = new Dictionary<ulong, TenGamesBattleScore>();
        if (battle.IsDraw)
        {
            return scores;
        }

        var initScore = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTenGamesInitMmrscore);
        var lower = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTenGamesMmrLowerboundary);
        var upper = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTenGamesMmrUpperboundary);
        var before = camps.ToDictionary(camp => camp.Key, camp => camp.Value.CurScore);
        foreach (var (campClient, team) in camps)
        {
            var won = campClient == battle.WinCampClient;
            var opponentScore = before.Where(other => other.Key != campClient).Select(other => other.Value).DefaultIfEmpty(initScore).First();
            var teamSize = battle.Humans.Count(human => human.CampClient == campClient);
            var k = gameData.GetGlobalIntData(EloKByTeamSize[Math.Clamp(teamSize, 1, EloKByTeamSize.Length) - 1]);
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
                "Ten games team {TeamID} {Result} on desk {DeskID}: score {Inc:+0;-0;0} to {Score} ({WinCount}-{LoseCount})",
                team.TeamID, won ? "won" : "lost", battle.DeskID, inc, newScore, team.WinCount, team.LoseCount);
            foreach (var human in battle.Humans.Where(human => human.CampClient == campClient))
            {
                scores[human.Uid] = new TenGamesBattleScore(inc, newScore);
                if (won)
                {
                    await bpActivity.RecordWinAsync(human.Uid);
                }
            }
        }

        return scores;
    }

    private async Task<bool> AllInTeamAsync(IEnumerable<TenGamesBattlePlayer> camp, TenGamesTeam team)
    {
        var memberUids = await tenGames.GetMemberUidsAsync(team.TeamID);
        return camp.All(human => memberUids.Contains(human.Uid));
    }
}
