using System.Collections.Concurrent;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Credit;
using SpiritUnite.Game.Energy;
using SpiritUnite.Game.FightHistory;
using SpiritUnite.Game.Friend;
using SpiritUnite.Game.Guild;
using SpiritUnite.Game.HeldItem;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Match;
using SpiritUnite.Game.Newbie;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.PveChallenge;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Room;
using SpiritUnite.Game.Settings;
using SpiritUnite.Game.StarRoad;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Game.TenGames;
using SpiritUnite.Game.TrainerExpGift;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

public sealed record BattleResult(BattleDesk Desk, DeskPlayer Human, pbCSMultgameGameoverReq Gameover, long GameStartTime, int GameTime, int HangupSeconds = 0)
{
    public const int DrawWinCampClient = 0;

    public bool QuitByExitButton => Gameover.IsExitButtonGameOver != 0;

    public bool IsDraw => !QuitByExitButton && IsDrawReport(Gameover.BattleParam);

    public bool Won => !QuitByExitButton && Gameover.BattleParam.WinCampClient == Human.CampClient;

    public PB_ENUM_ENM_BATTLE_RESULT_TYPE GameResult =>
        Won ? PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeWin
        : IsDraw ? PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeDraw
        : PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeLose;

    public static bool IsDrawReport(pbCOMDT_MULTI_GAME_PARAM param) => param.WinCampClient == DrawWinCampClient;
}

public sealed class BattleSettlement
{
    private const int AcntExpResID = (int)PB_ENUM_ENM_RESOURCES_ID.PbEnmResourcesIdAcntExp;
    private const int HeavenTrainerGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeHeavenTrainer;
    private const int CatchGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeCatch;
    private const int WinMedalType = (int)PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeWin;
    private const int DefeatMedalType = (int)PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeLose;
    private const int NewbieBenefitMedalType = (int)PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeNewbie;
    private const int NewbiePrivilegeRatioBase = 10000;
    private const int SingleHuman = 1;
    private const int MatchTimeMillisecondsPerMinute = 60000;
    private const int NoHeldItem = 0;
    private const int PveChallengeStarReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonPveChallengeStar;
    private const int PveChallengeFirstpassReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonPveChallengeFirstpass;
    private const int PveChallengeRepassReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonPveChallengeRepass;
    private const int PvePrivilegeRewardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonPvePrivilegeReward;
    private const int GotPrivilegeReward = 1;
    private const int StarTaskSystem = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemActivity;
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
    private const int IsAITypeOneBattleFlagBit = (int)PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeNpc - 1;
    private const long IsAITypeOneBattleFlag = 1L << IsAITypeOneBattleFlagBit;
    private const int RankedMatchLimitTimeRedDotBadgeNum = 1;
    private const int EnergyGainRedDotBadgeNum = 2;
    private const int TodaySetNoReminded = 1;
    private const int DaysPerWeek = 7;
    private const int GameTimeoutEnded = 1;

    private static readonly TimeSpan SettleGainRetention = TimeSpan.FromMinutes(5);

    private readonly GameData gameData;
    private readonly RoleInfoStore roles;
    private readonly PveChallengeProgress pveChallenge;
    private readonly PveChallengeManager pveChallengeManager;
    private readonly BagManager bag;
    private readonly HeldItemManager heldItems;
    private readonly PokemonManager pokemon;
    private readonly TaskUpdates taskUpdates;
    private readonly TrainerExpGiftManager trainerExpGiftManager;
    private readonly StarRoadManager starRoad;
    private readonly SettingsManager settings;
    private readonly LobbyNotices notices;
    private readonly NewbieChallengeManager newbieChallenges;
    private readonly NewbieChallengeTaskManager newbieChallengeTaskManager;
    private readonly BattleSettlementRepository repository;
    private readonly ActivitySchedule activities;
    private readonly ActivityManager activityManager;
    private readonly MvpScore mvpScore;
    private readonly RoleRepository roleRepository;
    private readonly LobbyRoles lobbyRoles;
    private readonly CreditManager credit;
    private readonly RankedManager ranked;
    private readonly RankPointMedals rankPointMedals;
    private readonly RoomManager rooms;
    private readonly WarmBattleManager warmBattles;
    private readonly SettleTeamPresence teamPresence;
    private readonly SettleFabulous fabulous;
    private readonly RoomMicTips micTips;
    private readonly FriendPresence presence;
    private readonly ActivityRewardNotices rewardNotices;
    private readonly GuildActivityTasks guildActivityTasks;
    private readonly EventBattleRecorder eventBattles;
    private readonly TenGamesBattleRecorder tenGamesBattles;
    private readonly FriendRepository friends;
    private readonly GVoiceOptions gVoice;
    private readonly IOptionsMonitor<BattleSettlementOptions> options;
    private readonly TimeProvider clock;
    private readonly ILogger<BattleSettlement> logger;
    private readonly IReadOnlyDictionary<int, int> pokemonIdByHeroID;
    private readonly IReadOnlySet<int> eeveelutionPokemonIDs;
    private readonly ILookup<int, pbDrop_Static> dropsById;
    private readonly ConcurrentDictionary<ulong, pbSCMultiGameSettleGainRsp> settleGains = new();
    private static readonly BattleTaskUpdate NoBattleTasks = new([], 0, 0, null);

