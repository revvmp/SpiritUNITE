using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Energy;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.PveChallenge;
using SpiritUnite.Game.Recharge;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Activity;

public sealed record ActivityExchangeOutcome(PB_ENUM_RES_ERROR_CODE Result, int DoneCount, IReadOnlyList<GamePacket> BeforeRsp)
{
    public static ActivityExchangeOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, 0, []);
}

public sealed record ActivityExchangeNotifyOutcome(PB_ENUM_RES_ERROR_CODE Result, int Notify);

public sealed class ActivityExchangeManager(
    GameData gameData,
    ActivitySchedule schedule,
    ActivityExchangeRepository repository,
    PveChallengeManager pveChallenge,
    ActivityManager activityManager,
    ActivityRewardNotices rewardNotices,
    EnergyManager energies,
    TimeProvider clock)
{
    private const int NoExchangeGroup = 0;
    private const int NoResID = 0;
    private const int MinDoExchangeNum = 1;
    private const int NoPvePrivilege = 0;
    private const int CustomTimeExchange = 1;
    private const int SingleBuffLevel = 1;
    private const int SingleBuffReward = 1;
    private const int NotifyOff = 0;
    private const int NotifyOn = 1;
    private const int MaxExchangeInfoNum = (int)PB_MACRO_MAX_EXCHANGE_INFO_NUM.PbMaxExchangeInfoNum;

    private static readonly ActivityTaskGrant BuffOnlyGrant = new([], [], new BagChange(), [], new Dictionary<int, int>());

    private readonly ILookup<int, pbActivityExchange> exchangesByGroup = gameData.ActivityExchange.Rows.ToLookup(exchange => exchange.ExchangeGroupID);

    public async Task<pbCOMDT_ALL_EXCHANGE_INFO> BuildAllExchangeInfoAsync(LMNRoleInfo role)
    {
        var states = await repository.LoadStatesAsync(role.GameUlongUID);
        return new pbCOMDT_ALL_EXCHANGE_INFO
        {
            ExchangeList =
            {
                FindOpenExchangeActivities(clock.GetUtcNow())
                    .SelectMany(activity => exchangesByGroup[activity.Row.ActivityParam])
                    .DistinctBy(exchange => exchange.ExchangeID)
                    .OrderBy(exchange => exchange.ExchangeID)
                    .Where(exchange => states.GetValueOrDefault(exchange.ExchangeID) is { } state
                        && (state.DoneCount > 0 || state.Notify != NotifyOff))
                    .Select(exchange => new pbCOMDT_ONE_EXCHANGEINFO
                    {
                        ExchangeID = exchange.ExchangeID,
                        DoneCount = states[exchange.ExchangeID].DoneCount,
                        Notify = states[exchange.ExchangeID].Notify,
                    })
                    .Take(MaxExchangeInfoNum),
            },
        };
    }

    public async Task<ActivityExchangeOutcome> DoExchangeAsync(LMNRoleInfo role, int activityID, int exchangeID, int doExchangeNum)
    {
        if (doExchangeNum < MinDoExchangeNum)
        {
            return ActivityExchangeOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeParamErr);
        }

        var now = clock.GetUtcNow();
        var lookup = FindOpenExchange(activityID, exchangeID, now);
        if (lookup is not { Activity: { } activity, Exchange: { } exchange })
        {
            return ActivityExchangeOutcome.Refused(lookup.Result);
        }

        if (exchange.CustomTime == CustomTimeExchange && !IsInValidTime(exchange, now))
        {
            return ActivityExchangeOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeExpired);
        }

        var isPrivilegeActive = await pveChallenge.IsPrivilegeActiveAsync(role.GameUlongUID, now);
        if (exchange.NeedPvePrivilege != NoPvePrivilege && !isPrivilegeActive)
        {
            return ActivityExchangeOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrNoActivePvePrivilege);
        }

        var rewards = FindRewards(exchange, doExchangeNum);
        if (rewards.Any(reward => OwnsUnrepeatableReward(role, reward.ResID)))
        {
            return ActivityExchangeOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeCheckFail);
        }

        if (BuildTrade(role, exchange, activity, rewards, doExchangeNum, MaxDoneNum(exchange, isPrivilegeActive), now) is not { } trade)
        {
            return ActivityExchangeOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeDelAddOpFail);
        }

        var commit = await repository.CommitAsync(role.GameUlongUID, trade);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return ActivityExchangeOutcome.Refused(ToExchangeResult(commit.Result));
        }

        ActivityManager.GrantToRole(role, trade.Grant, trade.Now, PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonActivityexchange);
        return new ActivityExchangeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, commit.DoneCount, await BuildPushesAsync(role, trade, commit));
    }

    public async Task<ActivityExchangeNotifyOutcome> SetNotifyAsync(LMNRoleInfo role, int activityID, int exchangeID, int notify)
    {
        var found = FindOpenExchange(activityID, exchangeID, clock.GetUtcNow()).Result;
        var result = found != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc ? found
            : notify is not (NotifyOff or NotifyOn) ? PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeParamErr
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return new ActivityExchangeNotifyOutcome(result, await repository.LoadNotifyAsync(role.GameUlongUID, exchangeID));
        }

        await repository.SaveNotifyAsync(role.GameUlongUID, exchangeID, notify);
        return new ActivityExchangeNotifyOutcome(result, notify);
    }

    private OpenExchangeLookup FindOpenExchange(int activityID, int exchangeID, DateTimeOffset now)
    {
        if (FindOpenExchangeActivities(now).FirstOrDefault(activity => activity.Row.ActivityID == activityID) is not { } activity)
        {
            return new OpenExchangeLookup(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit);
        }

        return gameData.ActivityExchange.Find(exchangeID) is { } exchange && exchange.ExchangeGroupID == activity.Row.ActivityParam
            ? new OpenExchangeLookup(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, activity, exchange)
            : new OpenExchangeLookup(PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeGetExchangeErr);
    }

    public static int MaxDoneNum(pbActivityExchange exchange, bool isPrivilegeActive) =>
        exchange.PvePrivilegeExtra > 0 && isPrivilegeActive ? exchange.MaxDoneNum + exchange.PvePrivilegeExtra : exchange.MaxDoneNum;

    private IEnumerable<ScheduledActivity> FindOpenExchangeActivities(DateTimeOffset now) =>
        schedule.FindActive(now).Where(activity => activity.Row.ActivityParam != NoExchangeGroup && exchangesByGroup.Contains(activity.Row.ActivityParam));

    private static bool IsInValidTime(pbActivityExchange exchange, DateTimeOffset now) =>
        ActivitySchedule.ParseConfigTime(exchange.ValidStartTime) <= now && now <= ActivitySchedule.ParseConfigTime(exchange.ValidEndTime);

    private static (int ResID, long ResNum)[] FindRewards(pbActivityExchange exchange, int doExchangeNum)
    {
        IEnumerable<(int ResID, int ResNum)> rewards =
        [
            (exchange.ToResID, exchange.ToResNum),
            .. exchange.ToDst.Select(extra => (extra.ResID, extra.ResNum)),
        ];
        return
        [
            .. rewards
                .Where(reward => reward.ResID != NoResID && reward.ResNum > 0)
                .Select(reward => (reward.ResID, (long)reward.ResNum * doExchangeNum)),
        ];
    }

    private bool OwnsUnrepeatableReward(LMNRoleInfo role, int resID) =>
        CosmeticGrant.FindItemType(gameData, resID) is { } itemType
            ? role.OwnsItem(itemType, resID)
            : RoleAvatar.FindSuitParts(gameData, resID) is { } suitParts
                ? suitParts.All(avatarID => role.OwnsItem(RoleItemType.AvatarItem, avatarID))
            : gameData.LobbyBg.Contains(resID)
                ? role.OwnsLobbyBg(gameData, resID)
                : UniteLicense.FindPokemonId(gameData, resID) is { } pokemonId && role.OwnsPokemon(pokemonId);

    private ActivityExchangeTrade? BuildTrade(
        LMNRoleInfo role,
        pbActivityExchange exchange,
        ScheduledActivity activity,
        (int ResID, long ResNum)[] rewards,
        int doExchangeNum,
        int maxDoneNum,
        DateTimeOffset now)
    {
        var coinCosts = new Dictionary<int, long>();
        var itemCosts = new List<BagCost>();
        foreach (var cost in exchange.FromRes.Where(cost => cost.ID != NoResID && cost.Num > 0))
        {
            var amount = (long)cost.Num * doExchangeNum;
            if (CoinID.IsGrantableCoin(cost.ID))
            {
                coinCosts[cost.ID] = coinCosts.GetValueOrDefault(cost.ID) + amount;
            }
            else if (gameData.OutSideItemBase.Contains(cost.ID) && amount <= int.MaxValue)
            {
                itemCosts.Add(new BagCost(RoleItemType.OutSideItem, cost.ID, (int)amount));
            }
            else
            {
                return null;
            }
        }

        if (rewards.Any(reward => reward.ResNum > int.MaxValue))
        {
            return null;
        }

        var buffs = rewards.Select(reward => pveChallenge.FindBuffOfItem(reward.ResID)).OfType<PveChallengeBuff>().ToArray();
        if (buffs.Length > 0 && (rewards.Length != SingleBuffReward || rewards[0].ResNum != SingleBuffLevel))
        {
            return null;
        }

        var rewardTime = now.ToUnixTimeSeconds();
        var grant = buffs.Length > 0
            ? BuffOnlyGrant
            : activityManager.PlanResourcesGrant(role, rewards.Select(reward => (reward.ResID, (int)reward.ResNum)), rewardTime);
        if (grant is null)
        {
            return null;
        }

        var couponsFlow = CouponsFlow.Spend(coinCosts.GetValueOrDefault(CoinID.Coupons), now, CouponsFlow.ActivitySystemType, new pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION
        {
            ActivityInfo = new pbCOMDT_COUPONS_FLOW_ACTIVITY_TYPE
            {
                ActivityId = activity.Row.ActivityID,
                ActivityType = activity.Row.ActivityType,
            },
        });
        return new ActivityExchangeTrade(
            exchange.ExchangeID,
            doExchangeNum,
            maxDoneNum,
            grant.Change with { Costs = itemCosts, CoinCosts = coinCosts },
            grant,
            buffs.FirstOrDefault(),
            couponsFlow,
            rewardTime);
    }

    private static PB_ENUM_RES_ERROR_CODE ToExchangeResult(PB_ENUM_RES_ERROR_CODE result) => result switch
    {
        PB_ENUM_RES_ERROR_CODE.PbErrNotEnoughCoin or PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemDeleteNotEnough => PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeCheckFail,
        PB_ENUM_RES_ERROR_CODE.PbErrSpecialActivityOverLimit
            or PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeCheckFail
            or PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeAddDoneCntFail => result,
        _ => PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeDelAddOpFail,
    };

    private async Task<IReadOnlyList<GamePacket>> BuildPushesAsync(LMNRoleInfo role, ActivityExchangeTrade trade, ActivityExchangeCommit commit)
    {
        var packets = new List<GamePacket>();
        var changedCoinIDs = trade.Change.CoinCosts.Keys.Concat(trade.Change.CoinGrants.Keys).Distinct().ToArray();
        if (commit.Wallet is { } wallet)
        {
            packets.AddRange(wallet.BuildCoinValPushes(changedCoinIDs));
            packets.AddRange(changedCoinIDs.Where(CoinID.IsSpecialCoin).Select(coinID => GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, commit.BeforeCoinNums.GetValueOrDefault(coinID), wallet.GetCoinVal(coinID)))));
        }

        if (commit.ChangedItems.Count > 0)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(commit.ChangedItems)));
        }

        if (commit.Wallet is { } grantWallet && (trade.Grant.Items.Count > 0 || trade.Grant.PokemonIds.Count > 0))
        {
            packets.AddRange(await rewardNotices.BuildAsync(
                role,
                new ActivityRewardGroup(grantWallet, [], new Dictionary<int, long>(), [], trade.Grant.PokemonIds, []) { Items = trade.Grant.Items }));
        }

        if (commit.BuffState is { } buffState)
        {
            packets.Add(PveChallengeManager.BuildBuffShopNtf(pveChallenge.BuildBuffInfo(buffState), buffState));
        }

        packets.AddRange(await energies.BuildAcceleratorGainPushesAsync(role.GameUlongUID, trade.Change.Grants));
        if (trade.Grant.Awards.Count > 0)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonActivityexchange,
                ResourcesList = { trade.Grant.Awards },
            }));
        }

        return packets;
    }

    private sealed record OpenExchangeLookup(PB_ENUM_RES_ERROR_CODE Result, ScheduledActivity? Activity = null, pbActivityExchange? Exchange = null);
}
