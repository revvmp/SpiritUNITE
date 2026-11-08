using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record EeveeChallengePeriod(int ActivityID, int Period, int EeveeID, int IsClick, long DrawTime);

public sealed record EeveeChallengeDrawCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    IReadOnlyDictionary<int, long> SpecialCoinsBefore,
    RoleWallet? Wallet)
{
    public static EeveeChallengeDrawCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, new Dictionary<int, long>(), null);
}

public sealed class EeveeChallengeRepository(DataOptions options)
{
    private const int Clicked = 1;
    private const int NotDrawn = 0;

    public async Task<EeveeChallengePeriod[]> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleEeveeChallenge>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .OrderBy(row => row.ActivityID)
            .ThenBy(row => row.Period)
            .ToArrayAsync();
        return [.. rows.Select(row => new EeveeChallengePeriod(row.ActivityID, row.Period, row.EeveeID, row.IsClick, row.DrawTime))];
    }

    public async Task ClickAsync(ulong gameUlongUID, int activityID, int period)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await EnsurePeriodAsync(database, gameUlongUID, activityID, period);
        await database.GetTable<RoleEeveeChallenge>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID && row.Period == period)
            .Set(row => row.IsClick, Clicked)
            .UpdateAsync();
        await transaction.CommitAsync();
    }

    public async Task<EeveeChallengeDrawCommit> CommitDrawAsync(
        ulong gameUlongUID,
        int activityID,
        int period,
        int eeveeID,
        long drawTime,
        RolePokemonInfo? timeLimitedPokemon,
        IReadOnlyDictionary<int, long> coinGrants)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await EnsurePeriodAsync(database, gameUlongUID, activityID, period);
        var eeveeAlreadyDrawn = await database.GetTable<RoleEeveeChallenge>()
            .AnyAsync(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID && row.EeveeID == eeveeID);
        var drawn = !eeveeAlreadyDrawn && await database.GetTable<RoleEeveeChallenge>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID && row.Period == period && row.EeveeID == NotDrawn)
            .Set(row => row.EeveeID, eeveeID)
            .Set(row => row.DrawTime, drawTime)
            .UpdateAsync() == 1;
        if (!drawn)
        {
            await transaction.RollbackAsync();
            return EeveeChallengeDrawCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrEeveeChallengeDrawFailed);
        }

        if (timeLimitedPokemon is not null)
        {
            await database.GetTable<RolePokemonTimeLimited>()
                .Where(row => row.GameUlongUID == gameUlongUID && row.PokemonId == timeLimitedPokemon.PokemonId)
                .DeleteAsync();
            await database.InsertAsync(RoleRepository.ToTimeLimitedEntity(gameUlongUID, timeLimitedPokemon));
        }

        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var (coinID, amount) in coinGrants)
        {
            if (CoinID.IsSpecialCoin(coinID))
            {
                specialCoinsBefore[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
            }

            await WalletRepository.CreditAsync(database, gameUlongUID, coinID, amount);
        }

        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new EeveeChallengeDrawCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, specialCoinsBefore, wallet);
    }

    private static async Task EnsurePeriodAsync(DataConnection database, ulong gameUlongUID, int activityID, int period)
    {
        try
        {
            await database.InsertAsync(new RoleEeveeChallenge
            {
                GameUlongUID = gameUlongUID,
                ActivityID = activityID,
                Period = period,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
        }
    }
}
