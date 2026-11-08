using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed record EeveeChallengeOutcome(PB_ENUM_RES_ERROR_CODE Result, GamePacket[] BeforeRsp, int EeveeID = EeveeChallengeManager.NoEevee)
{
    public static EeveeChallengeOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed class EeveeChallengeManager(
    ActivitySchedule schedule,
    EeveeChallengePool pool,
    EeveeChallengeRepository repository,
    ActivityManager activityManager,
    TaskUpdates taskUpdates,
    TimeProvider clock,
    ILogger<EeveeChallengeManager> logger)
{
    public const int NoEevee = 0;
    public const int EeveeChallengeRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdEeveechallenge;

    private const int FirstPeriod = 1;
    private const int Clicked = 1;
    private const int Replaced = 1;
    private const int UseOutSideItemReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonUseoutsideitem;
    private const int ConsumedItemCnt = 0;
    private const int OneExpireTimeItem = 1;

    public async Task<pbSCEeveeChallengeStatusRsp> BuildStatusRspAsync(LMNRoleInfo role)
    {
        if (FindOpenActivity() is not { } activity)
        {
            return new pbSCEeveeChallengeStatusRsp { ResultInfo = new pbResultInfo(), AllPeriod = FirstPeriod };
        }

        var periods = await FindPeriodsAsync(role.GameUlongUID, activity.Row.ActivityID);
        var allPeriod = CountPeriods(activity.Row.ActivityID);
        return new pbSCEeveeChallengeStatusRsp
        {
            ResultInfo = new pbResultInfo(),
            EeveeList =
            {
                Enumerable.Range(FirstPeriod, allPeriod).Select(period => periods.GetValueOrDefault(period) is { } saved
                    ? new pbCOMDT_EEVEE_CHALLENGE_DETAIL { EeveeID = saved.EeveeID, CurrPeriod = period, IsClick = saved.IsClick }
                    : new pbCOMDT_EEVEE_CHALLENGE_DETAIL { CurrPeriod = period }),
            },
            AllPeriod = allPeriod,
        };
    }

    public async Task<pbCOMDT_RED_DOT_DETAIL?> FindRedDotAsync(LMNRoleInfo role)
    {
        if (FindOpenActivity() is not { } activity)
        {
            return null;
        }

        var unclicked = await CountUnclickedPeriodsAsync(role.GameUlongUID, activity.Row.ActivityID);
        return unclicked > 0 ? new pbCOMDT_RED_DOT_DETAIL { RedDotID = EeveeChallengeRedDotID, RedDotBadgeNum = unclicked } : null;
    }

    public async Task<EeveeChallengeOutcome> ClickAsync(LMNRoleInfo role, int period)
    {
        var uid = role.GameUlongUID;
        var validation = Validate(period);
        if (validation.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Eevee challenge click on period {Period} refused for uid {Uid}: {Result}", period, uid, validation.Result);
            return EeveeChallengeOutcome.Refused(validation.Result);
        }

        var activityID = validation.Activity!.Row.ActivityID;
        await repository.ClickAsync(uid, activityID, period);
        var unclicked = await CountUnclickedPeriodsAsync(uid, activityID);
        return new EeveeChallengeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, [BuildRedDotPacket(unclicked)]);
    }

    public async Task<EeveeChallengeOutcome> DrawAsync(LMNRoleInfo role, int period)
    {
        var uid = role.GameUlongUID;
        var validation = Validate(period);
        if (validation.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return RefuseDraw(uid, period, validation.Result);
        }

        var activityID = validation.Activity!.Row.ActivityID;
        var eevees = pool.Find(activityID);
        if (eevees.Count == 0)
        {
            return RefuseDraw(uid, period, PB_ENUM_RES_ERROR_CODE.PbErrEeveeChallengeConfigErr);
        }

        var periods = await FindPeriodsAsync(uid, activityID);
        if (periods.GetValueOrDefault(period) is { EeveeID: not NoEevee })
        {
            return RefuseDraw(uid, period, PB_ENUM_RES_ERROR_CODE.PbErrEeveeChallengeDrawFailed);
        }

        var drawnEeveeIDs = periods.Values.Select(saved => saved.EeveeID).ToHashSet();
        var candidates = eevees.Where(eevee => !drawnEeveeIDs.Contains(eevee.EeveeID)).ToArray();
        if (candidates.Length == 0)
        {
            return RefuseDraw(uid, period, PB_ENUM_RES_ERROR_CODE.PbErrEeveeChallengeNoEeveeCanDraw);
        }

        var drawn = candidates[Random.Shared.Next(candidates.Length)];
        var item = drawn.PokemonExpireTimeItem;
        var transmutes = role.OwnsPokemon(drawn.EeveeID);
        if (transmutes && (item.TransmuteNum <= 0 || !CoinID.IsGrantableCoin(item.Transmuteid)))
        {
            return RefuseDraw(uid, period, PB_ENUM_RES_ERROR_CODE.PbErrEeveeChallengeConfigErr);
        }

        var drawTime = clock.GetUtcNow().ToUnixTimeSeconds();
        var timeLimitedPokemon = transmutes
            ? null
            : new RolePokemonInfo(drawn.EeveeID, drawTime, UseOutSideItemReason, RolePokemonInfo.HasNewTag, drawn.TimeLimited);
        var coinGrants = transmutes
            ? new Dictionary<int, long> { [item.Transmuteid] = item.TransmuteNum }
            : new Dictionary<int, long>();
        var commit = await repository.CommitDrawAsync(uid, activityID, period, drawn.EeveeID, drawTime, timeLimitedPokemon, coinGrants);
        if (commit.Wallet is not { } wallet)
        {
            return RefuseDraw(uid, period, commit.Result);
        }

        if (timeLimitedPokemon is not null)
        {
            role.GrantTimeLimitedPokemon(timeLimitedPokemon);
        }

        logger.LogInformation(
            "Eevee challenge period {Period} of activity {ActivityID} drew Pokemon {EeveeID} for uid {Uid}: {Grant}",
            period, activityID, drawn.EeveeID, uid, transmutes ? $"owned, {item.TransmuteNum} of coin {item.Transmuteid}" : $"time-limited until {drawn.TimeLimited}");
        var ownership = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role));
        GamePacket[] beforeRsp =
        [
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, new pbSCUpdateTaskInfoNtf
            {
                TaskInfoList = { await activityManager.BuildTaskInfoListAsync(role, drawn.TaskIDList) },
            }),
            GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify,
                BagManager.BuildOutSideItemUpdateNotify([new BagItem(RoleItemType.OutSideItem, item.OutSideItemId, ConsumedItemCnt, 0, 0, RolePokemonInfo.NoNewTag)])),
            ownership,
            .. timeLimitedPokemon is null
                ? BuildTransmutePackets(item, commit, wallet)
                : BuildTimeLimitedPokemonPackets(role, timeLimitedPokemon),
            ownership,
            ownership,
        ];
        return new EeveeChallengeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, beforeRsp, drawn.EeveeID);
    }

    private static GamePacket[] BuildTimeLimitedPokemonPackets(LMNRoleInfo role, RolePokemonInfo timeLimitedPokemon) =>
    [
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGainTimeLimitedPokemonNtf, new pbSCGainTimeLimitedPokemonNtf
        {
            PokemonInfo = timeLimitedPokemon.ToPokemonInfo(),
        }),
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
        {
            UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
            PokemonInfo = timeLimitedPokemon.ToPokemonInfo(),
        }),
        StarRoadManager.BuildAddPokemonRedDot(role),
    ];

    private static GamePacket[] BuildTransmutePackets(pbOutSideItem_Base item, EeveeChallengeDrawCommit commit, RoleWallet wallet) =>
    [
        .. commit.SpecialCoinsBefore.Select(coin => GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
            SpecialCoin.BuildSpecialCoinChangeNtf(coin.Key, coin.Value, wallet.GetCoinVal(coin.Key)))),
        .. wallet.BuildCoinValPushes([item.Transmuteid]),
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = UseOutSideItemReason,
            ResourcesList =
            {
                new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
                {
                    IsReplaced = Replaced,
                    FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
                    {
                        ResID = item.Transmuteid,
                        ResCnt = item.TransmuteNum,
                        ResType = (int)CoinID.GetCoinResourceType(item.Transmuteid),
                    },
                    OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
                    {
                        ResID = item.OutSideItemId,
                        ResCnt = OneExpireTimeItem,
                        ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem,
                    },
                },
            },
        }),
    ];

    private EeveeChallengeOutcome RefuseDraw(ulong uid, int period, PB_ENUM_RES_ERROR_CODE result)
    {
        logger.LogInformation("Eevee challenge draw for period {Period} refused for uid {Uid}: {Result}", period, uid, result);
        return EeveeChallengeOutcome.Refused(result);
    }

    private (PB_ENUM_RES_ERROR_CODE Result, ScheduledActivity? Activity) Validate(int period) =>
        FindOpenActivity() is not { } activity ? (PB_ENUM_RES_ERROR_CODE.PbErrEeveeChallengeNotExit, null)
        : period < FirstPeriod || period > CountPeriods(activity.Row.ActivityID) ? (PB_ENUM_RES_ERROR_CODE.PbErrEeveeChallengeCanNotActivate, activity)
        : (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, activity);

    private int CountPeriods(int activityID) => pool.Find(activityID).Count;

    private ScheduledActivity? FindOpenActivity() =>
        schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeEeveeChallenge, clock.GetUtcNow());

    private async Task<Dictionary<int, EeveeChallengePeriod>> FindPeriodsAsync(ulong uid, int activityID) =>
        (await repository.FindAsync(uid))
            .Where(saved => saved.ActivityID == activityID)
            .ToDictionary(saved => saved.Period);

    private async Task<int> CountUnclickedPeriodsAsync(ulong uid, int activityID)
    {
        var periods = await FindPeriodsAsync(uid, activityID);
        return Enumerable.Range(FirstPeriod, CountPeriods(activityID)).Count(period => periods.GetValueOrDefault(period)?.IsClick != Clicked);
    }

    private static GamePacket BuildRedDotPacket(int unclicked) => unclicked > 0
        ? GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = EeveeChallengeRedDotID, RedDotBadgeNum = unclicked } },
            },
        })
        : GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = EeveeChallengeRedDotID });
}
