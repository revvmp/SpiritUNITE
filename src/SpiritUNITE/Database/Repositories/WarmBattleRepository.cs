using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed record WarmBattleCount(int GameCnt, int SingleWarmCnt, int MultWarmCnt);

public sealed record WarmBattleGame(int MapSubMode, bool IsWarmBattle, bool IsSinglePlayer);

public sealed class WarmBattleRepository(DataOptions options)
{
    private const int OneGame = 1;
    private const int NoGame = 0;

    public async Task<IReadOnlyDictionary<int, WarmBattleCount>> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleWarmBattle>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .ToArrayAsync();
        return rows.ToDictionary(row => row.MapSubMode, row => new WarmBattleCount(row.GameCnt, row.SingleWarmCnt, row.MultWarmCnt));
    }

    public static Task AddGameAsync(DataConnection database, ulong gameUlongUID, WarmBattleGame game)
    {
        var singleWarm = game.IsWarmBattle && game.IsSinglePlayer ? OneGame : NoGame;
        var multWarm = game.IsWarmBattle && !game.IsSinglePlayer ? OneGame : NoGame;
        return database.GetTable<RoleWarmBattle>().InsertOrUpdateAsync(
            () => new RoleWarmBattle
            {
                GameUlongUID = gameUlongUID,
                MapSubMode = game.MapSubMode,
                GameCnt = OneGame,
                SingleWarmCnt = singleWarm,
                MultWarmCnt = multWarm,
            },
            saved => new RoleWarmBattle
            {
                GameCnt = saved.GameCnt < int.MaxValue ? saved.GameCnt + OneGame : saved.GameCnt,
                SingleWarmCnt = saved.GameCnt < int.MaxValue ? saved.SingleWarmCnt + singleWarm : saved.SingleWarmCnt,
                MultWarmCnt = saved.GameCnt < int.MaxValue ? saved.MultWarmCnt + multWarm : saved.MultWarmCnt,
            });
    }
}
