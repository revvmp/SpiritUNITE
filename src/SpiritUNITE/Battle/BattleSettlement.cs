using System.Collections.Concurrent;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.FightHistory;
using SpiritUNITE.Game.HeldItem;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Newbie;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.PveChallenge;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.TrainerExpGift;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Battle;

public sealed record BattleResult(BattleDesk Desk, DeskPlayer Human, pbCSMultgameGameoverReq Gameover, long GameStartTime, int GameTime);

public sealed class BattleSettlement
{
    private const int AcntExpResID = (int)PB_ENUM_ENM_RESOURCES_ID.PbEnmResourcesIdAcntExp;
    private const int HeavenTrainerGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeHeavenTrainer;
    private const int PveChallengeStarReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonPveChallengeStar;
    private const int PveChallengeFirstpassReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonPveChallengeFirstpass;
    private const int StarTaskSystem = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemActivity;
    private const int WinResult = (int)PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeWin;
    private const int LoseResult = (int)PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeLose;
    private const int AcntObjType = 1;
    private const int NoPokemon = 0;
    private const int NoDrop = 0;
    private const int NoStar = 0;
    private const int FirstStar = 1;
    private const int LevelParamIndex = 0;
    private const int CompletedOnce = 1;
    private const int HasTrainerLevelGift = 1;
    private const int FirstPass = 1;
    private const int Unlocked = 1;
    private const int Completed = 1;
    private const int NoMonsterClass = 0;
    private const int NoOccupation = 0;
    private const int NoNextUnLock = 0;
    private const int NewNodeRedDotBadgeNum = 1;
    private const int IsAITypeOneBattleFlagBit = 2;
    private const long IsAITypeOneBattleFlag = 1L << IsAITypeOneBattleFlagBit;

    private static readonly TimeSpan SettleGainRetention = TimeSpan.FromMinutes(5);

    private readonly GameData gameData;
    private readonly RoleInfoStore roles;
    private readonly PveChallengeProgress pveChallenge;
    private readonly BagManager bag;
    private readonly HeldItemManager heldItems;
    private readonly PokemonManager pokemon;
    private readonly TaskUpdates taskUpdates;
    private readonly TrainerExpGifts trainerExpGifts;
    private readonly StarRoadManager starRoad;
    private readonly SettingsManager settings;
    private readonly LobbyNotices notices;
    private readonly NewbieChallengeManager newbieChallenges;
    private readonly NewbieChallengeTasks newbieChallengeTasks;
    private readonly BattleSettlementRepository repository;
    private readonly ActivitySchedule activities;
    private readonly RoleRepository roleRepository;
    private readonly LobbyRoles lobbyRoles;
    private readonly BattleSettlementOptions options;
    private readonly TimeProvider clock;
    private readonly ILogger<BattleSettlement> logger;
    private readonly IReadOnlyDictionary<int, int> pokemonIdByHeroID;
    private readonly ILookup<int, pbDrop_Static> dropsById;
    private readonly ConcurrentDictionary<ulong, pbSCMultiGameSettleGainRsp> settleGains = new();

    public BattleSettlement(
        GameData gameData,
        RoleInfoStore roles,
        PveChallengeProgress pveChallenge,
        BagManager bag,
        HeldItemManager heldItems,
        PokemonManager pokemon,
        TaskUpdates taskUpdates,
        TrainerExpGifts trainerExpGifts,
        StarRoadManager starRoad,
        SettingsManager settings,
        LobbyNotices notices,
        NewbieChallengeManager newbieChallenges,
        NewbieChallengeTasks newbieChallengeTasks,
        BattleSettlementRepository repository,
        ActivitySchedule activities,
        RoleRepository roleRepository,
        LobbyRoles lobbyRoles,
        IOptions<BattleSettlementOptions> options,
        TimeProvider clock,
        ILogger<BattleSettlement> logger)
    {
        this.gameData = gameData;
        this.roles = roles;
        this.pveChallenge = pveChallenge;
        this.bag = bag;
        this.heldItems = heldItems;
        this.pokemon = pokemon;
        this.taskUpdates = taskUpdates;
        this.trainerExpGifts = trainerExpGifts;
        this.starRoad = starRoad;
        this.settings = settings;
        this.notices = notices;
        this.newbieChallenges = newbieChallenges;
        this.newbieChallengeTasks = newbieChallengeTasks;
        this.repository = repository;
        this.activities = activities;
        this.roleRepository = roleRepository;
        this.lobbyRoles = lobbyRoles;
        this.options = options.Value;
        this.clock = clock;
        this.logger = logger;
        pokemonIdByHeroID = gameData.PokemonBase.Rows
            .Where(row => row.MainUnitId != NoPokemon)
            .GroupBy(row => row.MainUnitId)
            .ToDictionary(group => group.Key, group => group.First().PokemonId);
        dropsById = gameData.DropStatic.Rows.OrderBy(drop => drop.SDropSeq).ToLookup(drop => drop.SDropID);
    }

