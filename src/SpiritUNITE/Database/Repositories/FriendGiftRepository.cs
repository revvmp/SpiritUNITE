using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record FriendGiftRules(int ResGiftCountMax, int ResGiftRecvCountMax, int GiftCoinIntimacyValue);

public sealed record FriendGiftCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    RoleMail? Mail = null,
    RoleFriend? SenderFriend = null,
    RoleFriend? ReceiverFriend = null);

public sealed class FriendGiftRepository(DataOptions options)
{
    private const int TwoRoles = 2;
    private const int FirstRecv = 1;

    public async Task<RoleDailyGiftCoin[]> GetDailyGiftCoinsAsync(ulong gameUlongUID, long giftDay)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleDailyGiftCoin>()
            .Where(gift => gift.GameUlongUID == gameUlongUID && gift.GiftDay == giftDay)
            .OrderBy(gift => gift.GiftTime)
            .ToArrayAsync();
    }

    public async Task<FriendGiftCommit> DailyGiftCoinAsync(
        ulong gameUlongUID,
        ulong friendUid,
        long giftDay,
        long now,
        FriendGiftRules rules,
        RoleMail mail)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var refusal = await FindRefusalAsync(database, gameUlongUID, friendUid, giftDay, rules);
        if (refusal != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return new FriendGiftCommit(refusal);
        }

        await database.GetTable<RoleDailyGiftCoin>()
            .Where(gift => gift.GameUlongUID == gameUlongUID && gift.GiftDay < giftDay)
            .DeleteAsync();
        await database.GetTable<RoleDailyGiftCoinRecv>()
            .Where(recv => recv.GameUlongUID == friendUid && recv.GiftDay < giftDay)
            .DeleteAsync();
        await database.InsertAsync(new RoleDailyGiftCoin
        {
            GameUlongUID = gameUlongUID,
            GiftDay = giftDay,
            DailyGiftCoinUid = friendUid,
            GiftTime = now,
        });
        await database.GetTable<RoleDailyGiftCoinRecv>()
            .InsertOrUpdateAsync(
                () => new RoleDailyGiftCoinRecv { GameUlongUID = friendUid, GiftDay = giftDay, RecvCount = FirstRecv },
                recv => new RoleDailyGiftCoinRecv { RecvCount = recv.RecvCount + 1 });
        var saved = await MailRepository.InsertAsync(database, mail);
        if (rules.GiftCoinIntimacyValue > 0)
        {
            await FriendRepository.AddIntimacyAsync(database, gameUlongUID, [friendUid], rules.GiftCoinIntimacyValue);
            await FriendRepository.AddIntimacyAsync(database, friendUid, [gameUlongUID], rules.GiftCoinIntimacyValue);
        }

        var senderFriend = await database.GetTable<RoleFriend>()
            .FirstAsync(friend => friend.GameUlongUID == gameUlongUID && friend.FriendUid == friendUid);
        var receiverFriend = await database.GetTable<RoleFriend>()
            .FirstOrDefaultAsync(friend => friend.GameUlongUID == friendUid && friend.FriendUid == gameUlongUID);
        await transaction.CommitAsync();
        return new FriendGiftCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, saved, senderFriend, receiverFriend);
    }

    private static async Task<PB_ENUM_RES_ERROR_CODE> FindRefusalAsync(
        DataConnection database,
        ulong gameUlongUID,
        ulong friendUid,
        long giftDay,
        FriendGiftRules rules)
    {
        if (gameUlongUID == friendUid
            || !await LockRolesAsync(database, gameUlongUID, friendUid)
            || !await database.GetTable<RoleFriend>().AnyAsync(friend => friend.GameUlongUID == gameUlongUID && friend.FriendUid == friendUid))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendNotFriend;
        }

        var dailyGiftCoinUids = await database.GetTable<RoleDailyGiftCoin>()
            .Where(gift => gift.GameUlongUID == gameUlongUID && gift.GiftDay == giftDay)
            .Select(gift => gift.DailyGiftCoinUid)
            .ToArrayAsync();
        if (dailyGiftCoinUids.Length >= rules.ResGiftCountMax)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendDailyGiftCoinCountLimit;
        }

        if (dailyGiftCoinUids.Contains(friendUid))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendDailyGiftCoinAlreadyGift;
        }

        var recvCount = await database.GetTable<RoleDailyGiftCoinRecv>()
            .Where(recv => recv.GameUlongUID == friendUid && recv.GiftDay == giftDay)
            .Select(recv => (int?)recv.RecvCount)
            .FirstOrDefaultAsync() ?? 0;
        return recvCount >= rules.ResGiftRecvCountMax
            ? PB_ENUM_RES_ERROR_CODE.PbErrSendCoinRecvLimit
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static async Task<bool> LockRolesAsync(DataConnection database, ulong gameUlongUID, ulong friendUid) =>
        (await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID || role.GameUlongUID == friendUid)
            .OrderBy(role => role.GameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .Select(role => role.GameUlongUID)
            .ToArrayAsync()).Length == TwoRoles;
}
