using System.Collections.Concurrent;
using System.Collections.Frozen;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Recharge;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.PveChallenge;

public sealed record PveChallengeOutcome(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<GamePacket> BeforeRsp)
{
    public static PveChallengeOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed record PveChallengeShopOutcome(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<GamePacket> BeforeRsp, PveChallengeShopState? State);

public sealed record PvePrivilegeOutcome(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<GamePacket> BeforeRsp, long PrivilegeExpireTime);

public sealed record PveChallengeWeekReward(
    PveChallengeWeek Week,
    int WeeklySettleLimit,
    bool IsPrivilegeActive,
    int WeekExtraRewardNum)
{
    public bool CanGetRefreshReward => Week.WeekRewardGetNum < WeeklySettleLimit;

    public bool CanGetPrivilegeReward => IsPrivilegeActive && Week.PrivilegeRewardGetNum < WeekExtraRewardNum;
}

public sealed record PveChallengeSettledNode(PveChallengeNodeKey Node, bool CanUseSettleBonusCard, IReadOnlyList<int> RefreshDrops);

public sealed class PveChallengeManager(
    GameData gameData,
    ActivitySchedule schedule,
    LobbyNotices notices,
    PveChallengeRepository repository,
    BagRepository bagRepository,
    BagManager bag,
    TimeProvider clock)
{
    public const int CurrentShopPeriodID = 1;

    private const int IsBuffShopItem = 1;
    private const int NoJumpLink = 0;
    private const int NoAttrID = 0;
    private const int NoPvePrivilege = 0;
    private const int PveBuffItemType = (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePveBuffLevel;
    private const int PveBuffItemBuffIDParamIndex = 0;
    private const int PveBuffItemLevelParamIndex = 2;
    private const int MinShopCnt = 1;
    private const int NoDrop = 0;
    private const int DaysPerWeek = 7;

    private readonly ConcurrentDictionary<ulong, PveChallengeSettledNode> settledNodes = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> roleGates = new();

    private readonly FrozenDictionary<int, int> buffGroupIdByBuffID = gameData.PveChallengeShop.Rows
        .Where(shopItem => shopItem.IsBuff == IsBuffShopItem && shopItem.PeriodID == CurrentShopPeriodID)
        .GroupBy(shopItem => shopItem.ResID)
        .ToFrozenDictionary(group => group.Key, group => group.First().GroupId);

    private readonly FrozenDictionary<(int GroupId, int Level), int> buffLevelUpDesignIds = gameData.PveBuffLevelUpDesign.Rows
        .GroupBy(design => (design.GroupId, design.Level))
        .ToFrozenDictionary(group => group.Key, group => group.First().Id);

    private readonly FrozenSet<int> privilegeBuffIDs = gameData.ActivityExchange.Rows
        .Where(exchange => exchange.NeedPvePrivilege != NoPvePrivilege)
        .Select(exchange => FindBuffOfItem(gameData, exchange.ToResID)?.BuffID)
        .OfType<int>()
        .ToFrozenSet();

    public int ScoreItemID { get; } = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalPvechallengeScoreItemid);

    public int ExtraAwardItemID { get; } = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalPveExtraAwardItemid);

