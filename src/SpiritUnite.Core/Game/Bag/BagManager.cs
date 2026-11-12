using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.Bag;

public sealed record BagOutcome(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<BagItem> ChangedItems, RoleWallet? Wallet = null)
{
    public IReadOnlyDictionary<int, long> BeforeCoinNums { get; init; } = new Dictionary<int, long>();
    public IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards { get; init; } = [];
    public IReadOnlyList<int> GrantedPokemonIds { get; init; } = [];
    public IReadOnlyList<RoleItemInfo> GrantedItems { get; init; } = [];
    public IReadOnlyList<RoleItemInfo> TimeLimitedPasters { get; init; } = [];
    public IReadOnlyList<RolePokemonInfo> TimeLimitedPokemon { get; init; } = [];
    public int AwardReason { get; init; } = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonOpenawardbox;
    public long LastRenameTime { get; init; }
    public int RenameRemainSeconds { get; init; }

    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;

    public IEnumerable<int> GrantedSpecialCoinIDs => BeforeCoinNums.Keys.Where(CoinID.IsSpecialCoin);

    public bool GrantedWalletCoins => BeforeCoinNums.Keys.Any(CoinID.WalletCoins.Contains);

    public static BagOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);

    public static BagOutcome From(BagCommit commit) =>
        new(commit.Result, commit.ChangedItems, commit.Wallet)
        {
            BeforeCoinNums = commit.BeforeCoinNums,
            TimeLimitedPasters = commit.TimeLimitedPasters,
            LastRenameTime = commit.LastRenameTime,
        };
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
    private const long SecondsPerDay = 86400;
    private const int FirstDropSeq = 1;
    private const int OpenAwardBoxReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonOpenawardbox;
    private const int OptionalChestReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonOptionalchest;
    private const int ExchangeSpecifiedItemReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonUseExchangeSpecifiedItem;
    private const int OptionalChestDropParamIndex = 0;
    private const int OptionalChestSingleSelectParamIndex = 1;
    private const int OptionalChestExtraDropParamIndex = 2;
    private const int ExchangeNeedNumParamIndex = 0;
    private const int ExchangeRewardItemParamIndex = 1;
    private const int ExchangeNonRepeatableParamIndex = 2;
    private const int RepeatableExchange = 0;
    private const int PasterItemParamIndex = 0;
    private const int PasterTimeParamIndex = 1;
    private const long CfgDateToCfgTime = 1000000;
    private const int GainPasterReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonGainPaster;
    private const int PasterTimeLimitedReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonPastertimelimited;
    private const int PasterTimeLimitedExchangeReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonPastertimelimitedexchange;
    private const int UseOutSideItemReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonUseoutsideitem;
    private const int TrialPokemonParamIndex = 0;
    private const int TrialSecondsParamIndex = 1;

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

    public BagGrant? CreateBadgeGrant(int badgeId, int cnt) =>
        gameData.BadgeBase.Contains(badgeId) && cnt > 0
            ? new BagGrant(RoleItemType.Badge, badgeId, cnt, UnlimitedStackSize, NoItemLevel, ShowNewTag)
            : null;

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
            UsesBagRedDot(config.OutSideItemType) ? ShowNewTag : NoNewTag);
    }

    private bool UsesBagRedDot(int outSideItemType) =>
        gameData.OutSideItemRedDot.Find(outSideItemType) is { IsUseRedDot: Enabled };

    public async Task<BagOutcome> SellOutSideItemAsync(ulong gameUlongUID, int resID, ulong gridUniqueID, int resCnt)
    {
        var (validation, config, owned) = await ValidateGridAsync(gameUlongUID, resID, gridUniqueID, resCnt);
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

        return (PB_ENUM_RES_OUTSIDEITEM_TYPE)config.OutSideItemType switch
        {
            PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeChest => await OpenChestAsync(role, config, resCnt),
            PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePokemonavatarvoucher
                or PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePastertimelimited
                or PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePasterexpiretime => await UsePasterItemAsync(role, config, resCnt),
            PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePokemontimelimited => await UsePokemonTrialAsync(role, config, resCnt),
            _ => BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUnKnownUseType),
        };
    }

    private async Task<BagOutcome> UsePasterItemAsync(LMNRoleInfo role, pbOutSideItem_Base config, int resCnt)
    {
        var pasterID = config.ItemParam.ElementAtOrDefault(PasterItemParamIndex);
        if (!PasterGrant.IsPaster(gameData, pasterID))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrPasterIdinvalid);
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var cost = new BagCost(RoleItemType.OutSideItem, config.OutSideItemId, resCnt);
        var type = (PB_ENUM_RES_OUTSIDEITEM_TYPE)config.OutSideItemType;
        if (role.OwnsItem(RoleItemType.Paster, pasterID))
        {
            return type == PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePokemonavatarvoucher
                ? BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrPasterAlreadyExist)
                : await ExchangeOwnedPasterItemAsync(role, config, cost);
        }

        if (type == PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePokemonavatarvoucher)
        {
            if (resCnt != SingleItem)
            {
                return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum);
            }

            var paster = new RoleItemInfo(RoleItemType.Paster, pasterID, now, RoleItemInfo.Permanent, RoleItemInfo.ShowNewTag);
            var plan = new ResourceGrantPlan([], new Dictionary<int, long>(), [paster.ToAwardReplaceInfo()], [], [paster]);
            return await CommitGrantsAsync(role, cost, plan, GainPasterReason);
        }

        var timeParam = config.ItemParam.ElementAtOrDefault(PasterTimeParamIndex);
        if (timeParam <= 0)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg);
        }

        PasterTimeLimit timeLimit;
        if (type == PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePastertimelimited)
        {
            timeLimit = new PasterTimeLimit(pasterID, (long)timeParam * resCnt, 0);
        }
        else
        {
            var expireTime = UTCTimeUtil.CfgTime2ServerUnixTime(timeParam * CfgDateToCfgTime);
            if (expireTime <= now)
            {
                return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemExpired);
            }

            if (resCnt != SingleItem)
            {
                return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum);
            }

            timeLimit = new PasterTimeLimit(pasterID, 0, expireTime);
        }

        var change = new BagChange { Costs = [cost], PasterTimeLimits = [timeLimit] };
        return BagOutcome.From(await repository.CommitAsync(role.GameUlongUID, change, now)) with { AwardReason = PasterTimeLimitedReason };
    }

    private async Task<BagOutcome> UsePokemonTrialAsync(LMNRoleInfo role, pbOutSideItem_Base config, int resCnt)
    {
        var pokemonId = config.ItemParam.ElementAtOrDefault(TrialPokemonParamIndex);
        var trialSeconds = (long)config.ItemParam.ElementAtOrDefault(TrialSecondsParamIndex) * resCnt;
        if (!gameData.PokemonBase.Contains(pokemonId) || trialSeconds <= 0)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg);
        }

        var cost = new BagCost(RoleItemType.OutSideItem, config.OutSideItemId, resCnt);
        if (role.OwnsPokemon(pokemonId))
        {
            return await ExchangeOwnedTrialAsync(role, config, cost);
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var trialStart = role.GetTimeLimitedPokemon().FirstOrDefault(trial => trial.PokemonId == pokemonId && trial.TimeLimited > now)?.TimeLimited ?? now;
        var trial = new RolePokemonInfo(pokemonId, now, UseOutSideItemReason, RolePokemonInfo.HasNewTag, trialStart + trialSeconds);
        var change = new BagChange { Costs = [cost], TimeLimitedPokemon = [trial] };
        var outcome = BagOutcome.From(await repository.CommitAsync(role.GameUlongUID, change, now));
        if (!outcome.Succeeded)
        {
            return outcome;
        }

        role.GrantTimeLimitedPokemon(trial);
        return outcome with { TimeLimitedPokemon = [trial], AwardReason = UseOutSideItemReason };
    }

    private async Task<BagOutcome> ExchangeOwnedTrialAsync(LMNRoleInfo role, pbOutSideItem_Base config, BagCost cost)
    {
        var transmuteNum = (long)config.TransmuteNum * cost.Cnt;
        if (config.TransmuteNum <= 0 || transmuteNum > int.MaxValue || !CoinID.IsGrantableCoin(config.Transmuteid))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemTransmuteNotCfg);
        }

        var plan = new ResourceGrantPlan(
            [],
            new Dictionary<int, long> { [config.Transmuteid] = transmuteNum },
            [BuildCoinAward(config.Transmuteid, (int)transmuteNum)],
            [],
            []);
        return await CommitGrantsAsync(role, cost, plan, UseOutSideItemReason);
    }

    private async Task<BagOutcome> ExchangeOwnedPasterItemAsync(LMNRoleInfo role, pbOutSideItem_Base config, BagCost cost)
    {
        var transmuteNum = (long)config.TransmuteNum * cost.Cnt;
        if (config.TransmuteNum <= 0 || transmuteNum > int.MaxValue || !CoinID.IsGrantableCoin(config.Transmuteid))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrPasterAlreadyExist);
        }

        var plan = new ResourceGrantPlan(
            [],
            new Dictionary<int, long> { [config.Transmuteid] = transmuteNum },
            [BuildCoinAward(config.Transmuteid, (int)transmuteNum)],
            [],
            []);
        return await CommitGrantsAsync(role, cost, plan, PasterTimeLimitedExchangeReason);
    }

    public bool IsFreeTicketCard(int resID) =>
        gameData.OutSideItemBase.Find(resID)?.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeFreeTicketCard;

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, pbOutSideItem_Base? Config)> ValidateUseAsync(ulong gameUlongUID, int resID, ulong gridUniqueID, int resCnt)
    {
        var (validation, config, owned) = await ValidateGridAsync(gameUlongUID, resID, gridUniqueID, resCnt);
        if (config is null)
        {
            return (validation, null);
        }

        return owned is null || owned.Cnt < resCnt
            ? (PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemDeleteNotEnough, null)
            : (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, config);
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ClearOutSideItemRedDotAsync(ulong gameUlongUID, ulong gridUniqueID)
    {
        if (gridUniqueID == GridUniqueID.None
            || await repository.FindByGridAsync(gameUlongUID, RoleItemType.OutSideItem, gridUniqueID) is not { IsOwned: true } item)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemNotFoundResInAcnt;
        }

        await repository.ClearNewTagAsync(gameUlongUID, RoleItemType.OutSideItem, item.ItemId);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<pbCOMDT_REQ_INFO?> BuildUseInfoAsync(ulong gameUlongUID, int type)
    {
        if (type != (int)PB_ENUM_ENM_REQ_INFO_TYPE.PbEnmReqInfoTypeRenamecdlimit)
        {
            return null;
        }

        var lastRenameTime = await repository.FindLastRenameTimeAsync(gameUlongUID);
        return new pbCOMDT_REQ_INFO
        {
            RenameCDLimitInfo = new pbCOMDT_REQ_INFO_RENAMECDLIMIT
            {
                RemainSeconds = CreateRenameCooldown().RemainSeconds(lastRenameTime),
                LastRenameTime = lastRenameTime,
            },
        };
    }

    private RenameCooldown CreateRenameCooldown() =>
        new(clock.GetUtcNow().ToUnixTimeSeconds(), (long)gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalRenameCddays) * SecondsPerDay);

    public async Task<BagOutcome> UseRenameCardAsync(LMNRoleInfo role, int resID, ulong gridUniqueID, string newUserName)
    {
        var (gridResult, config, _) = await ValidateGridAsync(role.GameUlongUID, resID, gridUniqueID, SingleItem);
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

        var cooldown = CreateRenameCooldown();
        var outcome = BagOutcome.From(await repository.UseRenameCardAsync(role.GameUlongUID, resID, newUserName, cooldown));
        if (outcome.Succeeded)
        {
            role.ChgInGameUsrName(newUserName);
        }

        return outcome.Result == PB_ENUM_RES_ERROR_CODE.PbErrInRenameCdlimit
            ? outcome with { RenameRemainSeconds = cooldown.RemainSeconds(outcome.LastRenameTime) }
            : outcome;
    }

    private async Task<BagOutcome> OpenChestAsync(LMNRoleInfo role, pbOutSideItem_Base chest, int openCnt)
    {
        var dropIDs = chest.ItemParam.Where(param => param != NoDropID).ToArray();
        if (dropIDs.Length == 0)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDropSystemUnValidDropId);
        }

        var staticDrops = dropIDs.SelectMany(FindStaticDrops).ToArray();
        var dropPacks = dropIDs.Where(dropID => FindStaticDrops(dropID).Length == 0).Select(FindDropPackIDs).ToArray();
        if (dropPacks.Any(dropPackIDs => dropPackIDs.Count == 0))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDropSystemUnValidDropId);
        }

        var packEntries = dropPacks.SelectMany(dropPackIDs => dropPackIDs).Distinct().ToDictionary(id => id, FindDropPackEntries);
        if (!packEntries.Values.All(IsRollable))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDropSystemDropPackFail);
        }

        var rolledCnts = new SortedDictionary<int, long>();
        for (var opened = 0; opened < openCnt; opened++)
        {
            foreach (var staticDrop in staticDrops)
            {
                rolledCnts[staticDrop.ResID] = rolledCnts.GetValueOrDefault(staticDrop.ResID) + staticDrop.ResNum;
            }

            foreach (var dropPackID in dropPacks.SelectMany(dropPackIDs => dropPackIDs))
            {
                var drop = RollDropPack(packEntries[dropPackID]);
                rolledCnts[drop.ResID] = rolledCnts.GetValueOrDefault(drop.ResID) + drop.ResCnt;
            }
        }

        return PlanResourceGrants(role, rolledCnts) is { } plan
            ? await CommitGrantsAsync(role, new BagCost(RoleItemType.OutSideItem, chest.OutSideItemId, openCnt), plan, OpenAwardBoxReason)
            : BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDropSystemDropPackFail);
    }

    public PB_ENUM_RES_ERROR_CODE BuildOptionalChestInfo(LMNRoleInfo role, int optionalChestID, out pbCOMDT_REQ_INFO_OPTIONALCHEST info)
    {
        info = new pbCOMDT_REQ_INFO_OPTIONALCHEST();
        var result = FindOptionalChest(optionalChestID, out var chest);
        if (chest is null)
        {
            return result;
        }

        info.ResAlreadyHaveList.Add(FindOptionalChestOptions(chest)
            .Where(option => IsNonRepeatableOwned(role, option.ResID))
            .Select(option => new pbCOMDT_ONE_OPTIONALCHEST_INFO
            {
                ResID = option.ResID,
                ResType = (int)CoinID.GetResIDType(option.ResID),
                AlreadyHaveNum = SingleItem,
                IsNonRepeatable = Enabled,
            }));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<BagOutcome> UseOptionalChestAsync(
        LMNRoleInfo role,
        int resID,
        ulong gridUniqueID,
        int resCnt,
        IReadOnlyList<pbCOMDT_ONE_SELECT_RESOURCE_INFO> chooseResList)
    {
        if (resCnt <= 0)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum);
        }

        var grid = await repository.FindAsync(role.GameUlongUID, RoleItemType.OutSideItem, resID);
        if (!MatchesGrid(grid, gridUniqueID))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemResIduniqueIdnotMatch);
        }

        var chestResult = FindOptionalChest(resID, out var chest);
        if (chest is null)
        {
            return BagOutcome.Refused(chestResult);
        }

        var options = FindOptionalChestOptions(chest)
            .GroupBy(option => option.ResID)
            .ToDictionary(group => group.Key, group => group.MaxBy(option => option.SDropSeq)!);
        if (options.Count == 0)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemOptChestNoContent);
        }

        var extraOption = FindOptionalChestExtraOption(chest);
        if (extraOption is not null)
        {
            options.TryAdd(extraOption.ResID, extraOption);
        }

        var choiceResult = ValidateOptionalChestChoices(chest, options, resCnt, chooseResList);
        if (choiceResult != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return BagOutcome.Refused(choiceResult);
        }

        if (extraOption is not null
            && chooseResList.Any(choice => choice.ResID != extraOption.ResID && IsNonRepeatableOwned(role, choice.ResID)))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemHadResNotOptChest);
        }

        if (grid is not { IsOwned: true } || grid.Cnt < resCnt)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemDeleteNotEnough);
        }

        var resCnts = new Dictionary<int, long>();
        foreach (var choice in chooseResList)
        {
            resCnts[choice.ResID] = resCnts.GetValueOrDefault(choice.ResID) + (long)options[choice.ResID].ResNum * choice.ResCnt;
        }

        return PlanResourceGrants(role, resCnts) is { } plan
            ? await CommitGrantsAsync(role, new BagCost(RoleItemType.OutSideItem, resID, resCnt), plan, OptionalChestReason)
            : BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemOptionalChestSelectResInvalid);
    }

    public async Task<BagOutcome> ExchangeSpecifiedItemAsync(LMNRoleInfo role, int resID, int exchangeNum)
    {
        if (exchangeNum <= 0)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum);
        }

        if (gameData.OutSideItemBase.Find(resID) is not { } config)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg);
        }

        if (config.OutSideItemType != (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeExchangeSpecifiedItem)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUnKnownUseType);
        }

        var exchangeNeedNum = config.ItemParam.ElementAtOrDefault(ExchangeNeedNumParamIndex);
        var rewardItemID = config.ItemParam.ElementAtOrDefault(ExchangeRewardItemParamIndex);
        var isNonRepeatable = config.ItemParam.ElementAtOrDefault(ExchangeNonRepeatableParamIndex) != RepeatableExchange;
        if (exchangeNeedNum <= 0 || rewardItemID <= 0)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg);
        }

        if (isNonRepeatable && exchangeNum != SingleItem)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum);
        }

        if (isNonRepeatable && await IsOwnedAsync(role, rewardItemID))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrExchangeSpecifiedItemExist);
        }

        var costCnt = (long)exchangeNeedNum * exchangeNum;
        if (costCnt > int.MaxValue)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum);
        }

        if (await FindOwnedOutSideItemAsync(role.GameUlongUID, resID) is not { } owned || owned.Cnt < costCnt)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrExchangeSpecifiedItemCostNotEnough);
        }

        return PlanResourceGrants(role, new Dictionary<int, long> { [rewardItemID] = exchangeNum }) is { } plan
            ? await CommitGrantsAsync(role, new BagCost(RoleItemType.OutSideItem, resID, (int)costCnt), plan, ExchangeSpecifiedItemReason)
            : BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUnKnownUseType);
    }

    private PB_ENUM_RES_ERROR_CODE FindOptionalChest(int optionalChestID, out pbOutSideItem_Base? chest)
    {
        var config = gameData.OutSideItemBase.Find(optionalChestID);
        chest = config?.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeOptionalchest ? config : null;
        return config is null
            ? PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg
            : chest is null
                ? PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUseTypeInvalid
                : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static PB_ENUM_RES_ERROR_CODE ValidateOptionalChestChoices(
        pbOutSideItem_Base chest,
        IReadOnlyDictionary<int, pbDrop_Static> options,
        int resCnt,
        IReadOnlyList<pbCOMDT_ONE_SELECT_RESOURCE_INFO> chooseResList)
    {
        if (chooseResList.Count == 0 || (IsSingleSelect(chest) && chooseResList.Count != SingleItem))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemOptionalChestSelectResInvalid;
        }

        if (chooseResList.Select(choice => choice.ResID).Distinct().Count() != chooseResList.Count)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemSelectSameRes;
        }

        if (!chooseResList.All(choice => options.ContainsKey(choice.ResID)))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemOptionalChestSelectResInvalid;
        }

        return chooseResList.Any(choice => choice.ResCnt <= 0) || chooseResList.Sum(choice => (long)choice.ResCnt) != resCnt
            ? PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemOptChestCntErr
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static bool IsSingleSelect(pbOutSideItem_Base chest) =>
        chest.ItemParam.ElementAtOrDefault(OptionalChestSingleSelectParamIndex) == Enabled;

    private pbDrop_Static[] FindOptionalChestOptions(pbOutSideItem_Base chest) =>
        FindStaticDrops(chest.ItemParam.ElementAtOrDefault(OptionalChestDropParamIndex));

    private pbDrop_Static? FindOptionalChestExtraOption(pbOutSideItem_Base chest) =>
        IsSingleSelect(chest)
            ? FindStaticDrops(chest.ItemParam.ElementAtOrDefault(OptionalChestExtraDropParamIndex))
                .FirstOrDefault(drop => drop.SDropSeq == FirstDropSeq)
            : null;

    private pbDrop_Static[] FindStaticDrops(int sDropID)
    {
        var drops = new List<pbDrop_Static>();
        for (var dropSeq = FirstDropSeq;
             sDropID != NoDropID && gameData.DropStatic.Find(pbDrop_Static.Key(sDropID, dropSeq)) is { } drop;
             dropSeq++)
        {
            drops.Add(drop);
        }

        return [.. drops.Where(drop => drop.ResNum > 0)];
    }

    private bool IsNonRepeatableOwned(LMNRoleInfo role, int resID) =>
        CosmeticGrant.FindItemType(gameData, resID) is { } cosmeticType
            ? role.OwnsItem(cosmeticType, resID)
            : CoinID.GetResIDType(resID) == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem
                && UniteLicense.FindPokemonId(gameData, resID) is { } pokemonId
                && role.OwnsPokemon(pokemonId);

    private async Task<bool> IsOwnedAsync(LMNRoleInfo role, int resID) =>
        IsNonRepeatableOwned(role, resID)
        || (CoinID.GetResIDType(resID) == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem
            && !UniteLicense.IsUniteLicense(gameData, resID)
            && await FindOwnedOutSideItemAsync(role.GameUlongUID, resID) is not null)
        || (gameData.BadgeBase.Contains(resID)
            && await repository.FindAsync(role.GameUlongUID, RoleItemType.Badge, resID) is { IsOwned: true });

    private ResourceGrantPlan? PlanResourceGrants(LMNRoleInfo role, IReadOnlyDictionary<int, long> resCnts)
    {
        var coinGrants = new Dictionary<int, long>();
        var grants = new List<BagGrant>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        var pokemonIds = new List<int>();
        var items = new List<RoleItemInfo>();
        var gainTime = clock.GetUtcNow().ToUnixTimeSeconds();
        foreach (var (resID, totalCnt) in resCnts)
        {
            if (totalCnt is <= 0 or > int.MaxValue)
            {
                return null;
            }

            var resCnt = (int)totalCnt;
            if (CoinID.IsGrantableCoin(resID))
            {
                coinGrants[resID] = coinGrants.GetValueOrDefault(resID) + resCnt;
                awards.Add(BuildCoinAward(resID, resCnt));
            }
            else if (UniteLicense.IsUniteLicense(gameData, resID))
            {
                if (UniteLicense.PlanGrant(gameData, role, resID, resCnt, pokemonIds) is not { } license)
                {
                    return null;
                }

                license.AddTo(pokemonIds, coinGrants, awards);
            }
            else if (CosmeticGrant.FindItemType(gameData, resID) is not null)
            {
                if (CosmeticGrant.Plan(gameData, this, role, resID, resCnt, gainTime, items) is not { } cosmetic)
                {
                    return null;
                }

                cosmetic.AddTo(items, grants, coinGrants, awards);
            }
            else if (CreateOutSideItemGrant(resID, resCnt) is { } grant)
            {
                grants.Add(grant);
                awards.Add(BuildAward(resID, resCnt, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
            }
            else if (CreateBadgeGrant(resID, resCnt) is { } badgeGrant)
            {
                grants.Add(badgeGrant);
                awards.Add(BuildAward(resID, resCnt, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesBadge));
            }
            else
            {
                return null;
            }
        }

        return new ResourceGrantPlan(grants, coinGrants, awards, pokemonIds, items);
    }

    private async Task<BagOutcome> CommitGrantsAsync(LMNRoleInfo role, BagCost cost, ResourceGrantPlan plan, int reason)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var change = new BagChange
        {
            Costs = [cost],
            Grants = plan.Grants,
            CoinGrants = plan.CoinGrants,
            PokemonGrants = [.. plan.PokemonIds.Select(pokemonId => new RolePokemonInfo(pokemonId, now, reason, RolePokemonInfo.HasNewTag))],
            ItemGrants = plan.Items,
        };
        var outcome = BagOutcome.From(await repository.CommitAsync(role.GameUlongUID, change, now));
        if (!outcome.Succeeded)
        {
            return outcome;
        }

        foreach (var pokemonId in plan.PokemonIds)
        {
            role.GrantPokemon(pokemonId, now, reason);
        }

        foreach (var item in plan.Items)
        {
            role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
        }

        return outcome with { Awards = plan.Awards, GrantedPokemonIds = plan.PokemonIds, GrantedItems = plan.Items, AwardReason = reason };
    }

    private sealed record ResourceGrantPlan(
        IReadOnlyList<BagGrant> Grants,
        IReadOnlyDictionary<int, long> CoinGrants,
        IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards,
        IReadOnlyList<int> PokemonIds,
        IReadOnlyList<RoleItemInfo> Items);

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
            && (CoinID.IsGrantableCoin(entry.ResID)
                || gameData.OutSideItemBase.Contains(entry.ResID)
                || gameData.BadgeBase.Contains(entry.ResID)
                || CosmeticGrant.FindItemType(gameData, entry.ResID) is not null));

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

    private async Task<(PB_ENUM_RES_ERROR_CODE Result, pbOutSideItem_Base? Config, BagItem? Owned)> ValidateGridAsync(
        ulong gameUlongUID,
        int resID,
        ulong gridUniqueID,
        int resCnt)
    {
        if (resCnt <= 0)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum, null, null);
        }

        var grid = await repository.FindAsync(gameUlongUID, RoleItemType.OutSideItem, resID);
        if (!MatchesGrid(grid, gridUniqueID))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemResIduniqueIdnotMatch, null, null);
        }

        return gameData.OutSideItemBase.Find(resID) is { } config
            ? (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, config, grid is { IsOwned: true } ? grid : null)
            : (PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg, null, null);
    }

    private static bool MatchesGrid(BagItem? grid, ulong gridUniqueID) =>
        gridUniqueID == GridUniqueID.None || grid?.GridUniqueID == gridUniqueID;
}