    public pbSCMultiGameSettleGainRsp? FindSettleGain(ulong uid) => settleGains.GetValueOrDefault(uid);

    public async Task SettleAsync(BattleResult result)
    {
        var uid = result.Human.Uid;
        var role = roles.Find(uid) ?? await roles.FindAsync(uid);
        if (role is null)
        {
            logger.LogWarning("Desk {DeskID}: uid {Uid} has no role to settle", result.Desk.DeskID, uid);
            return;
        }

        var route = FindSettledRoute(role, result);
        var node = route is null ? null : new PveChallengeNodeKey(route.StageID, route.NodeID, route.DifficultyLevel);
        var knownStarNum = route is null ? NoStar : pveChallenge.GetStarNum(role, route);
        var utcNow = clock.GetUtcNow();
        var now = utcNow.ToUnixTimeSeconds();
        var levelBefore = RoleProfile.GetLevel(gameData, role);
        var challengeUnlockedBefore = newbieChallenges.IsUnlocked(role);
        var newbieBenefitActive = await IsNewbieBenefitActiveAsync(uid, utcNow);
        var fightRecordNtf = BuildFightRecordNtf(result, IsWon(result));
        var fightHistory = FightHistoryRecords.CreateRecord(
            uid,
            fightRecordNtf.FightRecord,
            result.Gameover.GameoverSummary?.BattleReviewInfo,
            now);
        SettlementPlan? plan = null;
        IReadOnlyList<BagGrant> heldItemGrants = [];
        var commit = await repository.CommitAsync(uid, node, async storedStarNum =>
        {
            plan = Plan(role, result, route, Math.Max(storedStarNum, knownStarNum), newbieBenefitActive);
            heldItemGrants = await heldItems.CreateHeldItemUnlockGrantsAsync(role, role.RoleTotalExp + plan.ChgExp);
            return new BattleSettlementChange(
                plan.ChgExp,
                plan.PointPokemonId,
                plan.PointPokemonId == NoPokemon ? 0 : plan.IncExp,
                plan.Won ? now : UsedPokemonInfo.NeverWon,
                plan.NewStar,
                FindHeavenTrainerChallenge(result),
                [.. heldItemGrants, .. plan.Grants],
                plan.Coins,
                fightHistory,
                FightHistoryRecords.MaxRecordNumPerMapSubMode);
        }, now);
        if (commit is null || plan is null)
        {
            logger.LogError("Desk {DeskID}: settlement of uid {Uid} was not committed", result.Desk.DeskID, uid);
            return;
        }

        foreach (var clamped in commit.ClampedGrants)
        {
            logger.LogWarning(
                "Desk {DeskID}: uid {Uid} got {Cnt} of item {ItemId}; the rest did not fit its stack of {StackSize}",
                result.Desk.DeskID,
                uid,
                clamped.Cnt,
                clamped.ItemId,
                clamped.StackSize);
        }

        role.AddRoleTotalExp(plan.ChgExp);
        if (plan.NewStar is { } star)
        {
            role.RecordPveChallengeStarNum(star);
        }

        if (commit.HeavenTrainerRecord is { } heavenTrainerRecord)
        {
            role.RecordHeavenTrainerChallengeCnt(heavenTrainerRecord);
        }

        await roles.SaveAsync(role);
        logger.LogInformation(
            "Desk {DeskID}: uid {Uid} settled ({Result}): +{ChgExp} trainer exp, {StarNum} stars on node {NodeID}, {GrantCount} item grants",
            result.Desk.DeskID,
            uid,
            plan.Won ? "win" : "loss",
            plan.ChgExp,
            plan.StarNum,
            result.Desk.PveChallengeSelectNode?.NodeID,
            heldItemGrants.Count + plan.ItemGrants.Count);
        var gain = BuildSettleGainRsp(role, result, plan, commit, levelBefore, now);
        settleGains[uid] = gain;
        _ = ForgetSettleGainAsync(uid, gain);
        var battleTaskPackets = await taskUpdates.BuildBattleProgressPacketsAsync(
            role,
            BuildBattleTaskEvent(result, plan),
            [.. plan.StarTasks.Select(task => BuildStarTaskInfo(task, now))]);
        if (!lobbyRoles.IsOnline(uid))
        {
            logger.LogInformation("Settlement of uid {Uid} was stored but not delivered: the role is offline", uid);
            return;
        }

        var packets = await BuildPushesAsync(
            role,
            result,
            plan,
            commit,
            heldItemGrants,
            levelBefore,
            challengeUnlockedBefore,
            battleTaskPackets,
            gain,
            fightRecordNtf);
        if (!await lobbyRoles.PushAsync(uid, packets))
        {
            logger.LogInformation("Settlement of uid {Uid} was stored but not delivered: the push failed", uid);
        }
    }

