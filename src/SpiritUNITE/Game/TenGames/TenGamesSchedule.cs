using SpiritUNITE.Databin;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Game.TenGames;

public enum TenGamesState
{
    NotApply = 0,
    Apply = 1,
    Start = 2,
    Settle = 3,
    NotStart = 4,
    Interval = 5,
    Ended = 6,
}

public sealed record TenGamesWindow(long OpenTime, long CloseTime);

public sealed record TenGamesStatus(TenGamesState State, long EndTime);

public sealed class TenGamesGame
{
    private const long NoConfigTime = 0;
    private const int NoMemberCount = 0;

    public TenGamesGame(pbTenGames row)
    {
        Row = row;
        DisplayTime = UTCTimeUtil.CfgTime2ServerUnixTime(row.DisplayDate);
        HideTime = UTCTimeUtil.CfgTime2ServerUnixTime(row.HideDate);
        RegisterOpenTime = UTCTimeUtil.CfgTime2ServerUnixTime(row.OpenDate);
        RegisterCloseTime = UTCTimeUtil.CfgTime2ServerUnixTime(row.CloseDate);
        SettleEndTime = UTCTimeUtil.CfgTime2ServerUnixTime(row.SettleEndTime);
        Windows =
        [
            .. row.OpenGameTime.Zip(row.CloseGameTime)
                .Where(pair => pair.First != NoConfigTime && pair.Second != NoConfigTime)
                .Select(pair => new TenGamesWindow(
                    UTCTimeUtil.CfgTime2ServerUnixTime(pair.First),
                    UTCTimeUtil.CfgTime2ServerUnixTime(pair.Second)))
                .OrderBy(window => window.OpenTime),
        ];
        ApplyMemberCounts = [.. row.CanApplyMember.Where(count => count != NoMemberCount)];
    }

    public pbTenGames Row { get; }

    public int GamesId => Row.GamesId;

    public long DisplayTime { get; }

    public long HideTime { get; }

    public long RegisterOpenTime { get; }

    public long RegisterCloseTime { get; }

    public long SettleEndTime { get; }

    public IReadOnlyList<TenGamesWindow> Windows { get; }

    public IReadOnlyList<int> ApplyMemberCounts { get; }

    public bool IsDisplayed(long now) => DisplayTime <= now && now <= HideTime;

    public bool IsInRegisterTime(long now) => RegisterOpenTime <= now && now <= RegisterCloseTime;

    public bool AllowsMemberCount(int memberCount) => ApplyMemberCounts.Contains(memberCount);

    public TenGamesWindow? FindOpenWindow(long now) =>
        Windows.FirstOrDefault(window => window.OpenTime <= now && now <= window.CloseTime);

    public TenGamesStatus StatusAt(long now)
    {
        if (FindOpenWindow(now) is { } open)
        {
            return new TenGamesStatus(TenGamesState.Start, open.CloseTime);
        }

        if (Windows.FirstOrDefault(window => window.OpenTime > now) is { } next)
        {
            if (Windows.Any(window => window.CloseTime < now))
            {
                return new TenGamesStatus(TenGamesState.Interval, next.OpenTime);
            }

            return new TenGamesStatus(IsInRegisterTime(now) ? TenGamesState.Apply : TenGamesState.NotStart, next.OpenTime);
        }

        return now <= SettleEndTime
            ? new TenGamesStatus(TenGamesState.Settle, SettleEndTime)
            : new TenGamesStatus(TenGamesState.Ended, SettleEndTime);
    }
}

public sealed class TenGamesSchedule
{
    private readonly Dictionary<int, TenGamesGame> games;

    public TenGamesSchedule(GameData gameData)
    {
        games = gameData.TenGames.Rows.Select(row => new TenGamesGame(row)).ToDictionary(game => game.GamesId);
        MaxTeamMemberNum = games.Values.SelectMany(game => game.ApplyMemberCounts).DefaultIfEmpty().Max();
    }

    public int MaxTeamMemberNum { get; }

    public TenGamesGame? Find(int gamesId) => games.GetValueOrDefault(gamesId);
}
