using System.Text.Json;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Bag;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public enum PveBonusClaimState
{
    Unavailable = 0,
    Available = 1,
    Claimed = 2,
}

public sealed record PveSettlementEligibility(string SettlementID, long BattleStartedAt, PveChallengeNodeKey Node, PveBonusClaimState State, IReadOnlyList<int> RefreshDrops);

public sealed class PveSettlementRepository(DataOptions options)
{
    public async Task<PveSettlementEligibility?> LoadAsync(ulong uid)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RolePveSettlement>().FirstOrDefaultAsync(row => row.GameUlongUID == uid);
        return row is null ? null : new PveSettlementEligibility(
            row.SettlementID,
            row.BattleStartedAt,
            new PveChallengeNodeKey(row.StageID, row.NodeID, row.DifficultyLevel),
            (PveBonusClaimState)row.ClaimState,
            JsonSerializer.Deserialize<int[]>(row.RefreshDrops) ?? []);
    }

    public async Task<BagCommit> ClaimAsync(ulong uid, PveSettlementEligibility eligibility, BagChange change, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TaskProcessRepository.LockRoleAsync(database, uid))
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrUsePveSettleBonusCardFail);
        }

        var claimed = await database.GetTable<RolePveSettlement>()
            .Where(row => row.GameUlongUID == uid && row.SettlementID == eligibility.SettlementID
                && row.ClaimState == (int)PveBonusClaimState.Available)
            .Set(row => row.ClaimState, (int)PveBonusClaimState.Claimed)
            .UpdateAsync();
        if (claimed != 1)
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrUsePveSettleBonusCardFail);
        }

        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var coinID in change.CoinGrants.Keys)
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, uid, coinID);
        }

        var applied = await BagRepository.ApplyAsync(database, uid, change, now);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(applied);
        }

        var items = await BagRepository.ReadItemsAsync(database, uid, change.TouchedItems);
        var wallet = change.ChangesCoins ? await WalletRepository.ReadAsync(database, uid) : null;
        await transaction.CommitAsync();
        return new BagCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, items, wallet) { BeforeCoinNums = beforeCoinNums };
    }

    internal static async Task SaveAsync(DataConnection database, ulong uid, PveSettlementEligibility eligibility)
    {
        var previous = await database.GetTable<RolePveSettlement>().FirstOrDefaultAsync(row => row.GameUlongUID == uid);
        if (previous is not null && (previous.SettlementID == eligibility.SettlementID || previous.BattleStartedAt > eligibility.BattleStartedAt))
        {
            return;
        }

        var drops = JsonSerializer.Serialize(eligibility.RefreshDrops);
        await database.GetTable<RolePveSettlement>().InsertOrUpdateAsync(
            () => new RolePveSettlement
            {
                GameUlongUID = uid,
                SettlementID = eligibility.SettlementID,
                BattleStartedAt = eligibility.BattleStartedAt,
                StageID = eligibility.Node.StageID,
                NodeID = eligibility.Node.NodeID,
                DifficultyLevel = eligibility.Node.DifficultyLevel,
                ClaimState = (int)eligibility.State,
                RefreshDrops = drops,
            },
            _ => new RolePveSettlement
            {
                SettlementID = eligibility.SettlementID,
                BattleStartedAt = eligibility.BattleStartedAt,
                StageID = eligibility.Node.StageID,
                NodeID = eligibility.Node.NodeID,
                DifficultyLevel = eligibility.Node.DifficultyLevel,
                ClaimState = (int)eligibility.State,
                RefreshDrops = drops,
            });
    }
}
