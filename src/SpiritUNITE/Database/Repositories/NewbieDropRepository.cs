using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

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
        var storedStates = await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .Select(role => role.NewbieState)
            .ToArrayAsync();
        if (storedStates.Length != OneRow)
        {
            await transaction.RollbackAsync();
            return null;
        }

        var merged = MergeNewbieBits(pbCOMDT_NEWBIE_STATE_INFO.Parser.ParseFrom(storedStates[0]), newbieStateAfter).ToByteArray();
        await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .Set(role => role.NewbieState, merged)
            .UpdateAsync();

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
