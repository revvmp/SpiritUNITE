using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Bag;

public sealed record BagOutcome(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<BagItem> ChangedItems, RoleWallet? Wallet = null)
{
    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;

    public static BagOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);

    public static BagOutcome From(BagCommit commit) => new(commit.Result, commit.ChangedItems, commit.Wallet);
}

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

    public async Task<PB_ENUM_RES_ERROR_CODE> ValidateUseOutSideItemAsync(
        ulong gameUlongUID,
        int resID,
        ulong gridUniqueID,
        int resCnt)
    {
        var validation = ValidateGrid(resID, gridUniqueID, resCnt, out _);
        if (validation != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return validation;
        }

        var owned = await FindOwnedOutSideItemAsync(gameUlongUID, resID);
        return owned is null || owned.Cnt < resCnt
            ? PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemDeleteNotEnough
            : PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUnKnownUseType;
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

    private PB_ENUM_RES_ERROR_CODE ValidateGrid(int resID, ulong gridUniqueID, int resCnt, out pbOutSideItem_Base? config)
    {
        config = null;
        if (resCnt <= 0)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum;
        }

        if (!TryGetItemId(gridUniqueID, out var itemId) || itemId != resID)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemResIduniqueIdnotMatch;
        }

        config = gameData.OutSideItemBase.Find(resID);
        return config is null
            ? PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }
}
