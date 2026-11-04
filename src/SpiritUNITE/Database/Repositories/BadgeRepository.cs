using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Badge;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed class BadgeRepository(DataOptions options)
{
    private const int OneRow = 1;

    public async Task<IReadOnlyList<BadgeSchedue>> LoadSchedulesAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleBadgeSchedue>()
            .Where(schedue => schedue.GameUlongUID == gameUlongUID)
            .ToArrayAsync();
        return [.. rows.Select(ToBadgeSchedue)];
    }

    public async Task<int?> FindCurBadgeSchedueIdAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleBadge>().FirstOrDefaultAsync(badge => badge.GameUlongUID == gameUlongUID);
        return row?.CurBadgeSchedueId;
    }

    public async Task SetCurBadgeSchedueIdAsync(ulong gameUlongUID, int curBadgeSchedueId)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(new RoleBadge
        {
            GameUlongUID = gameUlongUID,
            CurBadgeSchedueId = curBadgeSchedueId,
        });
    }

    public async Task SaveSchedueAsync(ulong gameUlongUID, BadgeSchedue schedue)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(ToEntity(gameUlongUID, schedue));
    }

    public async Task SaveSchedulesAsync(ulong gameUlongUID, IEnumerable<BadgeSchedue> schedules)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        foreach (var schedue in schedules)
        {
            await database.InsertOrReplaceAsync(ToEntity(gameUlongUID, schedue));
        }

        await transaction.CommitAsync();
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, RoleWallet? Wallet)> UnlockSchedueAsync(
        ulong gameUlongUID,
        BadgeSchedue unlocked,
        int coinID,
        long cost)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var debited = await database.GetTable<RoleCoin>()
            .Where(coin => coin.GameUlongUID == gameUlongUID && coin.CoinID == coinID && coin.CoinVal >= cost)
            .Set(coin => coin.CoinVal, coin => coin.CoinVal - cost)
            .UpdateAsync();
        if (debited != OneRow)
        {
            await transaction.RollbackAsync();
            return (PB_ENUM_RES_ERROR_CODE.PbErrUnlockMoneyNotEnough, null);
        }

        var alreadyUnlocked = await database.GetTable<RoleBadgeSchedue>().AnyAsync(schedue =>
            schedue.GameUlongUID == gameUlongUID
            && schedue.BadgeSchedueId == unlocked.BadgeSchedueId
            && schedue.UnlockTimeStamp != BadgeSchedue.Locked);
        if (alreadyUnlocked)
        {
            await transaction.RollbackAsync();
            return (PB_ENUM_RES_ERROR_CODE.PbErrBadgeSchedueAlreadyUnlocked, null);
        }

        await database.InsertOrReplaceAsync(ToEntity(gameUlongUID, unlocked));
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wallet);
    }

    private static RoleBadgeSchedue ToEntity(ulong gameUlongUID, BadgeSchedue schedue) => new()
    {
        GameUlongUID = gameUlongUID,
        BadgeSchedueId = schedue.BadgeSchedueId,
        SchedueName = schedue.SchedueName,
        SchedueData = schedue.BuildSchedueData().ToByteArray(),
        UnlockTimeStamp = schedue.UnlockTimeStamp,
    };

    private static BadgeSchedue ToBadgeSchedue(RoleBadgeSchedue row) => new(
        row.BadgeSchedueId,
        row.SchedueName,
        [.. pbCOMDT_BADGE_SCHEDUE_DATA.Parser.ParseFrom(row.SchedueData).CellList.Select(cell => cell.BadgeID)],
        row.UnlockTimeStamp);
}
