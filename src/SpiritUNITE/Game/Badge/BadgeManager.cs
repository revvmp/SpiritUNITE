using System.Text;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Badge;

public sealed record BadgeUpgradeOutcome(
    PB_ENUM_RES_ERROR_CODE Result,
    int NeedConfirm = 0,
    int AfterBadgeId = 0,
    int RandSucc = 0,
    IReadOnlyList<BagItem>? ChangedBadges = null,
    RoleWallet? Wallet = null,
    BadgeSchedue? ChangedCurSchedue = null);

public sealed class BadgeManager(
    GameData gameData,
    BagRepository bagRepository,
    BadgeRepository repository,
    WalletManager wallets,
    TimeProvider clock)
{
    public const int MaxSlotNum = 10;
    public const int NeedConfirm = 1;
    public const int Confirmed = 1;
    public const int ShowNewTag = 1;

    private const int MaxSchedueNameBytes = 64;
    private const int MaxUpgradeNum = 99;
    private const int ProbabilityScale = 10000;
    private const int BitsPerPictureWord = 32;
    private const int Enabled = 1;
    private const int NoBadgeColor = 0;
    private const int NoBadgeColorLv = 0;
    private const int UnlimitedBadgeStack = int.MaxValue;
    private const int NoItemLevel = 0;

    private readonly int firstPaidSchedueId = gameData.BadgeUnlock.Rows.Min(unlock => unlock.BadgeSchemeID);
    private readonly int schedueCount = gameData.BadgeUnlock.Rows.Max(unlock => unlock.BadgeSchemeID) + 1;

    public async Task<BagItem[]> GetOwnedBadgesAsync(ulong gameUlongUID) =>
        [.. (await bagRepository.LoadAsync(gameUlongUID, RoleItemType.Badge)).Where(badge => badge.IsOwned)];

    public async Task<pbSCGetBadgeAllInfoRsp> BuildGetBadgeAllInfoRspAsync(ulong gameUlongUID) => new()
    {
        ResultInfo = new pbResultInfo(),
        BadgeGridInfoList = { (await GetOwnedBadgesAsync(gameUlongUID)).Select(badge => badge.ToBadgeGridInfo()) },
    };

    public static pbSCUpdateBadgeNotify BuildUpdateBadgeNotify(IEnumerable<BagItem> changedBadges) => new()
    {
        ResultInfo = new pbResultInfo(),
        UpdateBadgeGridInfoList = { changedBadges.Select(badge => badge.ToBadgeGridInfo()) },
    };

    public async Task<pbSCGetBadgePictureRsp> BuildGetBadgePictureRspAsync(ulong gameUlongUID)
    {
        var everOwned = await bagRepository.LoadAsync(gameUlongUID, RoleItemType.Badge);
        return new pbSCGetBadgePictureRsp
        {
            ResultInfo = new pbResultInfo(),
            BadgePictureInfo = BuildPictureBitmap(everOwned),
            NewBadgePictureInfo = BuildPictureBitmap(everOwned.Where(badge => badge.NewTag != 0)),
        };
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ClearBadgeNewTagAsync(ulong gameUlongUID, int badgeID)
    {
        if (!gameData.BadgeBase.Contains(badgeID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        return await bagRepository.ClearNewTagAsync(gameUlongUID, RoleItemType.Badge, badgeID) is null
            ? PB_ENUM_RES_ERROR_CODE.PbErrBadgeInlayNotEnoughInBag
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<IReadOnlyList<BadgeSchedue>> GetSchedulesAsync(ulong gameUlongUID)
    {
        var saved = (await repository.LoadSchedulesAsync(gameUlongUID)).ToDictionary(schedue => schedue.BadgeSchedueId);
        return [.. Enumerable.Range(0, schedueCount).Select(id => saved.GetValueOrDefault(id) ?? BadgeSchedue.Empty(id))];
    }

    public async Task<BadgeSchedue> GetCurSchedueAsync(ulong gameUlongUID)
    {
        var schedules = await GetSchedulesAsync(gameUlongUID);
        var curId = await repository.FindCurBadgeSchedueIdAsync(gameUlongUID) ?? 0;
        return schedules.FirstOrDefault(schedue => schedue.BadgeSchedueId == curId && IsUnlocked(schedue)) ?? schedules[0];
    }

    public bool IsUnlocked(BadgeSchedue schedue) =>
        schedue.BadgeSchedueId < firstPaidSchedueId || schedue.UnlockTimeStamp != BadgeSchedue.Locked;

    public pbCOMDT_BADGE_SCHEDUE BuildSchedueInfo(BadgeSchedue schedue) => new()
    {
        SchedueName = schedue.SchedueName,
        SchedueData = schedue.BuildSchedueData(),
        ActiveBadgeColorList = BuildActiveBadgeColorList(schedue.BadgeIDList),
        UnlockTimeStamp = schedue.UnlockTimeStamp,
    };

    public pbCSDT_BADGE_SCHEDUE_INFO BuildBadgePlate(BadgeSchedue schedue) => new()
    {
        BadgeSchedueId = schedue.BadgeSchedueId,
        SchedueInfo = BuildSchedueInfo(schedue),
    };

    public async Task<pbSCQueryBadgeSchedueListRsp> BuildQueryBadgeSchedueListRspAsync(ulong gameUlongUID)
    {
        var cur = await GetCurSchedueAsync(gameUlongUID);
        return new pbSCQueryBadgeSchedueListRsp
        {
            ResultInfo = new pbResultInfo(),
            BadgeSchedueList = { (await GetSchedulesAsync(gameUlongUID)).Select(BuildBadgePlate) },
            CurBadgeSchedueId = cur.BadgeSchedueId,
        };
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, BadgeSchedue? Schedue)> SelectCurSchedueAsync(
        ulong gameUlongUID,
        int badgeSchedueId)
    {
        var schedue = await FindUnlockedSchedueAsync(gameUlongUID, badgeSchedueId);
        if (schedue.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return schedue;
        }

        await repository.SetCurBadgeSchedueIdAsync(gameUlongUID, badgeSchedueId);
        return schedue;
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, BadgeSchedue? Schedue)> FindUnlockedSchedueAsync(
        ulong gameUlongUID,
        int badgeSchedueId)
    {
        if (badgeSchedueId < 0 || badgeSchedueId >= schedueCount)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, null);
        }

        var schedue = (await GetSchedulesAsync(gameUlongUID))[badgeSchedueId];
        return IsUnlocked(schedue)
            ? (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, schedue)
            : (PB_ENUM_RES_ERROR_CODE.PbErrBadgeSchedueNotUnlocked, schedue);
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ModifySchedueNameAsync(ulong gameUlongUID, int badgeSchedueId, string schedueName)
    {
        var nameResult = ValidateSchedueName(schedueName);
        if (nameResult != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return nameResult;
        }

        var (result, schedue) = await FindUnlockedSchedueAsync(gameUlongUID, badgeSchedueId);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || schedue is null)
        {
            return result;
        }

        await repository.SaveSchedueAsync(gameUlongUID, schedue with { SchedueName = schedueName });
        return result;
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, BadgeSchedue Schedue)> InlayBadgeAsync(
        ulong gameUlongUID,
        bool isOverRide,
        IReadOnlyList<int> badgeIDList)
    {
        var cur = await GetCurSchedueAsync(gameUlongUID);
        IReadOnlyList<int> cells = isOverRide ? badgeIDList : [.. cur.BadgeIDList, .. badgeIDList];
        if (cells.Count > MaxSlotNum)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrBadgeInlayPlateFull, cur);
        }

        var groups = new HashSet<int>();
        foreach (var badgeID in cells)
        {
            if (gameData.BadgeBase.Find(badgeID) is not { } badge || !groups.Add(badge.BadgeGroupId))
            {
                return (PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, cur);
            }
        }

        var owned = (await GetOwnedBadgesAsync(gameUlongUID)).Select(badge => badge.ItemId).ToHashSet();
        if (!cells.All(owned.Contains))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrBadgeInlayNotEnoughInBag, cur);
        }

        var inlaid = cur with { BadgeIDList = [.. cells] };
        await repository.SaveSchedueAsync(gameUlongUID, inlaid);
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, inlaid);
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, BadgeSchedue Schedue)> UnloadBadgeAsync(
        ulong gameUlongUID,
        IReadOnlyList<int> badgeIDList)
    {
        var cur = await GetCurSchedueAsync(gameUlongUID);
        if (badgeIDList.Count == 0 || !badgeIDList.All(cur.BadgeIDList.Contains))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, cur);
        }

        var unloaded = cur with { BadgeIDList = [.. cur.BadgeIDList.Except(badgeIDList)] };
        await repository.SaveSchedueAsync(gameUlongUID, unloaded);
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, unloaded);
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, long Cost, RoleWallet? Wallet)> UnlockSchedueAsync(
        ulong gameUlongUID,
        int badgeSchedueId,
        PB_ENUM_RES_COIN_TYPE coinType)
    {
        if (gameData.BadgeUnlock.Find(badgeSchedueId) is not { } unlock)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, 0, null);
        }

        var schedules = await GetSchedulesAsync(gameUlongUID);
        var schedue = schedules[badgeSchedueId];
        if (IsUnlocked(schedue))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrBadgeSchedueAlreadyUnlocked, 0, null);
        }

        if (badgeSchedueId > firstPaidSchedueId && !IsUnlocked(schedules[badgeSchedueId - 1]))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrUnlockNotSequenceSchedue, 0, null);
        }

        var price = coinType switch
        {
            PB_ENUM_RES_COIN_TYPE.PbResCoinTypeCookies when unlock.IsCookiesBuy == Enabled => unlock.CookiesBuy,
            PB_ENUM_RES_COIN_TYPE.PbResCoinTypePokecoins when unlock.IsPokeCoinsBuy == Enabled => unlock.PokeCoinsBuy,
            PB_ENUM_RES_COIN_TYPE.PbResCoinTypeCoupons when unlock.IsCouponsBuy == Enabled => unlock.CouponsBuy,
            _ => (int?)null,
        };
        if (price is not { } cost || CoinID.GetCoinID(coinType) is not { } coinID)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrShopCoinTypeUnexists, 0, null);
        }

        var wallet = await wallets.LoadAsync(gameUlongUID);
        if (wallet.GetCoinVal(coinID) < cost)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrUnlockMoneyNotEnough, cost, null);
        }

        var unlocked = schedue with { UnlockTimeStamp = clock.GetUtcNow().ToUnixTimeSeconds() };
        var (result, walletAfter) = await repository.UnlockSchedueAsync(gameUlongUID, unlocked, coinID, cost);
        return (result, cost, walletAfter);
    }

    public async Task<BadgeUpgradeOutcome> UpgradeBadgeAsync(ulong gameUlongUID, int badgeId, int upgradeNum, int force)
    {
        if (upgradeNum is < 1 or > MaxUpgradeNum
            || gameData.BadgeBase.Find(badgeId) is not { } badge
            || badge.BadgeQuality < 1
            || badge.BadgeQuality > badge.UpgradeCost.Count
            || FindBadge(badge.BadgeGroupId, badge.BadgeQuality + 1) is not { } upgraded)
        {
            return new BadgeUpgradeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        var cost = badge.UpgradeCost[badge.BadgeQuality - 1];
        var badgeCost = cost.BadgeNum * upgradeNum;
        var goldCost = (long)cost.Gold * upgradeNum;
        var owned = await bagRepository.FindAsync(gameUlongUID, RoleItemType.Badge, badgeId);
        if (owned is null || owned.Cnt < badgeCost || badgeCost <= 0)
        {
            return new BadgeUpgradeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrBadgeUpgradeMaterialNotEnough);
        }

        var wallet = await wallets.LoadAsync(gameUlongUID);
        if (wallet.GetCoinVal(CoinID.PokeCoins) < goldCost)
        {
            return new BadgeUpgradeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrBadgeUpgradeNotEnoughCoins);
        }

        var schedules = await GetSchedulesAsync(gameUlongUID);
        var leavesNone = owned.Cnt == badgeCost;
        if (force != Confirmed && leavesNone && schedules.Any(schedue => schedue.BadgeIDList.Contains(badgeId)))
        {
            return new BadgeUpgradeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, NeedConfirm, upgraded.BadgeId);
        }

        var successes = Enumerable.Range(0, upgradeNum).Count(_ => Random.Shared.Next(ProbabilityScale) < cost.Probability);
        var change = new BagChange
        {
            Costs = [new BagCost(RoleItemType.Badge, badgeId, badgeCost)],
            Grants = successes > 0
                ? [new BagGrant(RoleItemType.Badge, upgraded.BadgeId, successes, UnlimitedBadgeStack, NoItemLevel, ShowNewTag)]
                : [],
            CoinCosts = goldCost > 0 ? new Dictionary<int, long> { [CoinID.PokeCoins] = goldCost } : new Dictionary<int, long>(),
        };
        var commit = await bagRepository.CommitAsync(gameUlongUID, change, clock.GetUtcNow().ToUnixTimeSeconds());
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return new BadgeUpgradeOutcome(commit.Result);
        }

        var changedCur = leavesNone ? await RemoveFromSchedulesAsync(gameUlongUID, schedules, badgeId) : null;
        return new BadgeUpgradeOutcome(
            commit.Result,
            0,
            upgraded.BadgeId,
            successes > 0 ? 1 : 0,
            commit.ChangedItems,
            commit.Wallet ?? wallet,
            changedCur);
    }

    public PB_ENUM_RES_ERROR_CODE ValidateResolveBadge(int badgeID, int resolveNum)
    {
        if (resolveNum < 1 || gameData.BadgeBase.Find(badgeID) is not { } badge)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        return badge.TransmuteId == 0 || badge.TransmuteNum <= 0
            ? PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemTransmuteNotCfg
            : PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUnKnownUseType;
    }

    private async Task<BadgeSchedue?> RemoveFromSchedulesAsync(
        ulong gameUlongUID,
        IReadOnlyList<BadgeSchedue> schedules,
        int badgeId)
    {
        var changed = schedules.Where(schedue => schedue.BadgeIDList.Contains(badgeId))
            .Select(schedue => schedue with { BadgeIDList = [.. schedue.BadgeIDList.Where(id => id != badgeId)] })
            .ToArray();
        await repository.SaveSchedulesAsync(gameUlongUID, changed);
        var curId = (await GetCurSchedueAsync(gameUlongUID)).BadgeSchedueId;
        return changed.FirstOrDefault(schedue => schedue.BadgeSchedueId == curId);
    }

    private pbBadge_Base? FindBadge(int badgeGroupId, int badgeQuality) =>
        gameData.BadgeBase.Rows.FirstOrDefault(row => row.BadgeGroupId == badgeGroupId && row.BadgeQuality == badgeQuality);

    private pbCOMDT_BADGE_COLOR_LIST BuildActiveBadgeColorList(IEnumerable<int> badgeIDs)
    {
        var counts = badgeIDs.Select(badgeID => gameData.BadgeBase.Find(badgeID))
            .OfType<pbBadge_Base>()
            .SelectMany(badge => badge.BadgeColors.Where(color => color != NoBadgeColor))
            .GroupBy(color => color)
            .OrderBy(group => group.Key);
        return new pbCOMDT_BADGE_COLOR_LIST
        {
            BadgeColorNodes =
            {
                counts.Select(group => new pbCOMDT_BADGE_COLOR_NODE
                {
                    BadgeColor = group.Key,
                    BadgeNum = group.Count(),
                    BadgeLv = gameData.BadgeColor.Rows
                        .Where(level => level.BadgeColor == group.Key && level.LvNeedCount <= group.Count())
                        .Select(level => level.BadgeColorLv)
                        .DefaultIfEmpty(NoBadgeColorLv)
                        .Max(),
                }),
            },
        };
    }

    private pbCOMDT_BADGE_PICTURE_INFO BuildPictureBitmap(IEnumerable<BagItem> badges)
    {
        var pictureIds = badges.Select(badge => gameData.BadgeBase.Find(badge.ItemId)?.BadgePictureId ?? -1)
            .Where(pictureId => pictureId >= 0)
            .ToArray();
        var words = new int[pictureIds.Length == 0 ? 0 : pictureIds.Max() / BitsPerPictureWord + 1];
        foreach (var pictureId in pictureIds)
        {
            words[pictureId / BitsPerPictureWord] |= 1 << (pictureId % BitsPerPictureWord);
        }

        return new pbCOMDT_BADGE_PICTURE_INFO { BadgePictureList = { words } };
    }

    private static PB_ENUM_RES_ERROR_CODE ValidateSchedueName(string schedueName)
    {
        if (string.IsNullOrWhiteSpace(schedueName) || schedueName.Any(char.IsControl) || schedueName.Any(c => c is '<' or '>'))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrBadgeSchedueNameIlleagel;
        }

        return Encoding.UTF8.GetByteCount(schedueName) > MaxSchedueNameBytes
            ? PB_ENUM_RES_ERROR_CODE.PbErrBadgeSchedueNameOutofLength
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }
}
