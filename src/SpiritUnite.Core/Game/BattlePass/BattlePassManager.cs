using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Recharge;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.BattlePass;

public sealed record BattlePassOutcome(PB_ENUM_RES_ERROR_CODE Result, BattlePassCommit? Commit)
{
    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;

    public static BattlePassOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null);
}

public sealed class BattlePassManager(
    BattlePassSeasons seasons,
    BattlePassLeveling leveling,
    BattlePassRewards rewards,
    BattlePassTasks tasks,
    BattlePassRepository repository,
    RankedManager ranked,
    MailManager mail,
    ActivityRewardNotices rewardNotices,
    GameData gameData,
    TimeProvider clock,
    ILogger<BattlePassManager> logger)
{
    private const int NoBundledItem = 0;
    private const int BundledItemCount = 1;
    private const int PrimaryPackBuyType = 1;
    private const int HighPackBuyType = 2;
    private const int SeasonRewardMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeBpSeasonReward;
    private const string SeasonRewardMailSubject = "Mail_TextTitle_8_001";
    private const string SeasonRewardMailContent = "Mail_TextContent_8_001";

    private static readonly PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE[] RewardTypes = [PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE.PbEnmBattlepassLevelRewardTypeCommon, PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE.PbEnmBattlepassLevelRewardTypeElite];

    public BattlePassSeasonState FindCurrentSeason() => seasons.FindCurrentSeason(clock.GetUtcNow());

    public async Task<pbCOMDT_BATTLEPASS_INFO> BuildBattlePassInfoAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        var now = clock.GetUtcNow();
        await SettleEndedSeasonsAsync(role, now);
        var maxRankedDivision = await FindMaxRankedDivisionAsync(uid);
        var current = seasons.FindCurrentSeason(now);
        if (!current.IsInProcess)
        {
            return new pbCOMDT_BATTLEPASS_INFO
            {
                SeasonInfo = new pbCOMDT_BATTLEPASS_SEASON_INFO
                {
                    LevelReward = new pbCOMDT_LEVEL_REWARD_INFO(),
                    MaxRankedDivision = maxRankedDivision,
                },
                SeasonHistory = new pbCOMDT_BATTLEPASS_HISTORY_INFO(),
            };
        }

        var stored = await repository.LoadAsync(uid, current.Season.SeasonID);
        var history = new List<pbCOMDT_BATTLEPASS_SEASON_BRIEF>();
        foreach (var progress in await repository.ListAsync(uid))
        {
            if (progress.SeasonID == current.Season.SeasonID || seasons.FindSeason(progress.SeasonID) is not { } season)
            {
                continue;
            }

            var over = new BattlePassSeasonState(season, PB_ENUM_ENM_BATTLEPASS_SEASON_STATE.PbEnmBattlepassSeasonStateOver);
            history.Add(progress.ToSeasonBrief(over, await tasks.CountFinishedTasksAsync(role, over, now), maxRankedDivision));
        }

        return new pbCOMDT_BATTLEPASS_INFO
        {
            SeasonInfo = BuildSeasonInfo(current, stored, await tasks.CountFinishedTasksAsync(role, current, now), maxRankedDivision),
            WeekIndex = seasons.FindWeekIndex(current, now),
            SeasonHistory = new pbCOMDT_BATTLEPASS_HISTORY_INFO { SeasonBriefList = { history } },
        };
    }

    public async Task<pbSCBattlePassNtf> BuildBattlePassNtfAsync(LMNRoleInfo role) => new()
    {
        BattlePassInfo = await BuildBattlePassInfoAsync(role),
    };

    public static pbCSDT_BATTLE_PASS_TASK_REFRESH_INFO BuildTaskRefreshInfo() => new();

    public pbCOMDT_LEVEL_REWARD_INFO BuildLevelRewardInfo(BattlePassStored stored) => new()
    {
        Reward =
        {
            Enumerable.Range(BattlePassSeasons.StartingLevel, stored.Progress.Level)
                .Select(level => BuildLevelRewardDetail(stored, level)),
        },
    };

    public async Task<BattlePassOutcome> ClaimLevelRewardAsync(LMNRoleInfo role, int type, int level)
    {
        if (!Enum.IsDefined((PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE)type) || level < BattlePassSeasons.StartingLevel)
        {
            return Refuse(role, "level reward", PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        var key = new BattlePassLevelRewardKey(level, (PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE)type);
        return await ClaimLevelRewardsAsync(role, "level reward", stored => FindClaimable(stored).Contains(key) ? [key] : []);
    }

    public Task<BattlePassOutcome> ClaimAllLevelRewardsAsync(LMNRoleInfo role) =>
        ClaimLevelRewardsAsync(role, "batch level reward", FindClaimable);

    public Task<BattlePassOutcome> BuyLevelAsync(LMNRoleInfo role, int targetLevel) =>
        BuyLevelsAsync(role, $"level {targetLevel}", progress => (targetLevel, leveling.FindLevelCost(progress, targetLevel)));

    public Task<BattlePassOutcome> BuyLevelPackAsync(LMNRoleInfo role, int packBuyType)
    {
        var season = FindCurrentSeason().Season;
        (int Levels, int Price)? pack = packBuyType switch
        {
            PrimaryPackBuyType => (season.PrimaryPackLevel, season.PrimaryPackPrice),
            HighPackBuyType => (season.HighPackLevel, season.HighPackPrice),
            _ => null,
        };
        if (pack is not { Levels: > 0, Price: > 0 } levelPack)
        {
            return Task.FromResult(Refuse(role, "level pack purchase", PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid));
        }

        return BuyLevelsAsync(role, $"level pack {packBuyType}", progress => (progress.Level + levelPack.Levels, levelPack.Price));
    }

    private async Task<BattlePassOutcome> BuyLevelsAsync(
        LMNRoleInfo role,
        string purchase,
        Func<BattlePassProgress, (int TargetLevel, long Cost)> price)
    {
        var now = clock.GetUtcNow();
        var current = seasons.FindCurrentSeason(now);
        if (!current.IsInProcess)
        {
            return Refuse(role, "level purchase", PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidSeasonState);
        }

        var commit = await repository.CommitAsync(role.GameUlongUID, current.Season.SeasonID, stored =>
        {
            var progress = stored.Progress;
            if (!progress.HasPass)
            {
                return BattlePassDecision.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattlePassNoPass);
            }

            var (targetLevel, cost) = price(progress);
            if (targetLevel <= progress.Level || targetLevel > leveling.FindMaxLevel(progress))
            {
                return BattlePassDecision.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidTargetLevel);
            }

            if (leveling.FindLevelCoinType(progress) is not { } coinType
                || CoinID.GetCoinID((PB_ENUM_RES_COIN_TYPE)coinType) is not { } coinID)
            {
                return BattlePassDecision.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
            }

            var grant = BattlePassGrant.Empty with
            {
                Change = new Bag.BagChange { CoinCosts = new Dictionary<int, long> { [coinID] = cost } },
            };
            return new BattlePassDecision(
                PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
                new BattlePassMutation(
                    leveling.SetLevel(progress, targetLevel, now.ToUnixTimeSeconds()),
                    [],
                    grant,
                    [],
                    CouponsFlow.SpendOnBattlePassLevels(coinID, cost, now, progress.PassType, targetLevel - progress.Level)));
        }, now.ToUnixTimeSeconds());
        return Finish(role, "level purchase", commit, purchase);
    }

    public async Task<BattlePassOutcome> BuyPassAsync(LMNRoleInfo role, int passType, IReadOnlyList<int> bundledItemIds)
    {
        var now = clock.GetUtcNow();
        var current = seasons.FindCurrentSeason(now);
        if (!current.IsInProcess)
        {
            return Refuse(role, "pass purchase", PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidSeasonState);
        }

        var season = current.Season;
        var isElite = passType == (int)PB_ENUM_ENM_BATTLEPASS_PASSTYPE.PbEnmBattlepassPasstypeAdvanced;
        if ((passType != (int)PB_ENUM_ENM_BATTLEPASS_PASSTYPE.PbEnmBattlepassPasstypeNormal && !isElite)
            || seasons.FindPassInfo(season.SeasonID) is not { } passInfo)
        {
            return Refuse(role, "pass purchase", PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        if (FindBundlePrice(passInfo, bundledItemIds) is not { } bundlePrice)
        {
            return Refuse(role, "pass purchase", PB_ENUM_RES_ERROR_CODE.PbErrBattlePasssPokemonPreiceError);
        }

        var (passPrice, coinType) = isElite
            ? (season.ElitePassRealPrice, season.ElitePassCoinType)
            : (season.CommonPassRealPrice, season.CommonPassCoinType);
        if (passPrice <= 0 || CoinID.GetCoinID((PB_ENUM_RES_COIN_TYPE)coinType) is not { } coinID)
        {
            return Refuse(role, "pass purchase", PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        int[] dropIDs = isElite ? [passInfo.ExtraPassDropID, passInfo.HighPassDropID, season.ElitePassDropID] : [passInfo.ExtraPassDropID];
        BattlePassResource[] resources =
        [
            .. dropIDs.SelectMany(seasons.FindDropResources),
            .. bundledItemIds.Select(itemId => new BattlePassResource(itemId, BundledItemCount)),
        ];
        var commit = await repository.CommitAsync(role.GameUlongUID, season.SeasonID, stored =>
        {
            var progress = stored.Progress;
            if (progress.HasPass)
            {
                return BattlePassDecision.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattlePassAlreadyBuyPass);
            }

            if (rewards.Plan(role, resources, now.ToUnixTimeSeconds()) is not { } grant)
            {
                return BattlePassDecision.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
            }

            var bought = progress with { PassType = passType };
            if (isElite)
            {
                var levelAfterBonus = Math.Min(progress.Level + season.ElitePassLevelUp, leveling.FindMaxLevel(progress));
                bought = leveling.SetLevel(bought, levelAfterBonus, now.ToUnixTimeSeconds());
            }

            var paid = grant with
            {
                Change = grant.Change with { CoinCosts = new Dictionary<int, long> { [coinID] = passPrice + bundlePrice } },
            };
            var couponsFlow = CouponsFlow.SpendOnBattlePassPass(coinID, passPrice + bundlePrice, now, passType);
            return new BattlePassDecision(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, new BattlePassMutation(bought, [], paid, [], couponsFlow));
        }, now.ToUnixTimeSeconds());
        return Finish(role, "pass purchase", commit, $"pass type {passType}");
    }

    private static long? FindBundlePrice(pbBattlePassInfo passInfo, IReadOnlyList<int> bundledItemIds)
    {
        if (bundledItemIds.Distinct().Count() != bundledItemIds.Count)
        {
            return null;
        }

        long total = 0;
        foreach (var itemId in bundledItemIds)
        {
            var index = Array.IndexOf(passInfo.OutSideItemId, itemId);
            if (itemId == NoBundledItem || index < 0 || index >= passInfo.DiscountPrice.Length || passInfo.DiscountPrice[index] <= 0)
            {
                return null;
            }

            total += passInfo.DiscountPrice[index];
        }

        return total;
    }

    public async Task<BattlePassOutcome> OpenAdditionTreasureAsync(LMNRoleInfo role)
    {
        var now = clock.GetUtcNow();
        var current = seasons.FindCurrentSeason(now);
        if (!current.IsInProcess)
        {
            return Refuse(role, "addition treasure", PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidSeasonState);
        }

        var commit = await repository.CommitAsync(role.GameUlongUID, current.Season.SeasonID, stored =>
        {
            var progress = stored.Progress;
            var boxCnt = progress.AdditionTreasureUnGetCnt;
            if (boxCnt <= 0)
            {
                return BattlePassDecision.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattlePassAdditionTreasureNoReward);
            }

            var box = new BattlePassResource(current.Season.FullLevelTreasureBox, boxCnt);
            if (rewards.Plan(role, [box], now.ToUnixTimeSeconds()) is not { } grant)
            {
                return BattlePassDecision.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
            }

            var opened = progress with
            {
                AdditionTreasureCnt = progress.AdditionTreasureCnt + boxCnt,
                AdditionTreasureUnGetCnt = 0,
            };
            return new BattlePassDecision(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, new BattlePassMutation(opened, [], grant, []));
        }, now.ToUnixTimeSeconds());
        return Finish(role, "addition treasure", commit, "boxes");
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetVideoPlayedAsync(LMNRoleInfo role)
    {
        var now = clock.GetUtcNow();
        var current = seasons.FindCurrentSeason(now);
        if (!current.IsInProcess)
        {
            return Refuse(role, "video played flag", PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidSeasonState).Result;
        }

        var commit = await repository.CommitAsync(role.GameUlongUID, current.Season.SeasonID, stored => new BattlePassDecision(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            new BattlePassMutation(stored.Progress with { VideoPlayed = BattlePassProgress.VideoWatched }, [], BattlePassGrant.Empty, [])),
            now.ToUnixTimeSeconds());
        return Finish(role, "video played flag", commit, "season video").Result;
    }

    public PB_ENUM_RES_ERROR_CODE RefuseTaskRefresh(LMNRoleInfo role, int taskId) =>
        Refuse(
            role,
            $"task {taskId} refresh",
            FindCurrentSeason().IsInProcess
                ? PB_ENUM_RES_ERROR_CODE.PbErrTaskNotExist
                : PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidSeasonState).Result;

    public async Task<GamePacket[]> BuildChangePacketsAsync(LMNRoleInfo role, BattlePassCommit commit, PB_ENUM_ENM_RESOURCE_CHG_REASON reason)
    {
        if (!commit.Succeeded || commit.Mutation is not { } mutation || commit.Wallet is not { } wallet)
        {
            return [];
        }

        var grant = mutation.Grant;
        var change = grant.Change;
        var rewardGroup = new ActivityRewardGroup(
            wallet,
            [.. change.CoinCosts.Keys.Union(change.CoinGrants.Keys)],
            commit.SpecialCoinsBefore,
            commit.ChangedItems,
            grant.PokemonIds,
            grant.Awards);
        return
        [
            .. grant.Items
                .Where(item => item.ItemType == RoleItemType.AvatarItem)
                .Select(item => GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateAvatarNtf, new pbSCUpdateAvatarNtf
                {
                    UpdateType = (int)PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeGain,
                    AvatarInfo = item.ToAvatarInfo(),
                })),
            .. PokemonAvatarManager.BuildGainPushes(grant.Items),
            .. ProfileGainPushes.Build(gameData, role, grant.Items),
            .. await rewardNotices.BuildAsync(role, rewardGroup),
            .. grant.Awards.Count > 0
                ? [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
                {
                    ResultInfo = new pbResultInfo(),
                    AwardReason = (int)reason,
                    ResourcesList = { grant.Awards },
                })]
                : Array.Empty<GamePacket>(),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassNtf, await BuildBattlePassNtfAsync(role)),
        ];
    }

    private async Task<BattlePassOutcome> ClaimLevelRewardsAsync(
        LMNRoleInfo role,
        string action,
        Func<BattlePassStored, IReadOnlyList<BattlePassLevelRewardKey>> pick)
    {
        var now = clock.GetUtcNow();
        var current = seasons.FindCurrentSeason(now);
        if (!current.IsInProcess)
        {
            return Refuse(role, action, PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidSeasonState);
        }

        var seasonID = current.Season.SeasonID;
        var commit = await repository.CommitAsync(role.GameUlongUID, seasonID, stored =>
        {
            var picked = pick(stored);
            if (picked.Count == 0)
            {
                return BattlePassDecision.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattlePassBatchGetLevelRewardNoReward);
            }

            var resources = picked.SelectMany(key => BattlePassRewards.FindAwards(seasons.FindLevelReward(seasonID, key.Level)!, key.Type));
            return rewards.Plan(role, resources, now.ToUnixTimeSeconds()) is { } grant
                ? new BattlePassDecision(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, new BattlePassMutation(stored.Progress, picked, grant, []))
                : BattlePassDecision.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }, now.ToUnixTimeSeconds());
        return Finish(role, action, commit, "rewards");
    }

    private IReadOnlyList<BattlePassLevelRewardKey> FindClaimable(BattlePassStored stored) =>
    [
        .. seasons.LevelRewardsUpTo(stored.Progress.SeasonID, stored.Progress.Level)
            .SelectMany(reward => RewardTypes
                .Where(type => type == PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE.PbEnmBattlepassLevelRewardTypeCommon || stored.Progress.HasPass)
                .Where(type => BattlePassRewards.HasAwards(reward, type))
                .Select(type => new BattlePassLevelRewardKey(reward.Level, type)))
            .Where(key => !stored.ClaimedRewards.Contains(key)),
    ];

    private pbCOMDT_BATTLEPASS_SEASON_INFO BuildSeasonInfo(
        BattlePassSeasonState current,
        BattlePassStored stored,
        BattlePassTaskFinishCnt finishCnt,
        int maxRankedDivision)
    {
        var progress = stored.Progress;
        return new pbCOMDT_BATTLEPASS_SEASON_INFO
        {
            SeasonID = current.Season.SeasonID,
            SeasonStartTime = current.StartTime,
            SeasonEndTime = current.EndTime,
            SeasonState = (int)current.SeasonState,
            Level = progress.Level,
            Exp = progress.Exp,
            PassType = progress.PassType,
            LevelReward = BuildLevelRewardInfo(stored),
            LastUpLevelTime = progress.LastUpLevelTime,
            LastGainExpTime = progress.LastGainExpTime,
            TotalExp = progress.TotalExp,
            DailyTaskFinishCnt = finishCnt.DailyTaskFinishCnt,
            WeeklyTaskFinishCnt = finishCnt.WeeklyTaskFinishCnt,
            ChallengeTaskFinishCnt = finishCnt.ChallengeTaskFinishCnt,
            AdditionTreasureCnt = progress.AdditionTreasureCnt,
            AdditionTreasureUnGetCnt = progress.AdditionTreasureUnGetCnt,
            AdditionTreasureUnGetExp = progress.AdditionTreasureUnGetExp,
            MaxRankedDivision = maxRankedDivision,
            VideoPlayed = progress.VideoPlayed,
        };
    }

    private pbCOMDT_LEVEL_REWARD_DETAIL BuildLevelRewardDetail(BattlePassStored stored, int level)
    {
        var reward = seasons.FindLevelReward(stored.Progress.SeasonID, level);
        return new pbCOMDT_LEVEL_REWARD_DETAIL
        {
            NormalState = (int)FindRewardState(stored, reward, level, PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE.PbEnmBattlepassLevelRewardTypeCommon),
            EliteState = (int)FindRewardState(stored, reward, level, PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE.PbEnmBattlepassLevelRewardTypeElite),
        };
    }

    private static PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_STATE FindRewardState(
        BattlePassStored stored,
        pbBattlePassLevelReward? reward,
        int level,
        PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE type) =>
        reward is null || !BattlePassRewards.HasAwards(reward, type)
            ? PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_STATE.PbEnmBattlepassLevelRewardStateNone
            : stored.ClaimedRewards.Contains(new BattlePassLevelRewardKey(level, type))
                ? PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_STATE.PbEnmBattlepassLevelRewardStateAwarded
                : PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_STATE.PbEnmBattlepassLevelRewardStateReached;

    private async Task<int> FindMaxRankedDivisionAsync(ulong uid) =>
        (await ranked.BuildHighestRankedDivisionInHistoryAsync(uid)).RankedDivision;

    private async Task SettleEndedSeasonsAsync(LMNRoleInfo role, DateTimeOffset now)
    {
        var uid = role.GameUlongUID;
        var ended = (await repository.ListAsync(uid))
            .Where(progress => !progress.SeasonSettled && seasons.IsOver(progress.SeasonID, now))
            .ToArray();
        foreach (var progress in ended)
        {
            var savedMails = await repository.SettleSeasonAsync(uid, progress.SeasonID, stored => BuildUnclaimedRewardMails(uid, stored));
            if (savedMails.Count > 0)
            {
                logger.LogInformation(
                    "Battle pass season {SeasonID} of uid {Uid} settled: {MailCount} reward mails committed",
                    progress.SeasonID, uid, savedMails.Count);
            }

            await mail.PushNewMailsAsync(savedMails);
        }
    }

    private IReadOnlyList<RoleMail> BuildUnclaimedRewardMails(ulong uid, BattlePassStored stored)
    {
        var seasonID = stored.Progress.SeasonID;
        var resources = FindClaimable(stored)
            .SelectMany(key => BattlePassRewards.FindAwards(seasons.FindLevelReward(seasonID, key.Level)!, key.Type))
            .ToList();
        if (stored.Progress.AdditionTreasureUnGetCnt > 0 && seasons.FindSeason(seasonID) is { FullLevelTreasureBox: not 0 } season)
        {
            resources.Add(new BattlePassResource(season.FullLevelTreasureBox, stored.Progress.AdditionTreasureUnGetCnt));
        }

        var attachments = resources
            .GroupBy(resource => resource.ResID)
            .Select(group => BattlePassRewards.ToAwardInfo(new BattlePassResource(group.Key, group.Sum(resource => resource.ResNum))))
            .ToArray();
        var unattachable = attachments.Where(award => !mail.CanAttach(award)).Select(award => award.ResID).ToArray();
        if (unattachable.Length > 0)
        {
            logger.LogWarning(
                "Battle pass season {SeasonID} rewards [{ResIDs}] of uid {Uid} cannot be mailed; they are not sent",
                seasonID, string.Join(", ", unattachable), uid);
        }

        var mailed = attachments.Where(mail.CanAttach).ToArray();
        return mailed.Chunk(MailBox.MaxMailAccess)
            .Select(chunk => mail.CreateSystemMail(uid, new SystemMail(
                Subject: SeasonRewardMailSubject,
                Content: SeasonRewardMailContent,
                Attachments: chunk,
                MailID: SeasonRewardMailID)))
            .ToArray();
    }

    private BattlePassOutcome Finish(LMNRoleInfo role, string action, BattlePassCommit commit, string what)
    {
        if (!commit.Succeeded)
        {
            return Refuse(role, action, commit.Result);
        }

        foreach (var item in commit.Mutation!.Grant.Items)
        {
            role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
        }

        var gainTime = clock.GetUtcNow().ToUnixTimeSeconds();
        foreach (var pokemonId in commit.Mutation.Grant.PokemonIds)
        {
            role.GrantPokemon(pokemonId, gainTime, (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonBattlepassLevelReward);
        }

        var progress = commit.Stored!.Progress;
        logger.LogInformation(
            "Battle pass {Action} for uid {Uid} committed ({What}): season {SeasonID} level {Level} exp {Exp}, {AwardCount} awards",
            action, role.GameUlongUID, what, progress.SeasonID, progress.Level, progress.Exp, commit.Mutation.Grant.Awards.Count);
        return new BattlePassOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, commit);
    }

    private BattlePassOutcome Refuse(LMNRoleInfo role, string action, PB_ENUM_RES_ERROR_CODE result)
    {
        var current = seasons.FindCurrentSeason(clock.GetUtcNow());
        logger.LogInformation(
            "Battle pass {Action} from uid {Uid} refused with {Result}: season {SeasonID} is {SeasonState}",
            action, role.GameUlongUID, result, current.Season.SeasonID, current.SeasonState);
        return BattlePassOutcome.Refused(result);
    }
}