    private int WeeklySettleCookie { get; } = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalPvechallengeWeeklySettleCookie);

    public static long WeekStartTime(DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        return today.AddDays(-(((int)today.DayOfWeek + DaysPerWeek - (int)DayOfWeek.Monday) % DaysPerWeek)).ToUnixTimeSeconds();
    }

    public async Task<PveChallengeWeek> LoadWeekAsync(ulong gameUlongUID, DateTimeOffset now)
    {
        var weekStartTime = WeekStartTime(now);
        return await repository.LoadWeekAsync(gameUlongUID) is { } week && week.WeekStartTime == weekStartTime
            ? week
            : new PveChallengeWeek(weekStartTime, 0, 0);
    }

    public async Task<PveChallengeWeekReward> LoadWeekRewardAsync(ulong gameUlongUID, DateTimeOffset now)
    {
        var week = await LoadWeekAsync(gameUlongUID, now);
        var privilege = FindPrivilegeActivity(now);
        var isPrivilegeActive = privilege is not null
            && await repository.LoadPrivilegeExpireTimeAsync(gameUlongUID, privilege.ActivityID) > now.ToUnixTimeSeconds();
        var extraLimit = isPrivilegeActive ? Math.Max(privilege!.WeekRewardLimit, 0) : 0;
        return new PveChallengeWeekReward(
            week,
            Math.Max(WeeklySettleCookie + extraLimit, 0),
            isPrivilegeActive,
            isPrivilegeActive ? Math.Max(privilege!.WeekExtraRewardNum, 0) : 0);
    }

    public pbPvePrivilegeActivity? FindPrivilegeActivity(DateTimeOffset now) =>
        schedule.FindActive(now)
            .Where(activity => activity.Row.ActivityType == (int)PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypePvePrivilege)
            .Select(activity => gameData.PvePrivilegeActivity.Find(activity.Row.ActivityID))
            .OfType<pbPvePrivilegeActivity>()
            .FirstOrDefault();

    public async Task<pbCOMDT_PVE_PRIVILEGE_ACTIVITY_INFO> BuildPrivilegeInfoAsync(ulong gameUlongUID, int activityID) => new()
    {
        PrivilegeExpireTime = await repository.LoadPrivilegeExpireTimeAsync(gameUlongUID, activityID),
    };

    public async Task<pbCOMDT_PVE_PRIVILEGE_ACTIVITY_INFO> BuildVisiblePrivilegeInfoAsync(ulong gameUlongUID, DateTimeOffset now) =>
        FindPrivilegeActivity(now) is { } privilege
            ? await BuildPrivilegeInfoAsync(gameUlongUID, privilege.ActivityID)
            : new pbCOMDT_PVE_PRIVILEGE_ACTIVITY_INFO();

    public Task<PveChallengeShopState> LoadShopStateAsync(ulong gameUlongUID) => repository.LoadShopStateAsync(gameUlongUID);

    public pbCOMDT_PVE_CHALLENGE_BUFF_INFO BuildBuffInfo(PveChallengeShopState state) => new()
    {
        BuffList =
        {
            state.Buffs.Select(buff => new pbCOMDT_PVE_CHALLENGE_BUFF_ONE_INFO
            {
                BuffID = buff.BuffID,
                BuffLevel = buff.BuffLevel,
                IsNewUnLock = buff.IsNewUnLock,
                AttrID = AttrIDOf(buff),
            }),
        },
    };

    public async Task<pbCOMDT_PVE_CHALLENGE_BUFF_INFO> BuildBattleBuffInfoAsync(ulong gameUlongUID)
    {
        var state = await repository.LoadShopStateAsync(gameUlongUID);
        var isPrivilegeActive = await IsPrivilegeActiveAsync(gameUlongUID, clock.GetUtcNow());
        return new pbCOMDT_PVE_CHALLENGE_BUFF_INFO
        {
            BuffList =
            {
                state.Buffs
                    .Where(buff => isPrivilegeActive || !privilegeBuffIDs.Contains(buff.BuffID))
                    .Select(buff => new pbCOMDT_PVE_CHALLENGE_BUFF_ONE_INFO
                    {
                        BuffID = buff.BuffID,
                        BuffLevel = buff.BuffLevel,
                        AttrID = AttrIDOf(buff),
                    }),
            },
        };
    }

    public async Task<bool> IsPrivilegeActiveAsync(ulong gameUlongUID, DateTimeOffset now) =>
        FindPrivilegeActivity(now) is { } privilege
        && await repository.LoadPrivilegeExpireTimeAsync(gameUlongUID, privilege.ActivityID) > now.ToUnixTimeSeconds();

    public PveChallengeBuff? FindBuffOfItem(int outSideItemId) => FindBuffOfItem(gameData, outSideItemId);

    public static GamePacket BuildBuffShopNtf(pbCOMDT_PVE_CHALLENGE_BUFF_INFO buffInfo, PveChallengeShopState state) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeBuffShopNtf, new pbSCPveChallengeBuffShopNtf
        {
            PveChallengeBuffInfo = buffInfo,
            PveChallengeShopInfo = BuildShopInfo(state),
        });

    public static pbCOMDT_PVE_CHALLENGE_SHOP_INFO BuildShopInfo(PveChallengeShopState state) => new()
    {
        ShopList = { state.ShopBuys.Select(buy => new pbCOMDT_PVE_CHALLENGE_SHOP_ONE_INFO { ShopID = buy.ShopID, ShopBuyNum = buy.ShopBuyNum }) },
    };

    public async Task<PveChallengeShopOutcome> BuyAsync(LMNRoleInfo role, int shopID, int shopCnt)
    {
        var shopItem = gameData.PveChallengeShop.Find(shopID);
        if (shopItem is null || shopItem.PeriodID != CurrentShopPeriodID || shopItem.JumpLink != NoJumpLink)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrPveChallengeShopIdInvaild, [], null);
        }

        if (shopCnt < MinShopCnt || shopCnt > shopItem.BuyNumLimit)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrPveChallengeShopCntInvaild, [], null);
        }

        if (!IsBuffSystemUnlocked(role))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrPveChallengeBuffSystemLock, [], null);
        }

        var purchase = new PveChallengeShopPurchase(
            shopItem.ShopID,
            shopCnt,
            shopItem.BuyNumLimit,
            shopItem.IsBuff == IsBuffShopItem ? new PveChallengeBuff(shopItem.ResID, shopItem.Level, 1) : null,
            ScoreItemID,
            (long)shopItem.Score * shopCnt);
        var commit = await WithRoleGateAsync(role.GameUlongUID, () => repository.CommitShopBuyAsync(role.GameUlongUID, purchase));
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || commit.Wallet is not { } wallet)
        {
            return new(commit.Result, [], null);
        }

        return new(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            [BuildCoinChange(ScoreItemID, commit.BeforeCoinNum, wallet)],
            commit.State);
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ClearBuffNewTagAsync(LMNRoleInfo role, int buffID) =>
        await repository.ClearBuffNewTagAsync(role.GameUlongUID, buffID)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrPveChallengeBuffLevelInvaild;

    public async Task<PvePrivilegeOutcome> ActivatePrivilegeAsync(LMNRoleInfo role, int activityID)
    {
        var now = clock.GetUtcNow();
        if (FindPrivilegeActivity(now) is not { } privilege || privilege.ActivityID != activityID)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, [], 0);
        }

        var expireTime = now.ToUnixTimeSeconds() + privilege.PrivilegeKeepTime;
        var couponsFlow = CouponsFlow.Spend(privilege.CostCoupons, now, CouponsFlow.ActivitySystemType, new pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION
        {
            ActivityInfo = new pbCOMDT_COUPONS_FLOW_ACTIVITY_TYPE
            {
                ActivityId = activityID,
                ActivityType = (int)PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypePvePrivilege,
            },
        });
        var commit = await WithRoleGateAsync(role.GameUlongUID, () => repository.CommitActivatePrivilegeAsync(
            role.GameUlongUID,
            new PvePrivilegeActivation(activityID, privilege.CostCoupons, expireTime, now.ToUnixTimeSeconds(), couponsFlow)));
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || commit.Wallet is not { } wallet)
        {
            return new(commit.Result, [], 0);
        }

        return new(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp())],
            expireTime);
    }

    public void RecordSettledNode(ulong gameUlongUID, PveChallengeSettledNode settled) => settledNodes[gameUlongUID] = settled;

    public async Task<PveChallengeOutcome> UseSettleBonusCardAsync(LMNRoleInfo role, PveChallengeNodeKey node)
    {
        var now = clock.GetUtcNow();
        if (!(await LoadWeekRewardAsync(role.GameUlongUID, now)).IsPrivilegeActive)
        {
            return PveChallengeOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrNoActivePvePrivilege);
        }

        if (!settledNodes.TryGetValue(role.GameUlongUID, out var settled)
            || settled.Node != node
            || !settled.CanUseSettleBonusCard
            || !settledNodes.TryUpdate(role.GameUlongUID, settled with { CanUseSettleBonusCard = false }, settled))
        {
            return PveChallengeOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrUsePveSettleBonusCardFail);
        }

        var drops = settled.RefreshDrops.Where(dropId => dropId != NoDrop).SelectMany(schedule.FindDrops).ToArray();
        var coinGrants = new Dictionary<int, long>();
        var grants = new List<BagGrant>();
        foreach (var drop in drops)
        {
            if (CoinID.IsGrantableCoin(drop.ResID))
            {
                coinGrants[drop.ResID] = coinGrants.GetValueOrDefault(drop.ResID) + drop.ResNum;
            }
            else if (bag.CreateOutSideItemGrant(drop.ResID, drop.ResNum) is { } grant)
            {
                grants.Add(grant);
            }
        }

        var commit = await WithRoleGateAsync(role.GameUlongUID, () => bagRepository.CommitAsync(role.GameUlongUID, new BagChange
        {
            Costs = [new BagCost(RoleItemType.OutSideItem, ExtraAwardItemID, 1)],
            Grants = grants,
            CoinGrants = coinGrants,
        }, now.ToUnixTimeSeconds()));
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            settledNodes.TryUpdate(role.GameUlongUID, settled, settled with { CanUseSettleBonusCard = false });
            return PveChallengeOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrUsePveSettleBonusCardFail);
        }

        var packets = new List<GamePacket>
        {
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(commit.ChangedItems)),
        };
        if (commit.Wallet is { } wallet)
        {
            if (coinGrants.Keys.Any(CoinID.WalletCoins.Contains))
            {
                packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() }));
            }

            if (coinGrants.ContainsKey(CoinID.Coupons))
            {
                packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp()));
            }

            packets.AddRange(coinGrants.Keys.Where(CoinID.IsSpecialCoin)
                .Select(coinID => BuildCoinChange(coinID, commit.BeforeCoinNums.GetValueOrDefault(coinID), wallet)));
        }

        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonPveSettleBonusCard,
            ResourcesList =
            {
                drops.Select(drop => BagManager.BuildAward(drop.ResID, drop.ResNum, CoinID.IsGrantableCoin(drop.ResID)
                    ? CoinID.GetCoinResourceType(drop.ResID)
                    : PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem)),
            },
        }));
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, packets);
    }

    private static PveChallengeBuff? FindBuffOfItem(GameData gameData, int outSideItemId) =>
        gameData.OutSideItemBase.Find(outSideItemId) is { OutSideItemType: PveBuffItemType } item
        && item.OutSideItemTypeParam.Count > PveBuffItemLevelParamIndex
            ? new PveChallengeBuff(item.OutSideItemTypeParam[PveBuffItemBuffIDParamIndex], item.OutSideItemTypeParam[PveBuffItemLevelParamIndex], 1)
            : null;

    private int AttrIDOf(PveChallengeBuff buff) =>
        buffGroupIdByBuffID.TryGetValue(buff.BuffID, out var groupId)
            ? buffLevelUpDesignIds.GetValueOrDefault((groupId, buff.BuffLevel), NoAttrID)
            : NoAttrID;

    private bool IsBuffSystemUnlocked(LMNRoleInfo role) =>
        gameData.SystemUnlock.Find(pbSystemUnlock.Key(
            (int)PB_ENUM_RES_SYSTEM_UNLOCK_TYPE.PbResSystemUnlockTypePveBuff,
            notices.GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeSystemUnlock))) is not { } unlock
        || role.RoleTotalExp >= unlock.TrainerExp;

    private static GamePacket BuildCoinChange(int coinID, long beforeCoinNum, RoleWallet wallet) =>
        GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
            SpecialCoin.BuildSpecialCoinChangeNtf(coinID, beforeCoinNum, wallet.GetCoinVal(coinID)));

    private async Task<T> WithRoleGateAsync<T>(ulong gameUlongUID, Func<Task<T>> action)
    {
        var gate = roleGates.GetOrAdd(gameUlongUID, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