    private async Task ForgetSettleGainAsync(ulong uid, pbSCMultiGameSettleGainRsp gain)
    {
        try
        {
            await Task.Delay(SettleGainRetention, clock);
            settleGains.TryRemove(new KeyValuePair<ulong, pbSCMultiGameSettleGainRsp>(uid, gain));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Forgetting the settlement of uid {Uid} failed", uid);
        }
    }

    private pbPveChallengeStageRoute? FindSettledRoute(LMNRoleInfo role, BattleResult result)
    {
        if (result.Desk.PveChallengeSelectNode is not { } node
            || pveChallenge.FindRoute(node.StageID, node.NodeID, node.DifficultyLevel) is not { } route)
        {
            return null;
        }

        if (pveChallenge.IsUnlocked(role, route))
        {
            return route;
        }

        logger.LogWarning(
            "Desk {DeskID}: uid {Uid} has not unlocked PvE node {StageID}/{NodeID}/{DifficultyLevel}; it earns no stars or star rewards",
            result.Desk.DeskID,
            role.GameUlongUID,
            route.StageID,
            route.NodeID,
            route.DifficultyLevel);
        return null;
    }

    private ChallengeTrainerMapLevel? FindHeavenTrainerChallenge(BattleResult result)
    {
        var deskInfo = result.Desk.DeskInfo;
        return gameData.SceneMap.Find(deskInfo.MapId)?.GameType == HeavenTrainerGameType
            ? new ChallengeTrainerMapLevel(deskInfo.MapId, deskInfo.TrainerDifficultyLevel)
            : null;
    }

    private static bool IsWon(BattleResult result) => result.Gameover.BattleParam.WinCampClient == result.Human.CampClient;

    private async Task<bool> IsNewbieBenefitActiveAsync(ulong uid, DateTimeOffset now)
    {
        var newbieShownDays = activities.FindActive(now)
            .Select(activity => activity.Row.NewbieShownTime)
            .Where(days => days > 0)
            .ToArray();
        if (newbieShownDays.Length == 0)
        {
            return false;
        }

        var registerTime = DateTimeOffset.FromUnixTimeSeconds(await roleRepository.FindCreateTimeAsync(uid));
        return now - registerTime < TimeSpan.FromDays(newbieShownDays.Max());
    }

