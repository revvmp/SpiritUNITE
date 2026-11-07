using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Bag;

public sealed record BagOutcome(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<BagItem> ChangedItems, RoleWallet? Wallet = null)
{
    public IReadOnlyDictionary<int, long> BeforeCoinNums { get; init; } = new Dictionary<int, long>();
    public IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards { get; init; } = [];
    public IReadOnlyList<int> GrantedPokemonIds { get; init; } = [];

    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;

    public IEnumerable<int> GrantedSpecialCoinIDs => BeforeCoinNums.Keys.Where(CoinID.IsSpecialCoin);

    public bool GrantedWalletCoins => BeforeCoinNums.Keys.Any(CoinID.WalletCoins.Contains);

    public static BagOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);

    public static BagOutcome From(BagCommit commit) =>
        new(commit.Result, commit.ChangedItems, commit.Wallet) { BeforeCoinNums = commit.BeforeCoinNums };
}

public sealed record ChestDrop(int ResID, int ResCnt);

public sealed class BagManager(GameData gameData, BagRepository repository, TimeProvider clock)
{
    public const int HeldItemCategory = (int)PB_ENUM_RES_OUTSIDEITEM_CATEGORY.PbResOutsideitemCategoryHelditem;
    public const int TrainerItemCategory = (int)PB_ENUM_RES_OUTSIDEITEM_CATEGORY.PbResOutsideitemCategoryTraineritem;

    private const int Enabled = 1;
    private const int InitialHeldItemLevel = 1;
    private const int NoItemLevel = 0;
    private const int SingleItem = 1;
    private const int NoBulkLimit = 0;
    private const int UnlimitedStackSize = int.MaxValue;
    private const int NoNewTag = 0;
    private const int ShowNewTag = 1;
    private const int NoDropID = 0;
    private const int FirstDropSeq = 1;
    private const int OpenAwardBoxReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonOpenawardbox;
    private const ulong NoGridUniqueID = 0;

    public async Task<BagItem[]> GetOwnedOutSideItemsAsync(ulong gameUlongUID) =>
        [.. (await repository.LoadAsync(gameUlongUID, RoleItemType.OutSideItem)).Where(item => item.IsOwned)];

    public async Task<BagItem?> FindOwnedOutSideItemAsync(ulong gameUlongUID, int outSideItemId) =>
        await repository.FindAsync(gameUlongUID, RoleItemType.OutSideItem, outSideItemId) is { IsOwned: true } item
            ? item
            : null;

    public async Task<pbSCOutSideItemPackageAllInfoRsp> BuildOutSideItemPackageAllInfoRspAsync(ulong gameUlongUID) => new()
    {
        ResultInfo = new pbResultInfo(),
        OutSideItemGridInfoList =
        {
            (await GetOwnedOutSideItemsAsync(gameUlongUID)).Select(item => item.ToOutSideItemGridInfo()),
        },
    };

    public static pbSCOutSideItemUpdateNotify BuildOutSideItemUpdateNotify(IEnumerable<BagItem> changedItems) => new()
    {
        ResultInfo = new pbResultInfo(),
        UpdateOutSideItemGridInfoList =
        {
            changedItems.Where(item => item.ItemType == RoleItemType.OutSideItem)
                .Select(item => item.ToOutSideItemGridInfo()),
        },
    };

    public BagGrant? CreateOutSideItemGrant(int outSideItemId, int cnt)
    {
        if (gameData.OutSideItemBase.Find(outSideItemId) is not { } config || cnt <= 0)
        {
            return null;
        }

        var stackSize = config.OutSideItemStackSize > 0 ? config.OutSideItemStackSize : UnlimitedStackSize;
        var isHeldItem = config.OutSideItemCategory == HeldItemCategory;
        return new BagGrant(
            RoleItemType.OutSideItem,
            outSideItemId,
            cnt,
            stackSize,
            isHeldItem ? InitialHeldItemLevel : NoItemLevel,
            isHeldItem ? ShowNewTag : NoNewTag);
    }

