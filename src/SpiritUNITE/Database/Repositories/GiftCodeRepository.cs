using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.GiftCode;

namespace SpiritUNITE.Database.Repositories;

public sealed record GiftCodeRedeemCommit(GiftCodeRedeemResult Result, RoleMail? Mail = null);

public sealed class GiftCodeRepository(DataOptions options)
{
    private const int NoRows = 0;

    public async Task<bool> TryInsertAsync(GiftCodeRow code, IReadOnlyList<GiftCodeAward> awards)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(code);
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return false;
        }

        foreach (var award in awards)
        {
            await database.InsertAsync(award);
        }

        await transaction.CommitAsync();
        return true;
    }

    public async Task<int> CountAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GiftCodeRow>().CountAsync();
    }

    public async Task<GiftCodeRow[]> ListAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GiftCodeRow>().OrderByDescending(code => code.CreateTime).ThenBy(code => code.GiftCode).ToArrayAsync();
    }

    public async Task<GiftCodeAward[]> ListAwardsAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GiftCodeAward>().OrderBy(award => award.GiftCode).ThenBy(award => award.AwardIndex).ToArrayAsync();
    }

    public async Task<bool> ExistsAsync(string giftCode)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GiftCodeRow>().AnyAsync(code => code.GiftCode == giftCode);
    }

    public async Task<GiftCodeUse[]> ListUsesAsync(string giftCode, int limit)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GiftCodeUse>()
            .Where(use => use.GiftCode == giftCode)
            .OrderByDescending(use => use.UseTime)
            .ThenBy(use => use.GameUlongUID)
            .Take(limit)
            .ToArrayAsync();
    }

    public async Task<bool> SetEnabledAsync(string giftCode, bool isEnabled)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GiftCodeRow>()
            .Where(code => code.GiftCode == giftCode)
            .Set(code => code.IsEnabled, isEnabled)
            .UpdateAsync() > NoRows;
    }

    public async Task<bool> DeleteAsync(string giftCode)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GiftCodeRow>().Where(code => code.GiftCode == giftCode).DeleteAsync() > NoRows;
    }

    public async Task<GiftCodeRedeemCommit> RedeemAsync(
        string giftCode,
        ulong gameUlongUID,
        long nowCfgTime,
        long useTime,
        Func<IReadOnlyList<GiftCodeAward>, RoleMail> createMail)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var claimed = await database.GetTable<GiftCodeRow>()
            .Where(code => code.GiftCode == giftCode && code.IsEnabled
                && (code.ValidStartTime == GiftCodeManager.NoValidStartTime || code.ValidStartTime <= nowCfgTime)
                && code.ValidEndTime >= nowCfgTime
                && (code.MaxUseCnt == GiftCodeManager.UnlimitedUseCnt || code.UseCnt < code.MaxUseCnt))
            .Set(code => code.UseCnt, code => code.UseCnt + 1)
            .UpdateAsync();
        if (claimed == NoRows)
        {
            var refusal = await FindRefusalAsync(database, giftCode, gameUlongUID, nowCfgTime);
            await transaction.RollbackAsync();
            return new GiftCodeRedeemCommit(refusal);
        }

        if (await HasUsedAsync(database, giftCode, gameUlongUID))
        {
            await transaction.RollbackAsync();
            return new GiftCodeRedeemCommit(GiftCodeRedeemResult.AlreadyUsed);
        }

        var awards = await database.GetTable<GiftCodeAward>()
            .Where(award => award.GiftCode == giftCode)
            .OrderBy(award => award.AwardIndex)
            .ToArrayAsync();
        var saved = await MailRepository.InsertAsync(database, createMail(awards));
        try
        {
            await database.InsertAsync(new GiftCodeUse
            {
                GiftCode = giftCode,
                GameUlongUID = gameUlongUID,
                UseTime = useTime,
                MailIndex = saved.MailIndex,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return new GiftCodeRedeemCommit(GiftCodeRedeemResult.AlreadyUsed);
        }

        await transaction.CommitAsync();
        return new GiftCodeRedeemCommit(GiftCodeRedeemResult.Redeemed, saved);
    }

    private static async Task<GiftCodeRedeemResult> FindRefusalAsync(DataConnection database, string giftCode, ulong gameUlongUID, long nowCfgTime)
    {
        var code = await database.GetTable<GiftCodeRow>().FirstOrDefaultAsync(row => row.GiftCode == giftCode);
        if (code is null || !code.IsEnabled)
        {
            return GiftCodeRedeemResult.Invalid;
        }

        if (await HasUsedAsync(database, giftCode, gameUlongUID))
        {
            return GiftCodeRedeemResult.AlreadyUsed;
        }

        if (code.ValidStartTime != GiftCodeManager.NoValidStartTime && code.ValidStartTime > nowCfgTime)
        {
            return GiftCodeRedeemResult.NotStarted;
        }

        return code.ValidEndTime < nowCfgTime ? GiftCodeRedeemResult.Expired : GiftCodeRedeemResult.UsedUp;
    }

    private static Task<bool> HasUsedAsync(DataConnection database, string giftCode, ulong gameUlongUID) =>
        database.GetTable<GiftCodeUse>().AnyAsync(use => use.GiftCode == giftCode && use.GameUlongUID == gameUlongUID);
}