    private SettlementPlan Plan(LMNRoleInfo role, BattleResult result, pbPveChallengeStageRoute? route, int previousStarNum, bool newbieBenefitActive)
    {
        var won = IsWon(result);
        var acntInfo = result.Gameover.AcntInfo.First(info => info.AcntUid == result.Human.Uid && info.ObjType == AcntObjType);
        var pokemonLevel = acntInfo.CommonData?.PokemonData?.PokemonDetailInfo?.Level ?? 0;
        var heroID = result.Human.ChosenHeroID;
        var pokemonId = pokemonIdByHeroID.GetValueOrDefault(heroID);
        var baseExp = won ? options.PveChallengeWinBaseExp : options.PveChallengeLoseBaseExp;
        var extraExpOfNewbie = newbieBenefitActive ? baseExp : 0;
        var starTasks = route is null ? [] : route.TaskID.Where(taskId => taskId != NoDrop)
            .Select(taskId => new StarTask(taskId, won && IsStarTaskDone(taskId, won, pokemonLevel)))
            .ToArray();
        var starNum = won ? starTasks.Count(task => task.Done) : NoStar;
        var rewards = new List<StarReward>();
        if (route is not null)
        {
            if (previousStarNum == NoStar && starNum > NoStar)
            {
                rewards.AddRange(route.CompleteDrops.Where(dropId => dropId != NoDrop).Select(dropId => new StarReward(dropId, NoStar)));
            }

            for (var star = Math.Max(previousStarNum + 1, FirstStar); star <= starNum; star++)
            {
                rewards.AddRange(StarDrops(route, star).Where(dropId => dropId != NoDrop).Select(dropId => new StarReward(dropId, star)));
            }
        }

        var drops = rewards.SelectMany(reward => dropsById[reward.DropID]).ToArray();
        var itemGrants = new List<DropGrant>();
        var coins = new Dictionary<int, long>();
        var dropExp = 0;
        foreach (var drop in drops)
        {
            if (drop.ResID == AcntExpResID)
            {
                dropExp += drop.ResNum;
            }
            else if (CoinID.WalletCoins.Contains(drop.ResID) || CoinID.IsSpecialCoin(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
            }
            else if (bag.CreateOutSideItemGrant(drop.ResID, drop.ResNum) is { } grant)
            {
                itemGrants.Add(new DropGrant(drop, grant));
            }
            else
            {
                logger.LogWarning("PvE star drop {SDropID} resource {ResID} has no grant rule and is skipped", drop.SDropID, drop.ResID);
            }
        }

        var incExp = baseExp + extraExpOfNewbie;
        return new SettlementPlan(
            won,
            heroID,
            role.OwnsPokemon(pokemonId) ? pokemonId : NoPokemon,
            baseExp,
            extraExpOfNewbie,
            incExp,
            incExp + dropExp,
            route,
            starTasks,
            starNum,
            previousStarNum,
            route is not null && starNum > previousStarNum
                ? new RolePveChallengeStar(route.StageID, route.NodeID, route.DifficultyLevel, starNum)
                : null,
            rewards,
            itemGrants,
            coins);
    }

    private BattleTaskEvent BuildBattleTaskEvent(BattleResult result, SettlementPlan plan)
    {
        var statistic = result.Gameover.AcntInfo
            .FirstOrDefault(info => info.AcntUid == result.Human.Uid && info.ObjType == AcntObjType)?
            .CommonData?.PokemonData?.PokemonBattleStatistic;
        var sceneInteraction = statistic?.BaseStatistic?.SceneInteractionInfo;
        var joinKills = statistic?.KillMonster?.JoinKilltMonsterDetail
            .Select(kill => new BattleMonsterKill(gameData.UnitBase.Find(kill.MonsterID)?.MonsterClassType ?? NoMonsterClass, Math.Max(0, kill.KillCnt)))
            .ToArray() ?? [];
        var occupationType = pokemonIdByHeroID.TryGetValue(plan.HeroID, out var pokemonId)
            ? gameData.PokemonBase.Find(pokemonId)?.OccupationType ?? NoOccupation
            : NoOccupation;
        return new BattleTaskEvent(
            plan.Won,
            occupationType,
            Math.Max(0, statistic?.BreakCnt ?? 0),
            BattleTasks.AddCounts(sceneInteraction?.PickRecoverBerry ?? 0, sceneInteraction?.PickMoveBerry ?? 0),
            joinKills);
    }

    private bool IsStarTaskDone(int taskId, bool won, int pokemonLevel) =>
        gameData.TaskBase.Find(taskId)?.FinishCondition.FirstOrDefault() is { } condition
        && (PB_ENUM_RES_CONDITION_ID)condition.ID switch
        {
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdWin => won,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdBattlePokemonLevel =>
                condition.ParamB.Count > LevelParamIndex && pokemonLevel >= condition.ParamB[LevelParamIndex],
            _ => false,
        };

    private static IEnumerable<int> StarDrops(pbPveChallengeStageRoute route, int star) => star switch
    {
        1 => route.OneStarDrops,
        2 => route.TwoStarDrops,
        3 => route.ThreeStarDrops,
        _ => [],
    };

    private async Task<GamePacket[]> BuildPushesAsync(
        LMNRoleInfo role,
        BattleResult result,
        SettlementPlan plan,
        BattleSettlementCommit commit,
        IReadOnlyList<BagGrant> heldItemGrants,
        int levelBefore,
        bool challengeUnlockedBefore,
        IReadOnlyList<GamePacket> battleTaskPackets,
        pbSCMultiGameSettleGainRsp gain,
        pbSCFightRecordNtf fightRecordNtf)
    {
        var packets = new List<GamePacket>();
        if (commit.UsedPokemon is { } used)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateUsedPokemonNtf, pokemon.BuildUpdateUsedPokemonNtf(used)));
        }

        var itemsById = commit.ChangedItems.ToDictionary(item => item.ItemId);
        foreach (var grant in heldItemGrants.Where(grant => itemsById.ContainsKey(grant.ItemId)))
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify([itemsById[grant.ItemId]])));
        }

        if (heldItemGrants.Count > 0)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidHeldItemSlotNtf, await heldItems.BuildHeldItemSlotNtfAsync(role)));
        }

        var levelChanged = RoleProfile.GetLevel(gameData, role) != levelBefore;
        if (levelChanged)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildTrainerLevelUpdateTaskInfoNtfAsync(role)));
        }

        var challengeUnlock = !challengeUnlockedBefore && newbieChallenges.IsUnlocked(role)
            ? await newbieChallengeTasks.BuildUnlockAsync(role)
            : null;
        if (challengeUnlock is not null)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, challengeUnlock.ReceivedTasks));
        }

        if (levelChanged)
        {
            packets.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf,
                LobbyNotices.BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewActivityTask)));
            var countryCode = (await settings.LoadAsync(role.GameUlongUID)).CountryCode;
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidActivityInfoNtf, notices.BuildActivityInfoNtf(countryCode)));
        }

        if (heldItemGrants.Count > 0)
        {
            packets.AddRange(await BuildHeldItemUnlockTaskPacketsAsync(role, plan, heldItemGrants, itemsById));
        }

        if (challengeUnlock is not null)
        {
            packets.AddRange(challengeUnlock.DayTaskProgress);
        }

        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTrainerExpNtf, new pbSCUpdateTrainerExpNtf
        {
            Level = RoleProfile.GetLevel(gameData, role),
            ChgExp = plan.ChgExp,
            CurToTalExp = role.RoleTotalExp,
            ReachMaxLevel = RoleProfile.HasReachedMaxLevel(gameData, role) ? 1 : 0,
            NextLevelToTalExp = RoleProfile.GetNextLevelTotalExp(gameData, role),
            SeasonPoint = TrainerExpGifts.GetSeasonPoint(role),
            ReachMaxSeasonGift = trainerExpGifts.GetReachMaxSeasonGift(role),
        }));
        if (trainerExpGifts.CountAbleToGet(role) > 0)
        {
            packets.Add(BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdTrainerLavelGift, HasTrainerLevelGift));
        }

        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidTrainerExpNextGiftInfoNtf, trainerExpGifts.BuildTrainerExpNextGiftInfoNtf(role)));
        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)));
        packets.AddRange(battleTaskPackets);
        packets.AddRange(await BuildStarDropPacketsAsync(role, plan, commit, itemsById));
        if (plan.Route is { } route && plan.NewStar is not null)
        {
            if (plan.IsFirstPass)
            {
                packets.AddRange(Enumerable.Repeat(
                    BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdPveChallengeNormal, NewNodeRedDotBadgeNum),
                    route.NextUnLock.Count(nodeID => nodeID != NoNextUnLock)));
            }

            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeStageInfoNtf, pveChallenge.BuildCompletionNtf(role, route)));
        }

        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultiGameSettleGainRsp, gain));
        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidFightRecordNtf, fightRecordNtf));
        return [.. packets];
    }

    private async Task<IEnumerable<GamePacket>> BuildHeldItemUnlockTaskPacketsAsync(
        LMNRoleInfo role,
        SettlementPlan plan,
        IReadOnlyList<BagGrant> heldItemGrants,
        IReadOnlyDictionary<int, BagItem> itemsById)
    {
        var grantedItems = heldItemGrants.Where(grant => itemsById.ContainsKey(grant.ItemId)).Select(grant => itemsById[grant.ItemId]).ToArray();
        var packets = new List<GamePacket>
        {
            BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdItemBox, grantedItems.Count(item => item.NewTag != 0)),
        };
        var ownershipUpdate = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role));
        packets.AddRange(Enumerable.Repeat(ownershipUpdate, grantedItems.Length));

        var unlockedSlotNum = heldItems.GetHeldItemSlotUnlockNum(role) - heldItems.GetHeldItemSlotUnlockNum(role.RoleTotalExp - plan.ChgExp);
        packets.Add(BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdUnlockHelditem, unlockedSlotNum));
        return packets;
    }

    private async Task<IEnumerable<GamePacket>> BuildStarDropPacketsAsync(
        LMNRoleInfo role,
        SettlementPlan plan,
        BattleSettlementCommit commit,
        IReadOnlyDictionary<int, BagItem> itemsById)
    {
        var packets = new List<GamePacket>();
        var creditedCoins = commit.BeforeCoinNums.ToDictionary();
        foreach (var reward in plan.Rewards)
        {
            var itemPackets = new List<GamePacket>();
            var specialCoinPackets = new List<GamePacket>();
            var walletChanged = false;
            var resources = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
            foreach (var drop in dropsById[reward.DropID].Where(drop => drop.ResID != AcntExpResID))
            {
                PB_ENUM_RES_RESOURCES_TYPE resType;
                if (CoinID.IsSpecialCoin(drop.ResID))
                {
                    var beforeCoinNum = creditedCoins.GetValueOrDefault(drop.ResID);
                    creditedCoins[drop.ResID] = beforeCoinNum + drop.ResNum;
                    specialCoinPackets.Add(GamePacket.Create(
                        PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                        SpecialCoin.BuildSpecialCoinChangeNtf(drop.ResID, beforeCoinNum, beforeCoinNum + drop.ResNum)));
                    if (drop.ResID == CoinID.UNIVERSAL_SHARDS_TOKEN_ID)
                    {
                        specialCoinPackets.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, beforeCoinNum));
                    }

                    resType = PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr;
                }
                else if (CoinID.WalletCoins.Contains(drop.ResID))
                {
                    walletChanged = true;
                    resType = PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin;
                }
                else if (itemsById.TryGetValue(drop.ResID, out var item))
                {
                    itemPackets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify([item])));
                    resType = PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem;
                }
                else
                {
                    continue;
                }

                resources.Add(new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
                {
                    FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO { ResID = drop.ResID, ResCnt = drop.ResNum, ResType = (int)resType },
                    OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
                });
            }

            if (resources.Count == 0)
            {
                continue;
            }

            packets.AddRange(itemPackets);
            if (walletChanged)
            {
                packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, new pbSCGetCoinValNtf { CoinInfo = commit.Wallet.BuildCoinInfo() }));
            }

            packets.AddRange(specialCoinPackets);
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = reward.Star == NoStar ? PveChallengeFirstpassReason : PveChallengeStarReason,
                ResourcesList = { resources },
            }));
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)));
        }

        return packets;
    }

    private pbSCMultiGameSettleGainRsp BuildSettleGainRsp(
        LMNRoleInfo role,
        BattleResult result,
        SettlementPlan plan,
        BattleSettlementCommit commit,
        int levelBefore,
        long now)
    {
        var deskInfo = result.Desk.DeskInfo;
        var sceneMap = gameData.SceneMap.Find(deskInfo.MapId);
        var detail = new pbCOMDT_SETTLE_RESULT_DETAIL
        {
            GameInfo = new pbCOMDT_GAME_INFO
            {
                MapID = deskInfo.MapId,
                GameResult = plan.Won ? WinResult : LoseResult,
                GameType = deskInfo.GameType,
                MapMode = sceneMap?.MapMode ?? 0,
                MapSubMode = sceneMap?.MapSubMode ?? 0,
                MvpScoreDetail = new pbCOMDT_MVP_SCORE_DETAIL
                {
                    MvpScoreDetail =
                    {
                        result.Desk.Players.Select(player => new pbCOMDT_MVP_SCORE_INFO { ObjType = player.PlayerInfo.ObjType, ObjID = player.ObjId }),
                    },
                },
                GameTime = result.GameTime,
                BattleMaskDetail = new pbCOMDT_BATTLE_MASK_DETAIL
                {
                    BattleMaskDetail = { result.Desk.Players.Select(player => new pbCOMDT_BATTLE_MASK_INFO { Uid = player.Uid }) },
                },
            },
            AcntInfo = new pbCOMDT_ACNT_INFO
            {
                Lv = RoleProfile.GetLevel(gameData, role),
                Exp = role.RoleTotalExp,
                IncExp = plan.IncExp,
                LastRoomMemCnt = result.Desk.HumanUids.Length,
                IncLevel = RoleProfile.GetLevel(gameData, role) - levelBefore,
                BaseExp = plan.BaseExp,
                ExtraExpOfNewbie = plan.ExtraExpOfNewbie,
            },
            PokemonInfo = new pbCOMDT_SETTLE_POKEMON_RESULT_INFO
            {
                PokemonId = plan.HeroID,
                PokemonTotalPoint = commit.UsedPokemon?.PokemonTotalPoint ?? 0,
                PokemonSeasonPoint = commit.UsedPokemon?.PokemonSeasonPoint ?? 0,
            },
            GameType = deskInfo.GameType,
            SettleReward = new pbCOMDT_SETTLE_RESULT_REWARD_DETAIL
            {
                ResAwardList =
                {
                    new pbCOMDT_ONE_RESOURCE_AWARD_INFO
                    {
                        ResID = AcntExpResID,
                        ResCnt = plan.IncExp,
                        ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr,
                    },
                },
            },
        };
        if (plan.Route is { } route)
        {
            var starNum = pveChallenge.GetStarNum(role, route);
            detail.SettleResultByGameType = new pbCOMDT_SETTLE_RESULT_BY_GAME_TYPE_UNION
            {
                PveChallengeResult = new pbCOMDT_PVE_CHALLENGE_SETTLE_RESULT
                {
                    StageID = route.StageID,
                    NodeInfo = new pbCOMDT_PVE_CHALLENGE_NODE_INFO
                    {
                        NodeID = route.NodeID,
                        StarNum = starNum,
                        IsUnLock = Unlocked,
                        IsComplete = starNum > NoStar ? Completed : 0,
                        StageID = route.StageID,
                        DiffLevel = route.DifficultyLevel,
                    },
                    TaskInfo = new pbCOMDT_PVE_CHALLENGE_TASK_LIST { TaskList = { plan.StarTasks.Select(task => BuildStarTaskInfo(task, now)) } },
                    RewardInfo = new pbCOMDT_PVE_CHALLENGE_REWARD_LIST
                    {
                        RewardList = { plan.Rewards.Select(reward => new pbCOMDT_PVE_CHALLENGE_REWARD_INFO { RewardID = reward.DropID, RewardStar = reward.Star }) },
                    },
                    CurBattleStarNum = plan.StarNum,
                    IsFirstPass = plan.IsFirstPass ? FirstPass : 0,
                },
            };
        }

        return new pbSCMultiGameSettleGainRsp { Detail = detail };
    }

    private static pbCOMDT_TASK_INFO BuildStarTaskInfo(StarTask task, long now)
    {
        var info = new pbCOMDT_TASK_INFO
        {
            TaskId = task.TaskId,
            TaskSystem = StarTaskSystem,
            TaskProcess = { task.Done ? CompletedOnce : 0 },
            Status = (int)(task.Done ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete : PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved),
            RecvTime = now,
        };
        if (task.Done)
        {
            info.CompleteNum = CompletedOnce;
            info.CompleteTime = now;
        }

        return info;
    }

    private pbSCFightRecordNtf BuildFightRecordNtf(BattleResult result, bool won)
    {
        var deskInfo = result.Desk.DeskInfo;
        var sceneMap = gameData.SceneMap.Find(deskInfo.MapId);
        var summary = result.Gameover.GameoverSummary ?? new pbCSDT_GAMEOVER_SUMMARY();
        var acntInfoByObjId = result.Gameover.AcntInfo
            .GroupBy(info => info.AcntObjID)
            .ToDictionary(group => group.Key, group => group.First());
        return new pbSCFightRecordNtf
        {
            FightRecord = new pbCOMDT_FIGHT_HISTORY_RECORD
            {
                GameType = deskInfo.GameType,
                GameStartTime = result.GameStartTime,
                GameTime = result.GameTime,
                WinCampClient = result.Gameover.BattleParam.WinCampClient,
                MapID = deskInfo.MapId,
                MapMode = sceneMap?.MapMode ?? 0,
                DeskID = result.Desk.DeskID,
                DeskSeq = result.Desk.DeskCrtSeq,
                PlayerInfo = { result.Desk.Players.Select(player => BuildFightPlayerInfo(player, acntInfoByObjId.GetValueOrDefault(player.ObjId), deskInfo.GameType)) },
                Camp1Score = summary.Camp1Score,
                Camp2Score = summary.Camp2Score,
                MapSubMode = sceneMap?.MapSubMode ?? 0,
                RoomType = deskInfo.RoomType,
                Camp1OtherScore = summary.Camp1OtherScore?.Clone(),
                Camp2OtherScore = summary.Camp2OtherScore?.Clone(),
                BossRushInfo = new pbCOMDT_BOSS_RUSH_INFO(),
                TowerDefenseScoreInfo = new pbCOMDT_TOWER_DEFENSE_SCORE_INFO(),
                TowerDefenseResult = new pbCOMDT_TOWER_DEFENSE_SETTLE_RESULT(),
                GameResult = won ? WinResult : LoseResult,
            },
        };
    }

    private static pbCOMDT_FIGHT_HISTORY_PLAYER_INFO BuildFightPlayerInfo(DeskPlayer player, pbCSDT_MULTIGAMEOVER_INFO? acntInfo, int gameType)
    {
        var playerInfo = player.PlayerInfo;
        var pokemonData = acntInfo?.CommonData?.PokemonData;
        var statistic = pokemonData?.PokemonBattleStatistic;
        var baseStatistic = statistic?.BaseStatistic;
        var acnt = playerInfo.Detail?.PlayerOfAcnt;
        var npc = playerInfo.Detail?.PlayerOfNpc;
        var info = new pbCOMDT_FIGHT_HISTORY_PLAYER_INFO
        {
            PlayerCampClient = player.CampClient,
            PlayerName = playerInfo.Name,
            PlayerUid = player.Uid,
            PlayerLv = playerInfo.Level,
            HeroID = player.ChosenHeroID,
            HeroLv = pokemonData?.PokemonDetailInfo?.Level ?? 0,
            TeamFlag = playerInfo.TeamFlag,
            GameType = gameType,
            PlayerFightData = new pbCOMDT_PLAYER_FIGHT_UNION
            {
                CatchFightData = new pbCOMDT_PLAYER_CATCH_FIGHT_DATA
                {
                    Score = statistic?.GetScoreCnt ?? 0,
                    Kill = baseStatistic?.KillCnt ?? 0,
                    Interrupt = statistic?.BreakCnt ?? 0,
                    HurtToEnemy = baseStatistic?.Damage ?? 0,
                    HurtTakenByEnemy = baseStatistic?.DamageTank ?? 0,
                    CureToTeam = baseStatistic?.Cure ?? 0,
                    AssistCnt = baseStatistic?.AssistCnt ?? 0,
                },
            },
            Gender = acnt?.Gender ?? npc?.Gender ?? 0,
            HeadID = acnt?.HeadId ?? npc?.HeadId ?? 0,
            SkillList = { pokemonData?.PokemonDetailInfo?.SkillList.Select(skill => skill.Clone()) ?? [] },
            RoleAvatarInfo = playerInfo.RoleAvatarInfo?.Clone(),
            CarryTrainerItemID = player.ChoiceHero.CarryTrainerItemID,
            YiAosiTechnology = new pbCOMDT_YI_AOSI_TECHNOLOGY_INFO(),
            BeachVolleyballFightData = new pbCOMDT_PLAYER_BEACH_VOLLEYBALL_FIGHT_DATA(),
            HeadUrlInfo = (acnt?.UserPlantInfo ?? npc?.UserPlantInfo)?.Clone(),
        };
        if (player.IsNpc)
        {
            info.BattleFlag = IsAITypeOneBattleFlag;
        }
        else
        {
            info.TmpNameList = playerInfo.TmpNameList?.Clone();
        }

        return info;
    }

    private static GamePacket BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID redDotID, int redDotBadgeNum) => redDotBadgeNum > 0
        ? GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = (int)redDotID, RedDotBadgeNum = redDotBadgeNum } },
            },
        })
        : GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, LobbyNotices.BuildRedDotClearNtf(redDotID));

    private sealed record StarTask(int TaskId, bool Done);

    private sealed record StarReward(int DropID, int Star);

    private sealed record DropGrant(pbDrop_Static Drop, BagGrant Grant);

    private sealed record SettlementPlan(
        bool Won,
        int HeroID,
        int PointPokemonId,
        int BaseExp,
        int ExtraExpOfNewbie,
        int IncExp,
        int ChgExp,
        pbPveChallengeStageRoute? Route,
        IReadOnlyList<StarTask> StarTasks,
        int StarNum,
        int PreviousStarNum,
        RolePveChallengeStar? NewStar,
        IReadOnlyList<StarReward> Rewards,
        IReadOnlyList<DropGrant> ItemGrants,
        IReadOnlyDictionary<int, long> Coins)
    {
        public IEnumerable<BagGrant> Grants => ItemGrants.Select(grant => grant.Grant);

        public bool IsFirstPass => PreviousStarNum == NoStar && StarNum > NoStar;
    }
}