    public BattleSettlement(
        GameData gameData,
        RoleInfoStore roles,
        PveChallengeProgress pveChallenge,
        PveChallengeManager pveChallengeManager,
        BagManager bag,
        HeldItemManager heldItems,
        PokemonManager pokemon,
        TaskUpdates taskUpdates,
        TrainerExpGiftManager trainerExpGiftManager,
        StarRoadManager starRoad,
        SettingsManager settings,
        LobbyNotices notices,
        NewbieChallengeManager newbieChallenges,
        NewbieChallengeTaskManager newbieChallengeTaskManager,
        BattleSettlementRepository repository,
        ActivitySchedule activities,
        ActivityManager activityManager,
        MvpScore mvpScore,
        RoleRepository roleRepository,
        LobbyRoles lobbyRoles,
        CreditManager credit,
        RankedManager ranked,
        RankPointMedals rankPointMedals,
        RoomManager rooms,
        WarmBattleManager warmBattles,
        SettleTeamPresence teamPresence,
        SettleFabulous fabulous,
        RoomMicTips micTips,
        FriendPresence presence,
        ActivityRewardNotices rewardNotices,
        GuildActivityTasks guildActivityTasks,
        EventBattleRecorder eventBattles,
        TenGamesBattleRecorder tenGamesBattles,
        FriendRepository friends,
        IOptions<GVoiceOptions> gVoiceOptions,
        IOptionsMonitor<BattleSettlementOptions> options,
        TimeProvider clock,
        ILogger<BattleSettlement> logger)
    {
        this.gameData = gameData;
        this.roles = roles;
        this.pveChallenge = pveChallenge;
        this.pveChallengeManager = pveChallengeManager;
        this.bag = bag;
        this.heldItems = heldItems;
        this.pokemon = pokemon;
        this.taskUpdates = taskUpdates;
        this.trainerExpGiftManager = trainerExpGiftManager;
        this.starRoad = starRoad;
        this.settings = settings;
        this.notices = notices;
        this.newbieChallenges = newbieChallenges;
        this.newbieChallengeTaskManager = newbieChallengeTaskManager;
        this.repository = repository;
        this.activities = activities;
        this.activityManager = activityManager;
        this.mvpScore = mvpScore;
        this.roleRepository = roleRepository;
        this.lobbyRoles = lobbyRoles;
        this.credit = credit;
        this.ranked = ranked;
        this.rankPointMedals = rankPointMedals;
        this.rooms = rooms;
        this.warmBattles = warmBattles;
        this.teamPresence = teamPresence;
        this.fabulous = fabulous;
        this.micTips = micTips;
        this.presence = presence;
        this.rewardNotices = rewardNotices;
        this.guildActivityTasks = guildActivityTasks;
        this.eventBattles = eventBattles;
        this.tenGamesBattles = tenGamesBattles;
        this.friends = friends;
        gVoice = gVoiceOptions.Value;
        this.options = options;
        this.clock = clock;
        this.logger = logger;
        pokemonIdByHeroID = gameData.PokemonBase.Rows
            .Where(row => row.MainUnitId != NoPokemon)
            .GroupBy(row => row.MainUnitId)
            .ToDictionary(group => group.Key, group => group.First().PokemonId);
        dropsById = gameData.DropStatic.Rows.OrderBy(drop => drop.SDropSeq).ToLookup(drop => drop.SDropID);
        eeveelutionPokemonIDs = BattleTasks.FindEeveelutionPokemonIDs(gameData.TaskBase.Rows);
    }

    public pbSCMultiGameSettleGainRsp? FindSettleGain(ulong uid) => settleGains.GetValueOrDefault(uid);

