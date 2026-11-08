using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record FirstChargeState(
    long FirstChargeTime,
    long LastFaceShotTime,
    int FaceShotCnt,
    int IsBanFaceShot,
    IReadOnlyDictionary<int, long> AwardTimes);

public sealed class FirstChargeRepository(DataOptions options)
{
    private const int BanFaceShot = 1;

    public async Task<FirstChargeState> LoadAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await ReadAsync(database, gameUlongUID);
    }

    public async Task BanFaceShotAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleFirstCharge>().InsertOrUpdateAsync(
            () => new RoleFirstCharge { GameUlongUID = gameUlongUID, IsBanFaceShot = BanFaceShot },
            _ => new RoleFirstCharge { IsBanFaceShot = BanFaceShot });
    }

    public async Task RecordFaceShotShowedAsync(ulong gameUlongUID, long now)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleFirstCharge>().InsertOrUpdateAsync(
            () => new RoleFirstCharge { GameUlongUID = gameUlongUID, LastFaceShotTime = now, FaceShotCnt = 1 },
            row => new RoleFirstCharge { LastFaceShotTime = now, FaceShotCnt = row.FaceShotCnt + 1 });
    }

    public async Task<BagCommit> CommitAwardAsync(ulong gameUlongUID, int loginSeq, BagChange grants, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(new RoleFirstChargeAward { GameUlongUID = gameUlongUID, LoginSeq = loginSeq, AwardTime = now });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFirstPayHadRewarded);
        }

        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var coinID in grants.CoinGrants.Keys)
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var result = await BagRepository.ApplyAsync(database, gameUlongUID, grants, now);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(result);
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, grants.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new BagCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, changedItems, wallet) { BeforeCoinNums = beforeCoinNums };
    }

    private static async Task<FirstChargeState> ReadAsync(DataConnection database, ulong gameUlongUID)
    {
        var row = await database.GetTable<RoleFirstCharge>()
            .Where(firstCharge => firstCharge.GameUlongUID == gameUlongUID)
            .FirstOrDefaultAsync();
        var awardTimes = await database.GetTable<RoleFirstChargeAward>()
            .Where(award => award.GameUlongUID == gameUlongUID)
            .ToDictionaryAsync(award => award.LoginSeq, award => award.AwardTime);
        return new FirstChargeState(
            row?.FirstChargeTime ?? 0,
            row?.LastFaceShotTime ?? 0,
            row?.FaceShotCnt ?? 0,
            row?.IsBanFaceShot ?? 0,
            awardTimes);
    }
}