    public async Task<BagOutcome> SellOutSideItemAsync(ulong gameUlongUID, int resID, ulong gridUniqueID, int resCnt)
    {
        var validation = ValidateGrid(resID, gridUniqueID, resCnt, out var config);
        if (config is null)
        {
            return BagOutcome.Refused(validation);
        }

        if (config.IsCanSell != Enabled || config.SellPrice <= 0)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemCanNotBeSell);
        }

        if (resCnt > SingleItem
            && (config.IsCanBulkSell != Enabled || (config.BulkSellLimit != NoBulkLimit && resCnt > config.BulkSellLimit)))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum);
        }

        if (CoinID.GetCoinID((PB_ENUM_RES_COIN_TYPE)config.SellCurrency) is not { } sellCoinID)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrNotCoinCfgNotExist);
        }

        var owned = await FindOwnedOutSideItemAsync(gameUlongUID, resID);
        if (owned is null || owned.Cnt < resCnt)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemDeleteNotEnough);
        }

        var change = new BagChange
        {
            Costs = [new BagCost(RoleItemType.OutSideItem, resID, resCnt)],
            CoinGrants = new Dictionary<int, long> { [sellCoinID] = (long)config.SellPrice * resCnt },
        };
        return BagOutcome.From(await repository.CommitAsync(gameUlongUID, change, clock.GetUtcNow().ToUnixTimeSeconds()));
    }

    public async Task<BagOutcome> ConsumeOutSideItemAsync(ulong gameUlongUID, int itemId, int cnt) =>
        BagOutcome.From(await repository.CommitAsync(
            gameUlongUID,
            new BagChange { Costs = [new BagCost(RoleItemType.OutSideItem, itemId, cnt)] },
            clock.GetUtcNow().ToUnixTimeSeconds()));

    public async Task<BagOutcome> UseOutSideItemAsync(LMNRoleInfo role, int resID, ulong gridUniqueID, int resCnt)
    {
        var (validation, config) = await ValidateUseAsync(role.GameUlongUID, resID, gridUniqueID, resCnt);
        if (config is null)
        {
            return BagOutcome.Refused(validation);
        }

        return config.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeChest
            ? await OpenChestAsync(role, config, resCnt)
            : BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUnKnownUseType);
    }

    public bool IsFreeTicketCard(int resID) =>
        gameData.OutSideItemBase.Find(resID)?.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeFreeTicketCard;

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, pbOutSideItem_Base? Config)> ValidateUseAsync(ulong gameUlongUID, int resID, ulong gridUniqueID, int resCnt)
    {
        var validation = ValidateGrid(resID, gridUniqueID, resCnt, out var config);
        if (config is null)
        {
            return (validation, null);
        }

        var owned = await FindOwnedOutSideItemAsync(gameUlongUID, resID);
        return owned is null || owned.Cnt < resCnt
            ? (PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemDeleteNotEnough, null)
            : (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, config);
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ClearOutSideItemRedDotAsync(ulong gameUlongUID, ulong gridUniqueID)
    {
        if (!TryGetItemId(gridUniqueID, out var itemId)
            || await FindOwnedOutSideItemAsync(gameUlongUID, itemId) is null)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemNotFoundResInAcnt;
        }

        await repository.ClearNewTagAsync(gameUlongUID, RoleItemType.OutSideItem, itemId);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public static pbCOMDT_REQ_INFO? BuildUseInfo(int type) =>
        type == (int)PB_ENUM_ENM_REQ_INFO_TYPE.PbEnmReqInfoTypeRenamecdlimit
            ? new pbCOMDT_REQ_INFO { RenameCDLimitInfo = new pbCOMDT_REQ_INFO_RENAMECDLIMIT() }
            : null;

    public async Task<BagOutcome> UseRenameCardAsync(LMNRoleInfo role, int resID, ulong gridUniqueID, string newUserName)
    {
        var gridResult = ValidateGrid(resID, gridUniqueID, SingleItem, out var config);
        if (gridResult != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return BagOutcome.Refused(gridResult);
        }

        if (config?.OutSideItemType != (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeRenamecard)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUnKnownUseType);
        }

        var nameResult = RoleInfoStore.ValidateName(newUserName);
        if (nameResult != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return BagOutcome.Refused(nameResult);
        }

        var outcome = BagOutcome.From(await repository.UseRenameCardAsync(role.GameUlongUID, resID, newUserName));
        if (outcome.Succeeded)
        {
            role.ChgInGameUsrName(newUserName);
        }

        return outcome;
    }

    public static bool TryGetItemId(ulong gridUniqueID, out int itemId)
    {
        var valid = gridUniqueID is > 0 and <= int.MaxValue;
        itemId = valid ? (int)gridUniqueID : 0;
        return valid;
    }

    private async Task<BagOutcome> OpenChestAsync(LMNRoleInfo role, pbOutSideItem_Base chest, int openCnt)
    {
        var dropIDs = chest.ItemParam.Where(param => param != NoDropID).ToArray();
        if (dropIDs.Length == 0)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDropSystemUnValidDropId);
        }

        var dropPacks = dropIDs.Select(FindDropPackIDs).ToArray();
        if (dropPacks.Any(dropPackIDs => dropPackIDs.Count == 0))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDropSystemUnValidDropId);
        }

        var packEntries = dropPacks.SelectMany(dropPackIDs => dropPackIDs).Distinct().ToDictionary(id => id, FindDropPackEntries);
        if (!packEntries.Values.All(IsRollable))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDropSystemDropPackFail);
        }

        var rolledCnts = new Dictionary<int, long>();
        for (var opened = 0; opened < openCnt; opened++)
        {
            foreach (var dropPackID in dropPacks.SelectMany(dropPackIDs => dropPackIDs))
            {
                var drop = RollDropPack(packEntries[dropPackID]);
                rolledCnts[drop.ResID] = rolledCnts.GetValueOrDefault(drop.ResID) + drop.ResCnt;
            }
        }

        var coinGrants = new Dictionary<int, long>();
        var grants = new List<BagGrant>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        var pokemonIds = new List<int>();
        foreach (var (resID, rolledCnt) in rolledCnts)
        {
            if (rolledCnt > int.MaxValue)
            {
                return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum);
            }

            var resCnt = (int)rolledCnt;
            if (CoinID.IsGrantableCoin(resID))
            {
                coinGrants[resID] = coinGrants.GetValueOrDefault(resID) + resCnt;
                awards.Add(BuildCoinAward(resID, resCnt));
            }
            else if (UniteLicense.IsUniteLicense(gameData, resID))
            {
                if (UniteLicense.PlanGrant(gameData, role, resID, resCnt, pokemonIds) is not { } license)
                {
                    return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDropSystemDropPackFail);
                }

                license.AddTo(pokemonIds, coinGrants, awards);
            }
            else if (CreateOutSideItemGrant(resID, resCnt) is { } grant)
            {
                grants.Add(grant);
                awards.Add(BuildAward(resID, resCnt, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
            }
            else
            {
                return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDropSystemDropPackFail);
            }
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var change = new BagChange
        {
            Costs = [new BagCost(RoleItemType.OutSideItem, chest.OutSideItemId, openCnt)],
            Grants = grants,
            CoinGrants = coinGrants,
            PokemonGrants = [.. pokemonIds.Select(pokemonId => new RolePokemonInfo(pokemonId, now, OpenAwardBoxReason, RolePokemonInfo.HasNewTag))],
        };
        var outcome = BagOutcome.From(await repository.CommitAsync(role.GameUlongUID, change, now));
        if (!outcome.Succeeded)
        {
            return outcome;
        }

        foreach (var pokemonId in pokemonIds)
        {
            role.GrantPokemon(pokemonId, now, OpenAwardBoxReason);
        }

        return outcome with { Awards = awards, GrantedPokemonIds = pokemonIds };
    }

    private List<int> FindDropPackIDs(int dropID)
    {
        var dropPackIDs = new List<int>();
        for (var dropSeq = FirstDropSeq; gameData.DropBase.Find(pbDrop_Base.Key(dropID, dropSeq)) is { } dropBase; dropSeq++)
        {
            dropPackIDs.Add(dropBase.DropPackID);
        }

        return dropPackIDs;
    }

    private pbDrop_Pack[] FindDropPackEntries(int dropPackID)
    {
        var entries = new List<pbDrop_Pack>();
        for (var dropPackSeq = FirstDropSeq; gameData.DropPack.Find(pbDrop_Pack.Key(dropPackID, dropPackSeq)) is { } entry; dropPackSeq++)
        {
            entries.Add(entry);
        }

        return [.. entries.Where(entry => entry.Weight > 0)];
    }

    private bool IsRollable(pbDrop_Pack[] entries) =>
        entries.Length > 0 && entries.All(entry =>
            entry.MinDropCount > 0
            && entry.MaxDropCount >= entry.MinDropCount
            && (CoinID.IsGrantableCoin(entry.ResID) || gameData.OutSideItemBase.Contains(entry.ResID)));

    private static ChestDrop RollDropPack(pbDrop_Pack[] entries)
    {
        var pick = Random.Shared.NextInt64(entries.Sum(entry => (long)entry.Weight));
        var index = 0;
        while (pick >= entries[index].Weight)
        {
            pick -= entries[index].Weight;
            index++;
        }

        var picked = entries[index];
        return new ChestDrop(picked.ResID, Random.Shared.Next(picked.MinDropCount, picked.MaxDropCount + 1));
    }

    public static pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO BuildAward(int resID, int resCnt, PB_ENUM_RES_RESOURCES_TYPE resType) => new()
    {
        FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO { ResID = resID, ResCnt = resCnt, ResType = (int)resType },
        OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
    };

    public static pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO BuildCoinAward(int coinID, int coinCnt) =>
        BuildAward(coinID, coinCnt, CoinID.GetCoinResourceType(coinID));

    private PB_ENUM_RES_ERROR_CODE ValidateGrid(int resID, ulong gridUniqueID, int resCnt, out pbOutSideItem_Base? config)
    {
        config = null;
        if (resCnt <= 0)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum;
        }

        if (gridUniqueID != NoGridUniqueID && (!TryGetItemId(gridUniqueID, out var itemId) || itemId != resID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemResIduniqueIdnotMatch;
        }

        config = gameData.OutSideItemBase.Find(resID);
        return config is null
            ? PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }
}
