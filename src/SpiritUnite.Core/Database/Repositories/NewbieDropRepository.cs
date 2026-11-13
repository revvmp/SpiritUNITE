using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record NewbieDropCommit(IReadOnlyDictionary<int, long> BeforeCoinNums, RoleWallet Wallet);

public sealed class NewbieDropRepository(DataOptions options)
{
    private const int OneRow = 1;

    public async Task<NewbieDropCommit?> ArchiveWithCoinsAsync(
        ulong gameUlongUID,
        pbCOMDT_NEWBIE_STATE_INFO newbieStateAfter,
        IReadOnlyDictionary<int, long> coins)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await ArchiveNewbieStateAsync(database, gameUlongUID, newbieStateAfter))
        {
            await transaction.RollbackAsync();
            return null;
        }

        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var (coinID, amount) in coins)
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
            await WalletRepository.CreditAsync(database, gameUlongUID, coinID, amount);
        }

        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new NewbieDropCommit(beforeCoinNums, wallet);
    }

    public async Task<int?> ArchiveWithRoleTotalExpAsync(ulong gameUlongUID, pbCOMDT_NEWBIE_STATE_INFO newbieStateAfter, int chgExp)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await ArchiveNewbieStateAsync(database, gameUlongUID, newbieStateAfter)
            || await RoleRepository.AddRoleTotalExpAsync(database, gameUlongUID, chgExp) is not { } roleTotalExp)
        {
            await transaction.RollbackAsync();
            return null;
        }

        await transaction.CommitAsync();
        return roleTotalExp;
    }

    private static async Task<bool> ArchiveNewbieStateAsync(DataConnection database, ulong gameUlongUID, pbCOMDT_NEWBIE_STATE_INFO newbieStateAfter)
    {
        var storedStates = await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .Select(role => role.NewbieState)
            .ToArrayAsync();
        if (storedStates.Length != OneRow)
        {
            return false;
        }

        var merged = MergeNewbieBits(pbCOMDT_NEWBIE_STATE_INFO.Parser.ParseFrom(storedStates[0]), newbieStateAfter).ToByteArray();
        await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .Set(role => role.NewbieState, merged)
            .UpdateAsync();
        return true;
    }

    private static pbCOMDT_NEWBIE_STATE_INFO MergeNewbieBits(pbCOMDT_NEWBIE_STATE_INFO stored, pbCOMDT_NEWBIE_STATE_INFO after)
    {
        var words = Math.Max(stored.BitsDetail.Count, after.BitsDetail.Count);
        return new pbCOMDT_NEWBIE_STATE_INFO
        {
            BitsDetail =
            {
                Enumerable.Range(0, words).Select(word => stored.BitsDetail.ElementAtOrDefault(word) | after.BitsDetail.ElementAtOrDefault(word)),
            },
        };
    }
}
