using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed class MailRepository(DataOptions options)
{
    private const int OneRow = 1;
    private const int FirstMailIndex = 0;
    private const int MaxInsertAttempts = 3;

    private static readonly IReadOnlyDictionary<int, long> NoCoinNums = new Dictionary<int, long>();

    public async Task<RoleMail> InsertAsync(RoleMail mail)
    {
        await using var database = new DataConnection(options);
        for (var attempt = 1; ; attempt++)
        {
            var lastMailIndex = await database.GetTable<RoleMail>()
                .Where(saved => saved.GameUlongUID == mail.GameUlongUID)
                .MaxAsync(saved => (int?)saved.MailIndex);
            mail.MailIndex = lastMailIndex + 1 ?? FirstMailIndex;
            try
            {
                await database.InsertAsync(mail);
                return mail;
            }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry && attempt < MaxInsertAttempts)
            {
            }
        }
    }

    public async Task<int[]> CountUnreadAsync(ulong gameUlongUID, long now)
    {
        await using var database = new DataConnection(options);
        var unread = await database.GetTable<RoleMail>()
            .Where(mail => mail.GameUlongUID == gameUlongUID && mail.MailState == MailBox.Unread && mail.ExpireTime > now)
            .GroupBy(mail => mail.MailType)
            .Select(group => new { MailType = group.Key, Count = group.Count() })
            .ToArrayAsync();
        var counts = new int[MailBox.MailTypeCount];
        foreach (var type in unread.Where(type => type.MailType is >= 0 and < MailBox.MailTypeCount))
        {
            counts[type.MailType] = type.Count;
        }

        return counts;
    }

    public async Task<RoleMail[]> GetMailsAsync(ulong gameUlongUID, int mailType, long now, int maxMailNum)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleMail>()
            .Where(mail => mail.GameUlongUID == gameUlongUID && mail.MailType == mailType && mail.ExpireTime > now)
            .OrderByDescending(mail => mail.MailIndex)
            .Take(maxMailNum)
            .ToArrayAsync();
    }

    public async Task<RoleMail?> FindAsync(ulong gameUlongUID, int mailType, int mailIndex, long now)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleMail>()
            .FirstOrDefaultAsync(mail => mail.GameUlongUID == gameUlongUID
                && mail.MailType == mailType
                && mail.MailIndex == mailIndex
                && mail.ExpireTime > now);
    }

    public async Task MarkReadAsync(ulong gameUlongUID, IReadOnlyCollection<int> mailIndexes)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleMail>()
            .Where(mail => mail.GameUlongUID == gameUlongUID && mailIndexes.Contains(mail.MailIndex))
            .Set(mail => mail.MailState, MailBox.Haveread)
            .UpdateAsync();
    }

    public async Task<int> DeleteAsync(ulong gameUlongUID, IReadOnlyCollection<int> mailIndexes)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleMail>()
            .Where(mail => mail.GameUlongUID == gameUlongUID
                && mailIndexes.Contains(mail.MailIndex)
                && mail.AccessState != MailBox.AccessUnget)
            .DeleteAsync();
    }

    public async Task<int> DeleteExpiredAsync(ulong gameUlongUID, long now)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleMail>()
            .Where(mail => mail.GameUlongUID == gameUlongUID && mail.ExpireTime <= now)
            .DeleteAsync();
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, RoleWallet? Wallet, IReadOnlyDictionary<int, long> BeforeCoinNums)> ClaimAsync(
        ulong gameUlongUID,
        int mailIndex,
        MailGrant grant,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var claimed = await database.GetTable<RoleMail>()
            .Where(mail => mail.GameUlongUID == gameUlongUID
                && mail.MailIndex == mailIndex
                && mail.AccessState == MailBox.AccessUnget
                && mail.ExpireTime > now)
            .Set(mail => mail.AccessState, MailBox.AccessGeted)
            .Set(mail => mail.MailState, MailBox.Haveread)
            .UpdateAsync();
        if (claimed != OneRow)
        {
            await transaction.RollbackAsync();
            return (PB_ENUM_RES_ERROR_CODE.PbErrMailGetAccessFail, null, NoCoinNums);
        }

        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var (coinID, coinCnt) in grant.Coins)
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
            await WalletRepository.CreditAsync(database, gameUlongUID, coinID, coinCnt);
        }

        try
        {
            foreach (var item in grant.Items)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }

            foreach (var pokemonId in grant.PokemonIds)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, new RolePokemonInfo(
                    pokemonId,
                    now,
                    (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonMail,
                    RolePokemonInfo.HasNewTag)));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return (PB_ENUM_RES_ERROR_CODE.PbErrMailGetAccessFail, null, NoCoinNums);
        }

        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wallet, beforeCoinNums);
    }
}