    public async Task EndTimedOutGameAsync(BattleDesk desk, ulong uid)
    {
        var gain = new pbSCMultiGameSettleGainRsp { GameTimeoutEnd = GameTimeoutEnded };
        settleGains[uid] = gain;
        _ = ForgetSettleGainAsync(uid, gain);
        var delivered = await lobbyRoles.PushAsync(uid, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultiGameSettleGainRsp, gain));
        logger.LogInformation(
            "Desk {DeskID}: uid {Uid} {Delivery} the game timeout end",
            desk.DeskID,
            uid,
            delivered ? "was sent" : "is offline and was not sent");
    }

    public async Task SettleAsync(BattleResult result, SettlementDelivery delivery, SettlementKey? journalEntry)
    {
        var uid = result.Human.Uid;
        var role = roles.Find(uid) ?? await roles.FindAsync(uid);
        if (role is null)
        {
            throw new InvalidOperationException($"Uid {uid} has no role to settle for desk {result.Desk.DeskID}");
        }

        var route = FindSettledRoute(role, result);
        var node = route is null ? null : new PveChallengeNodeKey(route.StageID, route.NodeID, route.DifficultyLevel);
        var knownStarNum = route is null ? NoStar : pveChallenge.GetStarNum(role, route);
        var utcNow = clock.GetUtcNow();
        var now = utcNow.ToUnixTimeSeconds();
        var levelBefore = RoleProfile.GetLevel(gameData, role);
        var challengeUnlockedBefore = newbieChallenges.IsUnlocked(role);
        var newbieBenefitActive = await IsNewbieBenefitActiveAsync(uid, utcNow);
        var hangupType = FindHangupType(result);
        var mvp = ComputeMvp(result);
        var fightRecordNtf = BuildFightRecordNtf(result, mvp);
        var fightHistory = FightHistoryManager.CreateRecord(
            uid,
            fightRecordNtf.FightRecord,
            result.Gameover.GameoverSummary?.BattleReviewInfo,
            now);
        var weekReward = route is null ? null : await pveChallengeManager.LoadWeekRewardAsync(uid, utcNow);
        var intimacy = await FindBattleIntimacyGainAsync(result);
        SettlementPlan? plan = null;
        IReadOnlyList<BagGrant> heldItemGrants = [];
        var commit = await repository.CommitAsync(uid, journalEntry, node, async storedStarNum =>
        {
            plan = Plan(role, result, route, Math.Max(storedStarNum, knownStarNum), newbieBenefitActive, hangupType, weekReward);
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
                FightHistoryManager.MaxRecordNumPerMapSubMode,
                plan.IsMatch && !IsCustomBattle(result)
                    ? new BattleEnergyGrant(plan.EnergyGains.Sum(gain => gain.EnergyGain), EnergyCenter.WeeklyEnergyFreeCapacity(gameData))
                    : null,
                plan.IsMatch && !IsCustomBattle(result) && result.Desk.DeskInfo.ManMachine != CpuMatch.ManMachine
                    ? BuildCountedBattle(result, plan, mvp, utcNow)
                    : null,
                BuildCountedGame(uid, result, plan))
            {
                PveChallengeWeek = plan.NewWeek,
                Intimacy = intimacy,
                PveBonus = node is null ? null : new PveSettlementEligibility(
                    result.Desk.SettlementID.ToString("N"),
                    result.GameStartTime,
                    node,
                    plan.WeekReward is { IsPrivilegeActive: true } && plan.RefreshDrops.Count > 0 && !plan.IsGetPrivilegeReward
                        ? PveBonusClaimState.Available : PveBonusClaimState.Unavailable,
                    plan.RefreshDrops),
            };
        }, now);
        if (commit is null || plan is null)
        {
            throw new InvalidOperationException($"Settlement of uid {uid} for desk {result.Desk.DeskID} was not committed");
        }

        await warmBattles.LoadAsync(uid);

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
            result.QuitByExitButton ? "quit" : result.IsDraw ? "draw" : plan.Won ? "win" : "loss",
            plan.ChgExp,
            plan.StarNum,
            result.Desk.PveChallengeSelectNode?.NodeID,
            heldItemGrants.Count + plan.ItemGrants.Count);
        var rankedSettlement = IsRankedMatch(result)
            ? await ranked.SettleAsync(uid, new RankedBattle(result.Won, result.IsDraw, rankPointMedals.Award(BuildRankMedalPlayers(result), result.Human.ObjId)))
            : null;
        var isTodaySetNoReminded = !plan.IsMatch ? 0 : rankedSettlement is null ? TodaySetNoReminded : await ranked.TakeIsTodaySetNoRemindedAsync(uid);
        var gain = BuildSettleGainRsp(role, result, plan, commit, levelBefore, mvp, rankedSettlement, isTodaySetNoReminded, now);
        if (await tenGamesBattles.RecordBattleAsync(BuildTenGamesBattle(result), uid) is { } tenGamesScore)
        {
            gain.Detail.AcntInfo.IncTenGamesScore = tenGamesScore.IncTenGamesScore;
            gain.Detail.AcntInfo.TotalTenGamesScore = tenGamesScore.TotalTenGamesScore;
        }

        if (delivery == SettlementDelivery.Live)
        {
            settleGains[uid] = gain;
            _ = ForgetSettleGainAsync(uid, gain);
            fabulous.Open(result.Desk, uid);
        }

        var battleTasks = IsCustomBattle(result) ? NoBattleTasks : await RecordBattleTasksAsync(role, result, plan, now);
        if (!IsCustomBattle(result))
        {
            await guildActivityTasks.RecordBattleAsync(uid, BuildGuildBattle(result, mvp));
        }

        var eventBattlePushes = IsCustomBattle(result)
            ? []
            : await eventBattles.RecordAsync(role, plan.Won, result.Desk.DeskInfo.MapId, pokemonIdByHeroID.GetValueOrDefault(plan.HeroID));

        var creditPointNtf = result.QuitByExitButton ? null
            : hangupType != PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeNone ? await credit.PenalizeHangupAsync(uid, BuildCreditReduceBattle(result), hangupType)
            : await credit.RecoverAfterBattleAsync(uid, result.Desk.DeskInfo.MapId, result.Desk.DeskInfo.ManMachine == CpuMatch.ManMachine);
        if (delivery == SettlementDelivery.Recovered || !lobbyRoles.IsOnline(uid))
        {
            logger.LogInformation("Settlement of uid {Uid} was stored without live delivery ({Delivery})", uid, delivery);
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
            battleTasks,
            creditPointNtf,
            rankedSettlement,
            gain,
            fightRecordNtf);
        if (!await lobbyRoles.PushAsync(uid, packets))
        {
            logger.LogInformation("Settlement of uid {Uid} was stored but not delivered: the push failed", uid);
            return;
        }

        if (eventBattlePushes.Length > 0)
        {
            await lobbyRoles.PushAsync(uid, eventBattlePushes);
        }

        if (intimacy is not null)
        {
            await lobbyRoles.PushAsync(uid, FriendIntimacy.BuildFriendInteractionNtf(gameData, await friends.GetFriendsAsync(uid, intimacy.FriendUids)));
        }

        if (HasSettleRoom(result))
        {
            await EnterSettleRoomAsync(result);
        }
    }

    public async Task SettleJudgeAsync(BattleResult result, SettlementDelivery delivery)
    {
        if (delivery == SettlementDelivery.Recovered)
        {
            return;
        }

        var uid = result.Human.Uid;
        var role = roles.Find(uid) ?? await roles.FindAsync(uid);
        if (role is null)
        {
            throw new InvalidOperationException($"Judge uid {uid} has no role to settle for desk {result.Desk.DeskID}");
        }

        var gameResult = JudgeGameResultOf(result);
        var gain = new pbSCMultiGameSettleGainRsp
        {
            Detail = new pbCOMDT_SETTLE_RESULT_DETAIL
            {
                GameInfo = BuildGameInfo(result, ComputeMvp(result), gameResult),
                AcntInfo = new pbCOMDT_ACNT_INFO
                {
                    Lv = RoleProfile.GetLevel(gameData, role),
                    Exp = role.RoleTotalExp,
                    LastRoomMemCnt = result.Desk.Humans.Count(),
                },
                PokemonInfo = new pbCOMDT_SETTLE_POKEMON_RESULT_INFO(),
                GameType = result.Desk.DeskInfo.GameType,
                SettleReward = new pbCOMDT_SETTLE_RESULT_REWARD_DETAIL(),
            },
        };
        settleGains[uid] = gain;
        _ = ForgetSettleGainAsync(uid, gain);
        fabulous.Open(result.Desk, uid);
        logger.LogInformation(
            "Desk {DeskID}: judge uid {Uid} settled with camp {WinCampClient} winning ({GameResult} for the first camp); judges earn nothing",
            result.Desk.DeskID,
            uid,
            result.Gameover.BattleParam.WinCampClient,
            gameResult);
        GamePacket[] packets =
        [
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRoomSettleOneKeyTeamInfoNtf, SettleTeamPresence.BuildInfoNtf(BuildSettleTeamMembers(result), new HashSet<ulong>())),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultiGameSettleGainRsp, gain),
        ];
        if (!lobbyRoles.IsOnline(uid) || !await lobbyRoles.PushAsync(uid, packets))
        {
            logger.LogInformation("Settlement of judge uid {Uid} was stored but not delivered: the role is offline", uid);
            return;
        }

        if (HasSettleRoom(result))
        {
            await EnterSettleRoomAsync(result);
        }
    }

    private async Task<BattleTaskUpdate> RecordBattleTasksAsync(LMNRoleInfo role, BattleResult result, SettlementPlan plan, long now)
    {
        var battleTaskEvent = await BuildBattleTaskEventAsync(result, plan);
        var newbieChallengeProgress = await newbieChallengeTaskManager.RecordBattleAsync(role, battleTaskEvent);
        return await taskUpdates.RecordBattleAsync(
            role,
            battleTaskEvent,
            [.. plan.StarTasks.Select(task => BuildStarTaskInfo(task, now)), .. newbieChallengeProgress]);
    }

    private static CreditReduceBattle BuildCreditReduceBattle(BattleResult result) => new(
        result.Desk.DeskInfo.MapId,
        result.GameStartTime,
        result.Human.ChosenHeroID,
        CreditReduceBattle.BattleIDOf(result.Desk.DeskID, result.Desk.DeskCrtSeq, result.Desk.RelayEntity));

    private static TenGamesBattle BuildTenGamesBattle(BattleResult result) => new(
        result.Desk.DeskID,
        result.Desk.DeskCrtSeq,
        result.Desk.DeskInfo.MapId,
        [.. result.Desk.Humans.Select(human => new TenGamesBattlePlayer(human.Uid, human.CampClient))],
        result.Gameover.BattleParam.WinCampClient,
        result.IsDraw);

    private static bool IsCustomBattle(BattleResult result) =>
        result.Desk.DeskInfo.RoomType == (int)PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeNormal;

    private async Task EnterSettleRoomAsync(BattleResult result)
    {
        var uid = result.Human.Uid;
        var outcome = rooms.EnterSettleRoom(uid);
        if (!outcome.Succeeded || rooms.FindRoomOf(uid) is not { } room)
        {
            logger.LogInformation("Uid {Uid} gets no post-game room after desk {DeskID}: {Result}", uid, result.Desk.DeskID, outcome.Result);
            return;
        }

        GamePacket[] own =
        [
            .. outcome.Replies.Select(reply => GamePacket.Create(reply.MsgID, reply.Message)),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRoomRttNtf, RoomNotices.RoomRtt(gVoice, lobbyRoles.Rtt(uid))),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRoomMicTipsInfoNtf, micTips.WithShowMicTipsCnt(uid, IsRankedMatch(result) ? ranked.BuildMicTipsInfoNtf(uid) : new pbSCRoomMicTipsInfoNtf())),
            .. outcome.Pushes.Where(push => push.Uid == uid).Select(push => GamePacket.Create(push.Message.MsgID, push.Message.Message)),
        ];
        await lobbyRoles.PushAsync(uid, own);
        foreach (var group in outcome.Pushes.Where(push => push.Uid != uid).GroupBy(push => push.Uid))
        {
            await lobbyRoles.PushAsync(group.Key, [.. group.Select(push => GamePacket.Create(push.Message.MsgID, push.Message.Message))]);
        }

        await presence.SetGameStateAsync(uid, PB_ENUM_E_ACNT_GAMESTATE.PbEacntGameroom, clock.GetUtcNow().ToUnixTimeSeconds());
        logger.LogInformation("Uid {Uid} entered post-game room {RoomID} after desk {DeskID}", uid, room.RoomID, result.Desk.DeskID);
        teamPresence.Follow(uid, room.RoomID, BuildSettleTeamMembers(result));
    }

    private static SettleTeamMember[] BuildSettleTeamMembers(BattleResult result) =>
    [
        .. result.Desk.CampOf(result.Human.CampClient)
            .Where(player => player != result.Human)
            .Where(player => player.IsNpc || player.PlayerInfo.TeamFlag == result.Human.PlayerInfo.TeamFlag)
            .Select(player => new SettleTeamMember(player.Uid, player.ChosenHeroID, player.IsNpc)),
    ];

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

    private PB_ENUM_ENM_HANGUP_TYPE FindHangupType(BattleResult result)
    {
        if (result.QuitByExitButton || !IsMatch(result) || IsCustomBattle(result))
        {
            return PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeNone;
        }

        var hangupType = credit.FindHangupType(result.Desk.DeskInfo.MapId, result.HangupSeconds);
        if (hangupType != PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeNone)
        {
            logger.LogInformation("Desk {DeskID}: uid {Uid} idled {HangupSeconds} s ({HangupType})", result.Desk.DeskID, result.Human.Uid, result.HangupSeconds, hangupType);
        }

        return hangupType;
    }

    private SettlementPlan Plan(
        LMNRoleInfo role,
        BattleResult result,
        pbPveChallengeStageRoute? route,
        int previousStarNum,
        bool newbieBenefitActive,
        PB_ENUM_ENM_HANGUP_TYPE hangupType,
        PveChallengeWeekReward? weekReward)
    {
        var won = result.Won;
        var isMatch = IsMatch(result);
        var acntInfo = result.Gameover.AcntInfo.First(info => info.AcntUid == result.Human.Uid && info.ObjType == AcntObjType);
        var pokemonLevel = acntInfo.CommonData?.PokemonData?.PokemonDetailInfo?.Level ?? 0;
        var heroID = result.Human.ChosenHeroID;
        var pokemonId = pokemonIdByHeroID.GetValueOrDefault(heroID);
        var isCustomBattle = IsCustomBattle(result);
        var matchMinutes = isMatch && !isCustomBattle ? FindMatchMinutes(result) : 0;
        var baseExp = isCustomBattle ? 0
            : isMatch ? credit.ReduceHangupIncome(matchMinutes * (won ? options.CurrentValue.MatchWinExpPerMinute : options.CurrentValue.MatchLoseExpPerMinute), hangupType)
            : won ? options.CurrentValue.PveChallengeWinBaseExp : options.CurrentValue.PveChallengeLoseBaseExp;
        var extraExpOfNewbie = newbieBenefitActive ? NewbiePrivilegeShareOf(baseExp, PB_ENUM_RES_RESGLOBAL.PbResglobalNewbiePrivilegeAcntExp) : 0;
        var starTasks = route is null ? [] : route.TaskID.Where(taskId => taskId != NoDrop)
            .Select(taskId => new StarTask(taskId, won && IsStarTaskDone(taskId, won, pokemonLevel)))
            .ToArray();
        var starNum = won ? starTasks.Count(task => task.Done) : NoStar;
        var rewards = new List<StarReward>();
        if (route is not null)
        {
            if (previousStarNum == NoStar && starNum > NoStar)
            {
                rewards.AddRange(route.CompleteDrops.Where(dropId => dropId != NoDrop).Select(dropId => new StarReward(dropId, NoStar, PveChallengeFirstpassReason)));
            }

            for (var star = Math.Max(previousStarNum + 1, FirstStar); star <= starNum; star++)
            {
                rewards.AddRange(StarDrops(route, star).Where(dropId => dropId != NoDrop).Select(dropId => new StarReward(dropId, star, PveChallengeStarReason)));
            }
        }

        var refreshDrops = route?.RefreshDrops.Where(dropId => dropId != NoDrop).ToArray() ?? [];
        var getsRefreshReward = route is not null && won && previousStarNum > NoStar && refreshDrops.Length > 0
            && weekReward is { CanGetRefreshReward: true };
        var isGetPrivilegeReward = getsRefreshReward && weekReward!.CanGetPrivilegeReward;
        if (getsRefreshReward)
        {
            rewards.AddRange(refreshDrops.Select(dropId => new StarReward(dropId, NoStar, PveChallengeRepassReason)));
        }

        if (isGetPrivilegeReward)
        {
            rewards.AddRange(refreshDrops.Select(dropId => new StarReward(dropId, NoStar, PvePrivilegeRewardReason)));
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
            coins,
            isMatch,
            isMatch && !isCustomBattle ? BuildEnergyGains(won, IsRankedMatch(result), matchMinutes, newbieBenefitActive, hangupType) : [])
        {
            WeekReward = weekReward,
            NewWeek = getsRefreshReward
                ? weekReward!.Week with
                {
                    WeekRewardGetNum = weekReward.Week.WeekRewardGetNum + 1,
                    PrivilegeRewardGetNum = weekReward.Week.PrivilegeRewardGetNum + (isGetPrivilegeReward ? 1 : 0),
                }
                : null,
            IsGetPrivilegeReward = isGetPrivilegeReward,
            RefreshDrops = getsRefreshReward ? refreshDrops : [],
        };
    }

    private UsedPokemonBattle BuildCountedBattle(BattleResult result, SettlementPlan plan, MvpResult mvp, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + DaysPerWeek - (int)DayOfWeek.Monday) % DaysPerWeek));
        return new UsedPokemonBattle(
            plan.Won,
            mvp.WinMvpObjID == result.Human.ObjId || mvp.LoseMvpObjID == result.Human.ObjId,
            gameData.SceneMap.Find(result.Desk.DeskInfo.MapId)?.MapSubMode ?? 0,
            RankedMatch.FindCurrentSeason(gameData, now).Season.SeasonId,
            weekStart.ToUnixTimeSeconds(),
            now.ToUnixTimeSeconds());
    }

    private WarmBattleGame? BuildCountedGame(ulong uid, BattleResult result, SettlementPlan plan)
    {
        var mapSubMode = gameData.SceneMap.Find(result.Desk.DeskInfo.MapId)?.MapSubMode ?? 0;
        return plan.IsMatch
            && !IsCustomBattle(result)
            && result.Desk.DeskInfo.ManMachine != CpuMatch.ManMachine
            && WarmBattleManager.IsWarmBattleSubMode(mapSubMode)
                ? new WarmBattleGame(
                    mapSubMode,
                    result.Desk.DeskInfo.IsWarmBattle == CpuMatch.WarmBattle
                        && result.Desk.DeskInfo.WarmBattleType == MatchGroup.Rookie
                        && !warmBattles.IsWithinFirstNGames(uid, mapSubMode),
                    result.Desk.Players.Count(player => !player.IsNpc) == SingleHuman,
                    WarmBattleResultOf(result))
                : null;
    }

    private static WarmBattleGameResult WarmBattleResultOf(BattleResult result) => result.GameResult switch
    {
        PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeWin => WarmBattleGameResult.Won,
        PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeDraw => WarmBattleGameResult.Drawn,
        _ => WarmBattleGameResult.Lost,
    };

    private static bool HasSettleRoom(BattleResult result) => result.Desk.PveChallengeSelectNode is null;

    private bool IsMatch(BattleResult result) =>
        result.Desk.PveChallengeSelectNode is null && gameData.SceneMap.Find(result.Desk.DeskInfo.MapId)?.GameType == CatchGameType;

    private int FindMatchMinutes(BattleResult result)
    {
        var mapId = result.Desk.DeskInfo.MapId;
        if (gameData.SceneMap.Find(mapId) is not { } sceneMap || gameData.MapBase.Find(sceneMap.MapBaseId) is not { } mapBase)
        {
            logger.LogWarning("Desk {DeskID}: map {MapId} has no MapBase row; the match earns no base exp", result.Desk.DeskID, mapId);
            return 0;
        }

        return mapBase.MatchTime / MatchTimeMillisecondsPerMinute;
    }

    private pbCOMDT_SETTLE_GAIN_ENERGY_DETAIL[] BuildEnergyGains(bool won, bool isRankedMatch, int matchMinutes, bool newbieBenefitActive, PB_ENUM_ENM_HANGUP_TYPE hangupType)
    {
        var energyGain = credit.ReduceHangupIncome((won, isRankedMatch) switch
        {
            (true, true) => options.CurrentValue.RankedMatchWinEnergy,
            (false, true) => options.CurrentValue.RankedMatchLoseEnergy,
            (true, false) => options.CurrentValue.MatchWinEnergy,
            (false, false) => matchMinutes * options.CurrentValue.MatchLoseEnergyPerMinute,
        }, hangupType);
        if (energyGain <= 0)
        {
            return [];
        }

        var resultGain = new pbCOMDT_SETTLE_GAIN_ENERGY_DETAIL { MedalType = won ? WinMedalType : DefeatMedalType, EnergyGain = energyGain };
        return newbieBenefitActive
            ?
            [
                resultGain,
                new pbCOMDT_SETTLE_GAIN_ENERGY_DETAIL
                {
                    MedalType = NewbieBenefitMedalType,
                    EnergyGain = NewbiePrivilegeShareOf(energyGain, PB_ENUM_RES_RESGLOBAL.PbResglobalNewbiePrivilegeEnergy),
                },
            ]
            : [resultGain];
    }

    private int NewbiePrivilegeShareOf(int amount, PB_ENUM_RES_RESGLOBAL privilegeGlobalId) =>
        amount * gameData.GetGlobalIntData(privilegeGlobalId) / NewbiePrivilegeRatioBase;

    private async Task<BattleTaskEvent> BuildBattleTaskEventAsync(BattleResult result, SettlementPlan plan)
    {
        var statistic = result.Gameover.AcntInfo
            .FirstOrDefault(info => info.AcntUid == result.Human.Uid && info.ObjType == AcntObjType)?
            .CommonData?.PokemonData?.PokemonBattleStatistic;
        var sceneInteraction = statistic?.BaseStatistic?.SceneInteractionInfo;
        var joinKills = statistic?.KillMonster?.JoinKilltMonsterDetail
            .Select(kill => new BattleMonsterKill(
                kill.MonsterID,
                gameData.UnitBase.Find(kill.MonsterID)?.MonsterClassType ?? NoMonsterClass,
                Math.Max(0, kill.KillCnt)))
            .ToArray() ?? [];
        var occupationType = pokemonIdByHeroID.TryGetValue(plan.HeroID, out var pokemonId)
            ? gameData.PokemonBase.Find(pokemonId)?.OccupationType ?? NoOccupation
            : NoOccupation;
        var heldItemIDs = result.Gameover.AcntInfo
            .FirstOrDefault(info => info.AcntUid == result.Human.Uid && info.ObjType == AcntObjType)?
            .CommonData?.PokemonData?.HeldItemDesignInfo?.HeldItemID.ToArray() ?? [];
        return new BattleTaskEvent(
            plan.Won,
            result.Desk.DeskInfo.MapId,
            gameData.SceneMap.Find(result.Desk.DeskInfo.MapId)?.MapSubMode ?? 0,
            occupationType,
            Math.Max(0, statistic?.BreakCnt ?? 0),
            BattleTasks.AddCounts(sceneInteraction?.PickRecoverBerry ?? 0, sceneInteraction?.PickMoveBerry ?? 0),
            heldItemIDs.Any(heldItemID => heldItemID != NoHeldItem),
            joinKills)
        {
            IsManMachine = result.Desk.DeskInfo.ManMachine == CpuMatch.ManMachine,
            PokemonID = pokemonId,
            IsEeveelution = eeveelutionPokemonIDs.Contains(pokemonId),
            CampPokemonIDs = [.. result.Desk.CampOf(result.Human.CampClient).Select(player => PokemonIdOf(player.ChosenHeroID))],
            PlayedWithFriend = await PlayedWithFriendAsync(result),
        };
    }

    private int PokemonIdOf(int heroID) => pokemonIdByHeroID.GetValueOrDefault(heroID, NoPokemon);

    private async Task<bool> PlayedWithFriendAsync(BattleResult result) => (await FindTeammateFriendUidsAsync(result)).Length > 0;

    private async Task<ulong[]> FindTeammateFriendUidsAsync(BattleResult result)
    {
        var teammateUids = result.Desk.CampOf(result.Human.CampClient)
            .Where(player => !player.IsNpc && player.Uid != result.Human.Uid)
            .Select(player => player.Uid)
            .ToHashSet();
        return teammateUids.Count == 0
            ? []
            : [.. (await friends.GetFriendsAsync(result.Human.Uid)).Select(friend => friend.FriendUid).Where(teammateUids.Contains)];
    }

    private async Task<BattleIntimacyGain?> FindBattleIntimacyGainAsync(BattleResult result)
    {
        if (!IsMatch(result) || FriendIntimacy.FindRuleValue(gameData, PB_ENUM_RES_INTIMACY_RULE_ID.PbResIntimacyRuleFightId) is not { } fightIntimacy)
        {
            return null;
        }

        var friendUids = await FindTeammateFriendUidsAsync(result);
        return friendUids.Length == 0 ? null : new BattleIntimacyGain(friendUids, fightIntimacy);
    }

    private static GuildBattle BuildGuildBattle(BattleResult result, MvpResult mvp)
    {
        var statistic = result.Gameover.AcntInfo
            .FirstOrDefault(info => info.AcntUid == result.Human.Uid && info.ObjType == AcntObjType)?
            .CommonData?.PokemonData?.PokemonBattleStatistic;
        var baseStatistic = statistic?.BaseStatistic;
        return new GuildBattle(
            result.Won,
            mvp.WinMvpObjID == result.Human.ObjId || mvp.LoseMvpObjID == result.Human.ObjId,
            baseStatistic?.KillCnt ?? 0,
            baseStatistic?.AssistCnt ?? 0,
            statistic?.ScoreCnt ?? 0,
            statistic?.GetScoreCnt ?? 0,
            baseStatistic?.UseUltimateSkillCnt ?? 0,
            [.. result.Desk.CampOf(result.Human.CampClient).Where(player => !player.IsNpc && player != result.Human).Select(player => player.Uid)]);
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
        BattleTaskUpdate battleTasks,
        pbSCCreditPointNtf? creditPointNtf,
        RankedSettlement? rankedSettlement,
        pbSCMultiGameSettleGainRsp gain,
        pbSCFightRecordNtf fightRecordNtf)
    {
        var packets = new List<GamePacket>();
        if (commit.UsedPokemon is { } used)
        {
            packets.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidUpdateUsedPokemonNtf,
                await pokemon.BuildUpdateUsedPokemonNtfAsync(role.GameUlongUID, used)));
        }

        var levelAfter = RoleProfile.GetLevel(gameData, role);
        var rankedMatchUnlockLevel = RankedMatch.GetRankedMatchUnlockLevel(gameData, notices);
        if (levelBefore < rankedMatchUnlockLevel && levelAfter >= rankedMatchUnlockLevel)
        {
            packets.Add(BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdRankedMatchLimitTime, RankedMatchLimitTimeRedDotBadgeNum));
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

        var levelChanged = levelAfter != levelBefore;
        if (levelChanged)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildTrainerLevelUpdateTaskInfoNtfAsync(role)));
            if (notices.BuildLevelUpShopNewSaleInfoNtf(levelBefore, levelAfter) is { } newSale)
            {
                packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidShopNewSaleInfoNtf, newSale));
            }
        }

        var challengeUnlock = !challengeUnlockedBefore && newbieChallenges.IsUnlocked(role)
            ? await newbieChallengeTaskManager.BuildUnlockAsync(role)
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
            SeasonPoint = TrainerExpGiftManager.GetSeasonPoint(role),
            ReachMaxSeasonGift = trainerExpGiftManager.GetReachMaxSeasonGift(role),
        }));
        packets.Add(BuildRedDotReplaceNtf(
            PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdTrainerLavelGift,
            trainerExpGiftManager.CountAbleToGet(role) > 0 ? HasTrainerLevelGift : 0));
        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidTrainerExpNextGiftInfoNtf, trainerExpGiftManager.BuildTrainerExpNextGiftInfoNtf(role)));
        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)));
        if (rankedSettlement is not null)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRankedMatchInfoNtf, rankedSettlement.InfoNtf));
        }

        if (commit.Energy is { } energy)
        {
            if (energy.EnergyGain > 0)
            {
                packets.Add(BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdEnergyDraw, EnergyGainRedDotBadgeNum));
            }

            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidEnergyInfoNtf, EnergyCenter.BuildEnergyInfoNtf(energy.After)));
        }

        packets.AddRange(await BuildBattleTaskPacketsAsync(role, battleTasks));
        packets.AddRange(await BuildStarDropPacketsAsync(role, plan, commit, itemsById));
        if (plan.Route is { } route && (plan.NewStar is not null || plan.NewWeek is not null))
        {
            if (plan.IsFirstPass)
            {
                packets.AddRange(Enumerable.Repeat(
                    BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdPveChallengeNormal, NewNodeRedDotBadgeNum),
                    route.NextUnLock.Count(nodeID => nodeID != NoNextUnLock)));
            }

            var completionNtf = pveChallenge.BuildCompletionNtf(role, route);
            completionNtf.WeekRewardGetNum = plan.SettledWeekRewardGetNum;
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeStageInfoNtf, completionNtf));
        }

        if (creditPointNtf is not null)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidCreditPointNtf, creditPointNtf));
        }

        if (HasSettleRoom(result))
        {
            packets.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidRoomSettleOneKeyTeamInfoNtf,
                SettleTeamPresence.BuildInfoNtf(BuildSettleTeamMembers(result), new HashSet<ulong>())));
        }

        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultiGameSettleGainRsp, gain));
        if (plan.IsMatch && !IsCustomBattle(result))
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRoomDailyActivityRewardNtf, new pbSCRoomDailyActivityRewardNtf
            {
                DailyActivityInfo = await RoomDailyActivity.BuildDailyActivityInfoAsync(activities, activityManager, role, clock.GetUtcNow()),
            }));
        }

        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidFightRecordNtf, fightRecordNtf));
        return [.. packets];
    }

    private async Task<IEnumerable<GamePacket>> BuildBattleTaskPacketsAsync(LMNRoleInfo role, BattleTaskUpdate battleTasks)
    {
        var packets = new List<GamePacket>();
        if (battleTasks.AutoClaim is { } autoClaim)
        {
            packets.Add(TaskUpdates.BuildUpdateTaskInfoNtf(autoClaim.UpdatedTasks));
            packets.AddRange(await rewardNotices.BuildAsync(role, autoClaim.Rewards));
            if (autoClaim.ChainedTasks.Length > 0)
            {
                packets.Add(TaskUpdates.BuildUpdateTaskInfoNtf(autoClaim.ChainedTasks));
                packets.AddRange(await rewardNotices.BuildAsync(role, autoClaim.ChainedRewards));
            }
        }

        if (battleTasks.UpdatedTasks.Length > 0)
        {
            packets.Add(TaskUpdates.BuildUpdateTaskInfoNtf(battleTasks.UpdatedTasks));
        }

        packets.AddRange(battleTasks.BuildProgressRedDots());
        if (battleTasks.AutoClaim is { } claimed)
        {
            var ownershipTasks = (await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)).TaskInfoList;
            packets.Add(TaskUpdates.BuildUpdateTaskInfoNtf([.. claimed.ProgressedTasks, .. ownershipTasks]));
        }

        return packets;
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
            var changedWalletCoins = new HashSet<int>();
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
                    changedWalletCoins.Add(drop.ResID);
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
            packets.AddRange(commit.Wallet.BuildCoinValPushes(changedWalletCoins));

            packets.AddRange(specialCoinPackets);
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = reward.AwardReason,
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
        MvpResult mvp,
        RankedSettlement? rankedSettlement,
        int isTodaySetNoReminded,
        long now)
    {
        var deskInfo = result.Desk.DeskInfo;
        var detail = new pbCOMDT_SETTLE_RESULT_DETAIL
        {
            GameInfo = BuildGameInfo(result, mvp, result.GameResult),
            AcntInfo = new pbCOMDT_ACNT_INFO
            {
                Lv = RoleProfile.GetLevel(gameData, role),
                Exp = role.RoleTotalExp,
                IncExp = plan.IncExp,
                LastRoomMemCnt = result.Desk.Humans.Count(),
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
            DrawEnergy = commit.Energy is { } energy ? BuildDrawEnergy(plan, energy) : null,
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
            IsTodaySetNoReminded = isTodaySetNoReminded,
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
                    WeekRewardGetNum = plan.SettledWeekRewardGetNum,
                    CurBattleStarNum = plan.StarNum,
                    PrivilegeRewardGetNum = (plan.NewWeek ?? plan.WeekReward?.Week)?.PrivilegeRewardGetNum ?? 0,
                    IsGetPrivilegeReward = plan.IsGetPrivilegeReward ? GotPrivilegeReward : 0,
                    IsFirstPass = plan.IsFirstPass ? FirstPass : 0,
                },
            };
        }

        if (rankedSettlement is not null)
        {
            detail.SettleResultByGameType = new pbCOMDT_SETTLE_RESULT_BY_GAME_TYPE_UNION
            {
                CatchSettleResult = rankedSettlement.CatchSettleResult,
            };
        }

        return new pbSCMultiGameSettleGainRsp { Detail = detail };
    }

    private pbCOMDT_GAME_INFO BuildGameInfo(BattleResult result, MvpResult mvp, PB_ENUM_ENM_BATTLE_RESULT_TYPE gameResult)
    {
        var deskInfo = result.Desk.DeskInfo;
        var sceneMap = gameData.SceneMap.Find(deskInfo.MapId);
        return new pbCOMDT_GAME_INFO
        {
            MapID = deskInfo.MapId,
            GameResult = (int)gameResult,
            GameType = deskInfo.GameType,
            MapMode = sceneMap?.MapMode ?? 0,
            MapSubMode = sceneMap?.MapSubMode ?? 0,
            MvpScoreDetail = new pbCOMDT_MVP_SCORE_DETAIL
            {
                MvpScoreDetail =
                {
                    result.Desk.Players.Select(player => new pbCOMDT_MVP_SCORE_INFO
                    {
                        ObjType = player.PlayerInfo.ObjType,
                        ObjID = player.ObjId,
                        MvpScoreTTH = mvp.MvpScoreTTHByObjID.GetValueOrDefault(player.ObjId),
                    }),
                },
            },
            GameTime = result.GameTime,
            WinMvpObjID = mvp.WinMvpObjID,
            LoseMvpObjID = mvp.LoseMvpObjID,
            BattleMaskDetail = BuildBattleMaskDetail(result.Desk, mvp),
        };
    }

    private static PB_ENUM_ENM_BATTLE_RESULT_TYPE JudgeGameResultOf(BattleResult result) =>
        result.IsDraw ? PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeDraw
        : result.Gameover.BattleParam.WinCampClient == result.Desk.Players.Min(player => player.CampClient) ? PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeWin
        : PB_ENUM_ENM_BATTLE_RESULT_TYPE.PbEnmBattleResultTypeLose;

    private bool IsRankedMatch(BattleResult result) =>
        IsMatch(result) && gameData.SceneMap.Find(result.Desk.DeskInfo.MapId)?.MapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank;

    private MvpResult ComputeMvp(BattleResult result) =>
        mvpScore.Compute(BuildMvpPlayers(result), result.Gameover.BattleParam.WinCampClient);

    private static RankMedalPlayer[] BuildRankMedalPlayers(BattleResult result)
    {
        var statisticByObjId = result.Gameover.AcntInfo
            .GroupBy(info => info.AcntObjID)
            .ToDictionary(group => group.Key, group => group.First().CommonData?.PokemonData?.PokemonBattleStatistic);
        return
        [
            .. result.Desk.Players.Select(player => new RankMedalPlayer(
                player.ObjId,
                player.CampClient,
                statisticByObjId.GetValueOrDefault(player.ObjId) ?? new pbCOMDT_POKEMON_BATTLE_STATISTIC())),
        ];
    }

    private MvpPlayer[] BuildMvpPlayers(BattleResult result)
    {
        var acntInfoByObjId = result.Gameover.AcntInfo
            .GroupBy(info => info.AcntObjID)
            .ToDictionary(group => group.Key, group => group.First());
        return
        [
            .. result.Desk.Players.Select(player => new MvpPlayer(
                player.PlayerInfo.ObjType,
                player.ObjId,
                player.CampClient,
                pokemonIdByHeroID.TryGetValue(player.ChosenHeroID, out var pokemonId)
                    ? gameData.PokemonBase.Find(pokemonId)?.OccupationType ?? NoOccupation
                    : NoOccupation,
                MvpStats.From(acntInfoByObjId.GetValueOrDefault(player.ObjId)))),
        ];
    }

    private static long BattleFlagOf(DeskPlayer player, MvpResult mvp) =>
        (player.IsNpc ? IsAITypeOneBattleFlag : 0) | mvp.BattleFlagOf(player.ObjId);

    private static pbCOMDT_BATTLE_MASK_DETAIL BuildBattleMaskDetail(BattleDesk desk, MvpResult mvp) => desk.DeskInfo.ManMachine == CpuMatch.ManMachine
        ? new pbCOMDT_BATTLE_MASK_DETAIL()
        : new pbCOMDT_BATTLE_MASK_DETAIL
        {
            BattleMaskDetail =
            {
                desk.Players.Select(player => new pbCOMDT_BATTLE_MASK_INFO
                {
                    Uid = player.Uid,
                    BattleMask = BattleFlagOf(player, mvp),
                }),
            },
        };

    private static pbCOMDT_SETTLE_ENERGY_DETAIL BuildDrawEnergy(SettlementPlan plan, BattleEnergyChange energy)
    {
        var remaining = energy.EnergyGain;
        var drawEnergy = new pbCOMDT_SETTLE_ENERGY_DETAIL { OldEnergyValue = energy.Before.Energy, EnergyGain = energy.EnergyGain };
        foreach (var gain in plan.EnergyGains)
        {
            var received = Math.Min(gain.EnergyGain, remaining);
            remaining -= received;
            if (received > 0)
            {
                drawEnergy.EnergyGainList.Add(new pbCOMDT_SETTLE_GAIN_ENERGY_DETAIL { MedalType = gain.MedalType, EnergyGain = received });
            }
        }

        return drawEnergy;
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

    private pbSCFightRecordNtf BuildFightRecordNtf(BattleResult result, MvpResult mvp)
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
                PlayerInfo = { result.Desk.Players.Select(player => BuildFightPlayerInfo(player, acntInfoByObjId.GetValueOrDefault(player.ObjId), deskInfo.GameType, mvp)) },
                Camp1Score = summary.Camp1Score,
                Camp2Score = summary.Camp2Score,
                MapSubMode = sceneMap?.MapSubMode ?? 0,
                RoomType = deskInfo.RoomType,
                Camp1OtherScore = summary.Camp1OtherScore?.Clone(),
                Camp2OtherScore = summary.Camp2OtherScore?.Clone(),
                BossRushInfo = new pbCOMDT_BOSS_RUSH_INFO(),
                TowerDefenseScoreInfo = new pbCOMDT_TOWER_DEFENSE_SCORE_INFO(),
                TowerDefenseResult = new pbCOMDT_TOWER_DEFENSE_SETTLE_RESULT(),
                GameResult = (int)result.GameResult,
                IsManMachine = deskInfo.ManMachine,
            },
        };
    }

    private static pbCOMDT_FIGHT_HISTORY_PLAYER_INFO BuildFightPlayerInfo(DeskPlayer player, pbCSDT_MULTIGAMEOVER_INFO? acntInfo, int gameType, MvpResult mvp)
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
            MVPScore = mvp.MvpScoreTTHByObjID.GetValueOrDefault(player.ObjId),
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
        info.BattleFlag = BattleFlagOf(player, mvp);
        if (!player.IsNpc)
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

    private sealed record StarReward(int DropID, int Star, int AwardReason);

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
        IReadOnlyDictionary<int, long> Coins,
        bool IsMatch,
        IReadOnlyList<pbCOMDT_SETTLE_GAIN_ENERGY_DETAIL> EnergyGains)
    {
        public IEnumerable<BagGrant> Grants => ItemGrants.Select(grant => grant.Grant);

        public bool IsFirstPass => PreviousStarNum == NoStar && StarNum > NoStar;

        public PveChallengeWeekReward? WeekReward { get; init; }

        public PveChallengeWeek? NewWeek { get; init; }

        public bool IsGetPrivilegeReward { get; init; }

        public IReadOnlyList<int> RefreshDrops { get; init; } = [];

        public int SettledWeekRewardGetNum => (NewWeek ?? WeekReward?.Week)?.WeekRewardGetNum ?? 0;
    }
}
