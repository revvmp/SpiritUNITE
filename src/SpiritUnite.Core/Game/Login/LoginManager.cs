using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpiritUnite.Auth;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Badge;
using SpiritUnite.Game.BattlePass;
using SpiritUnite.Databin;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Energy;
using SpiritUnite.Game.Credit;
using SpiritUnite.Game.Friend;
using SpiritUnite.Game.Guild;
using SpiritUnite.Game.HeldItem;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.Match;
using SpiritUnite.Game.Newbie;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.PveChallenge;
using SpiritUnite.Game.RallyChallenge;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Settings;
using SpiritUnite.Game.Shop;
using SpiritUnite.Game.StarRoad;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Game.TenGames;
using SpiritUnite.Game.TrainerExpGift;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;
using static SpiritUnite.Packets.Messages.PB_ENUM_CS_MSGID_DEF;

namespace SpiritUnite.Game.Login;

public sealed class LoginManager(
    RoleInfoStore roles,
    GameData gameData,
    ActivityManager activityManager,
    EeveeChallengeManager eeveeChallengeManager,
    RallyChallengeManager rallyChallenge,
    AllPkmChallengeManager allPkmChallenges,
    MegaEvolutionManager megaEvolution,
    BagManager bag,
    BadgeManager badges,
    FriendRepository friendRepository,
    HeldItemManager heldItems,
    PokemonManager pokemon,
    ProfileManager profiles,
    SettingsManager settings,
    ShopManager shop,
    FriendManager friends,
    GuildManager guilds,
    NewbieManager newbies,
    NewbieChallengeManager newbieChallenges,
    NewbieChallengeTaskManager newbieChallengeTasks,
    NewbieGiftManager newbieGifts,
    LobbyNotices notices,
    StarRoadManager starRoad,
    TrainerExpGiftManager trainerExpGiftManager,
    TaskUpdates taskUpdates,
    WalletManager wallets,
    AccountManager accounts,
    MailManager mail,
    EnergyManager energies,
    CreditManager credit,
    CreditPointTaskManager creditPointTasks,
    RankedManager ranked,
    BattlePassManager battlePass,
    WarmBattleManager warmBattles,
    InviteFriendManager inviteFriends,
    TenGamesManager tenGames,
    PveChallengeProgress pveChallenge,
    PveChallengeManager pveChallengeManager,
    LobbyRoles lobbyRoles,
    BackgroundOperations operations,
    TimeProvider clock,
    ILogger<LoginManager> logger)
{
    private const int NoActiveBossRushPeriodID = 0;
    private const int NewTagShown = 1;
    private const int BindingAccountBadgeNum = 1;
    private const int LoginSucceeded = 1;
    private const int ShippedDatabinABTestID = 1;
    private const int NoRankedDivision = 0;
    private const int FirstpayTotalCoupons = 60;
    private const int FirstLoginInToday = 1;
    private const int NotFirstLoginInToday = 0;
    private const int NewRolePveChallengePrivilegeBadgeNum = 1;
    private const int NewRoleSubscriptionPasterBadgeNum = 1;
    private const int NewRoleScoreFinMapNoticeBadgeNum = 1;
    private const int NewRoleTenGamesApplyBadgeNum = 1;
    private const int NewRoleTenGamesTeamBadgeNum = 1;
    private const int NewRoleLocalLangugageBadgeNum = 1;
    private const int NewRoleBackDownloadBadgeNum = 1;

    private static readonly TimeSpan DailyRefreshMaxDelay = TimeSpan.FromSeconds(60);

    private static readonly PB_ENUM_RES_RED_DOT_ID[] DailyRefreshRecallClears =
    [
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdRecallLoginAward,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdRecallScoreAward,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdRecallBattleAward,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdRecallTaskAward,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdRecallRechargeAward,
    ];

    private static readonly PB_ENUM_RES_RED_DOT_ID[] DailyRefreshEventClears =
    [
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdPayForWeekLoginAward,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdSnowTurntableDraw,
    ];

    private static readonly PB_ENUM_RES_RED_DOT_ID[] DailyRefreshLateClears =
    [
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdWcsMatchPrediction,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewHead,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdPuzzleActivity,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdSubscriptionCoupons,
    ];

    private readonly ConcurrentDictionary<ulong, ulong> relayMD5ByUid = new();

    public Func<ulong, GamePacket?>? ConnRelayRecover { get; set; }

    public void RememberRelayMD5(ulong uid, ulong relayMD5) => relayMD5ByUid[uid] = relayMD5;

    public ulong FindRelayMD5(ulong uid) => relayMD5ByUid.GetValueOrDefault(uid);

    public async Task<bool> RecordAcntLoginAsync(ulong uid, int loginCountryCode)
    {
        await settings.AdoptLoginCountryCodeAsync(uid, loginCountryCode);
        return await activityManager.RecordLoginDayAsync(uid);
    }

    public async Task<bool> CompleteRegistrationAsync(ulong uid, int? loginCountryCode)
    {
        if (loginCountryCode is { } countryCode)
        {
            await settings.AdoptLoginCountryCodeAsync(uid, countryCode);
        }

        await pokemon.CreateRegistUsedPokemonAsync(uid);
        logger.LogInformation("Trainer registration completed for uid {Uid}", uid);
        await mail.SendWelcomeMailAsync(uid);
        return await activityManager.RecordLoginDayAsync(uid);
    }

    public Task<bool> RecordLoginDayAsync(ulong uid) => activityManager.RecordLoginDayAsync(uid);

    public async Task<pbSCAcntLoginRsp> BuildAcntLoginRspAsync(LMNRoleInfo role, bool isFirstLoginInToday)
    {
        logger.LogInformation("Login from uid {Uid} as {Name}", role.GameUlongUID, role.InGameUsrName);

        var wallet = await wallets.LoadAsync(role.GameUlongUID);
        var buyRecords = await shop.GetBuyRecordsAsync(role.GameUlongUID);
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var clientSettings = await settings.LoadAsync(role.GameUlongUID);
        await trainerExpGiftManager.LoadAsync(role.GameUlongUID);
        var registerTime = await newbies.GetRegisterTimeAsync(role);
        await ranked.LoadAsync(role.GameUlongUID, reload: true);
        await warmBattles.LoadAsync(role.GameUlongUID);

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var acntLoginRsp = new pbSCAcntLoginRsp
        {
            ResultInfo = new pbResultInfo(),
            IsSucc = LoginSucceeded,
            GameAcntUid = role.GameUlongUID,
            Level = RoleProfile.GetLevel(gameData, role),
            RoleTotalExp = role.RoleTotalExp,
            NextLevelToTalExp = RoleProfile.GetNextLevelTotalExp(gameData, role),
            ReachMaxLevel = RoleProfile.HasReachedMaxLevel(gameData, role) ? 1 : 0,
            Name = role.InGameUsrName,
            FollowInfo = await pokemon.BuildFollowInfoAsync(role),
            Gender = role.Gender,
            AvatarSuitSeqID = role.GetAvatarSuitSeqID(gameData),
            HeadID = RoleImage.GetHeadID(gameData, role),
            RoleAvatarInfo = role.RoleAvatarInfo,
            NewbieState = role.BuildNewbieState(),
            RegisterTime = registerTime,
            IsFirstLoginInToday = isFirstLoginInToday ? FirstLoginInToday : NotFirstLoginInToday,
            FriendShortId = await inviteFriends.FindFriendShortIdAsync(role.GameUlongUID),
            MonthUseCouponsInfo = AwaitsRegistGive(role)
                ? new pbCOMDT_MONTH_USE_COUPONS_INFO()
                : LobbyNotices.BuildMonthUseCouponsInfo(registerTime),
            FirstPokemonId = role.FirstPokemonId,
            ServerGameTimeStamp = now,
            ServerRealTimeStamp = now,
            ABTestID = ShippedDatabinABTestID,
            UsingLobbyBgId = role.GetUsingLobbyBgId(gameData),
            SeasonId = RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId,
            SeasonPoint = TrainerExpGiftManager.GetSeasonPoint(role),
            ReachMaxSeasonGift = trainerExpGiftManager.GetReachMaxSeasonGift(role),
            RankedDivision = RankedMatch.IsRankedMatchUnlocked(gameData, notices, role)
                ? ranked.PositionOf(role.GameUlongUID).RankedDivision
                : NoRankedDivision,
            ProtectCardSelectStatus = ranked.ProtectCardSelectStatusOf(role.GameUlongUID),
            TotalCoupons = FirstpayTotalCoupons,
            CoinInfo = wallet.BuildCoinInfo(),
            AcntRealCoupous = wallet.AcntRealCoupous,
            SpecialCoin = wallet.BuildSpecialCoinInfo(),
            SpecialSaleInfo = ShopBuyRecord.BuildSpecialSaleInfo(buyRecords),
            HeldItemSlotUnlockInfo = await heldItems.BuildHeldItemSlotUnlockInfoAsync(role),
            AcceptChatConvention = (await friendRepository.GetSocialAsync(role.GameUlongUID)).AcceptChatConvention,
            HeadIconInfo = profiles.BuildHeadIconInfo(role, style),
            PlayerContestantInfo = profiles.BuildLoginPlayerContestantInfo(role, style),
            IsAnonymity = style.IsAnonymity,
            IsHideInfo = style.IsHideInfo,
            PersonStyleInfo = ProfileManager.BuildPersonStyleInfo(role, style),
            UserPlantInfo = style.BuildUserPlantInfo(),
            ClientBitState = clientSettings.ClientBitState,
            MenuInfo = clientSettings.MenuInfo,
            ABMode = clientSettings.ABMode,
            CountryCode = clientSettings.CountryCode,
            IsInvisible = clientSettings.IsForeverInvisible,
            IsCloseIntimacyShow = clientSettings.IsCloseIntimacyShow,
            UserSchemeSelectInfo = notices.BuildUserSchemeSelectInfo(),
            PokemonID = role.FollowPokemonId,
            LoginCountryCode = clientSettings.CountryCode,
            RegisterCountryCode = clientSettings.CountryCode,
            AntiAdditionInfo = new pbCOMDT_ANTIADDITION_INFO(),
            GuildInfo = await guilds.BuildAcntGuildInfoAsync(role.GameUlongUID),
            NewbiChallengeUnlockInfo = await newbieChallenges.BuildUnlockInfoAsync(role),
            PrivilePlatInfo = new pbCOMDT_PRIVILE_PLAT_INFO(),
            GroupInfo = new pbCOMDT_GROUP_INFO(),
            DailyUniqTaskInfo = new pbCOMDT_DAILY_UNIQ_TASK_INFO(),
            HighestRankedDivisionInHistory = await ranked.BuildHighestRankedDivisionInHistoryAsync(role.GameUlongUID),
            AchievementInfo = new pbCOMDT_ROLE_ACHIEVEMENT_INFO(),
            BanInfo = EmptyChildren.Fill(new pbCOMDT_BAN_INFO()),
            NoneRechargeInfo = new pbCOMDT_NONE_RECHARGE_ACTIVITY_VIEW_INFO(),
            PersonaliseButtonInfo = new pbCOMDT_PERSONALISE_BUTTON_LIST(),
        };
        await rallyChallenge.FillAcntLoginRspAsync(role.GameUlongUID, acntLoginRsp);
        await allPkmChallenges.MailUnclaimedRewardsAsync(role);
        return acntLoginRsp;
    }

    public async Task<GamePacket[]> BuildLoginFinishAsync(
        GamePacket request,
        LMNRoleInfo role,
        bool offingRestart,
        bool isFirstLoginInToday)
    {
        var registGains = profiles.GrantRegistGive(role);
        var isFirstLogin = registGains.Length > 0;
        GamePacket[] replies;
        if (isFirstLogin)
        {
            await roles.SaveAsync(role);
            replies = await BuildFirstLoginAsync(request, role, registGains);
        }
        else if (offingRestart)
        {
            replies = await BuildReturningLoginAsync(request, role, offingRestart);
        }
        else
        {
            replies = await BuildColdLoginAsync(request, role, isFirstLoginInToday);
        }

        if (isFirstLoginInToday)
        {
            ScheduleDailyRefresh(role);
        }

        return replies;
    }

    private void ScheduleDailyRefresh(LMNRoleInfo role)
    {
        var delay = DailyRefreshMaxDelay * Random.Shared.NextDouble();
        operations.Run("daily refresh push", async token =>
        {
            await Task.Delay(delay, clock, token);
            if (lobbyRoles.IsOnline(role.GameUlongUID))
            {
                await lobbyRoles.PushAsync(role.GameUlongUID, await BuildDailyRefreshPacketsAsync(role));
            }
        });
    }

    private async Task<GamePacket[]> BuildDailyRefreshPacketsAsync(LMNRoleInfo role) =>
    [
        .. DailyRefreshRecallClears.Select(BuildRedDotClear),
        await newbieGifts.BuildFourteenDayRedDotAsync(role),
        GamePacket.Create(PbScidNewbieGiftInfoNtf, await newbieGifts.BuildNewbieGiftInfoNtfAsync(role)),
        .. DailyRefreshEventClears.Select(BuildRedDotClear),
        GamePacket.Create(PbScidGetCanopyListNtf, await BuildCanopyListNtfAsync(role)),
        .. DailyRefreshLateClears.Select(BuildRedDotClear),
        GamePacket.Create(PbScidPrivilePlatInfoNtf, new pbSCPrivilePlatInfoNtf { PrivilePlatInfo = new pbCOMDT_PRIVILE_PLAT_INFO() }),
    ];

    private static GamePacket BuildRedDotClear(PB_ENUM_RES_RED_DOT_ID redDotID) =>
        GamePacket.Create(PbScidRedDotClearNtf, LobbyNotices.BuildRedDotClearNtf(redDotID));

    private bool AwaitsRegistGive(LMNRoleInfo role) =>
        !ContestantCard.FindRegistGiveDecorationIDs(gameData)
            .Any(decorationID => role.OwnsItem(RoleItemType.ContestantCardDecoration, decorationID));

    private async Task<GamePacket[]> BuildReturningLoginAsync(GamePacket request, LMNRoleInfo role, bool offingRestart)
    {
        await StoreMailRedDotsAsync(role.GameUlongUID);
        await StoreFourteenDayRedDotAsync(role);
        await StoreEeveeChallengeRedDotAsync(role);
        var clientSettings = await settings.LoadAsync(role.GameUlongUID);
        var route = await SyncStarRoadAsync(role);
        var canopyList = await BuildCanopyListNtfAsync(role);

        var replies = new ReplyList(request);
        replies.Add(PbScidLoginFinishNtf, new pbSCLoginFinishNtf());
        replies.AddRange(await friends.BuildFriendDataPacketsAsync(role));
        replies.Add(await starRoad.BuildStarRoadInfoNtfAsync(role, route, isLoginNtf: false));
        await AddLobbyStateAsync(replies, role, canopyList);
        if (offingRestart)
        {
            replies.Add(PbScidOffingRestartRsp, new pbSCOffingRestartRsp { ResultInfo = new pbResultInfo() });
        }

        if (ConnRelayRecover?.Invoke(role.GameUlongUID) is { } connRelayRecover)
        {
            replies.Add(connRelayRecover);
        }

        replies.Add(PbScidNewbieGiftInfoNtf, await newbieGifts.BuildNewbieGiftInfoNtfAsync(role));
        replies.Add(PbScidGetCanopyListNtf, canopyList);
        replies.Add(PbScidWeeklySettleCookieNtf, LobbyNotices.BuildWeeklySettleCookieNtf());
        if (SettingsManager.BuildClientGameSettingNtf(clientSettings) is { } clientGameSettingNtf)
        {
            replies.Add(PbScidClientGameSettingNtf, clientGameSettingNtf);
        }

        replies.Add(PbScidActivityLoginShowNtf, new pbSCActivityLoginShowNtf());
        replies.AddRange(await megaEvolution.BuildLoginNtfsAsync(role.GameUlongUID));
        AddRedDots(replies, clientSettings);
        replies.Add(await tenGames.BuildInviteRedDotAsync(role.GameUlongUID));
        return replies.ToArray();
    }

    public async Task<GamePacket[]> BuildColdLoginPreludeAsync(LMNRoleInfo role) =>
        SettingsManager.BuildClientGameSettingNtf(await settings.LoadAsync(role.GameUlongUID)) is { } clientGameSettingNtf
            ? [GamePacket.Create(PbScidClientGameSettingNtf, clientGameSettingNtf)]
            : [];

    private async Task<GamePacket[]> BuildColdLoginAsync(GamePacket request, LMNRoleInfo role, bool isFirstLoginInToday)
    {
        var uid = role.GameUlongUID;
        await StoreMailRedDotsAsync(uid);
        await StoreFourteenDayRedDotAsync(role);
        await StoreEeveeChallengeRedDotAsync(role);
        var creditPointTaskInfo = isFirstLoginInToday
            ? await creditPointTasks.ReissueDailyAsync(uid, clock.GetUtcNow().ToUnixTimeSeconds())
            : null;
        var clientSettings = await settings.LoadAsync(uid);
        var route = await SyncStarRoadAsync(role);
        var canopyList = await BuildCanopyListNtfAsync(role);

        var replies = new ReplyList(request);
        replies.Add(PbScidLoginFinishNtf, new pbSCLoginFinishNtf());
        replies.Add(PbScidBattlePassNtf, await battlePass.BuildBattlePassNtfAsync(role));
        replies.AddRange(await friends.BuildFriendDataPacketsAsync(role));
        if (isFirstLoginInToday)
        {
            replies.Add(PbScidActivityInfoNtf, notices.BuildActivityInfoNtf(clientSettings.CountryCode));
            replies.Add(PbScidTaskDeleteNtf, notices.BuildCreditPointTaskDeleteNtf());
            replies.Add(PbScidUpdateTaskInfoNtf, creditPointTaskInfo!);
            replies.AddRange(await newbieChallengeTasks.BuildDayUnlockPushesAsync(role));
            replies.Add(PbScidNewbieStateInfoNtf, NewbieManager.BuildNewbieStateInfoNtf(role));
            replies.Add(PbScidEnergyInfoNtf, EnergyCenter.BuildEnergyInfoNtf(await energies.LoadAsync(uid)));
            if (await activityManager.BuildDayLoginTaskUpdateAsync(role) is { } dayLoginTasks)
            {
                replies.Add(PbScidUpdateTaskInfoNtf, dayLoginTasks);
            }
        }

        replies.Add(PbScidNewbieGiftInfoNtf, await newbieGifts.BuildNewbieGiftInfoNtfAsync(role));
        replies.Add(PbScidGetCanopyListNtf, canopyList);
        replies.Add(PbScidActivityInfoNtf, notices.BuildActivityInfoNtf(clientSettings.CountryCode));
        replies.Add(PbScidSubscriptionStatusNtf, notices.BuildSubscriptionStatusNtf());
        replies.Add(PbScidQueryTowerDefenseNtf, notices.BuildQueryTowerDefenseNtf());
        var starRoadInfo = await starRoad.BuildStarRoadInfoNtfAsync(role, route, isLoginNtf: false);
        replies.Add(starRoadInfo);
        if (isFirstLoginInToday)
        {
            replies.Add(starRoadInfo);
        }

        replies.Add(PbScidQuickSpeakCfgNtf, new pbSCQuickSpeakCfgNtf { QuickSpeakInfo = settings.BuildQuickSpeakInfo(clientSettings) });
        await AddLobbyStateAsync(replies, role, canopyList);
        if (ConnRelayRecover?.Invoke(uid) is { } connRelayRecover)
        {
            replies.Add(connRelayRecover);
        }

        replies.Add(PbScidWeeklySettleCookieNtf, LobbyNotices.BuildWeeklySettleCookieNtf());
        replies.Add(PbScidActivityLoginShowNtf, new pbSCActivityLoginShowNtf());
        replies.AddRange(await megaEvolution.BuildLoginNtfsAsync(uid));
        AddRedDots(replies, clientSettings);
        replies.AddRange(notices.BuildCouponsRefreshPackets((await wallets.LoadAsync(uid)).BuildAcntCouponsRsp(), grantSubscriptionOpen: false));
        replies.Add(PbScidGetBindChannelRsp, BuildGetBindChannelRsp(await accounts.FindBoundChannelsAsync(uid)));
        replies.Add(await tenGames.BuildInviteRedDotAsync(uid));
        return replies.ToArray();
    }

    private async Task<GamePacket[]> BuildFirstLoginAsync(GamePacket request, LMNRoleInfo role, pbSCProfileGainResNtf[] registGains)
    {
        var uid = role.GameUlongUID;
        var registerTime = await newbies.GetRegisterTimeAsync(role);
        await StoreRegistRedDotsAsync(role, registGains[^1]);
        var clientSettings = await settings.LoadAsync(uid);
        var route = await SyncStarRoadAsync(role);
        var ownershipTasks = await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role);
        var energyInfo = EnergyCenter.BuildEnergyInfoNtf(await energies.LoadAsync(uid));
        var boundChannels = await accounts.FindBoundChannelsAsync(uid);
        var canopyList = await BuildCanopyListNtfAsync(role);
        var firstGain = registGains[0];
        var laterGains = registGains[1..];

        var replies = new ReplyList(request);
        replies.Add(PbScidLoginFinishNtf, new pbSCLoginFinishNtf());
        replies.Add(PbScidBattlePassNtf, await battlePass.BuildBattlePassNtfAsync(role));
        replies.AddRange(await friends.BuildFriendDataPacketsAsync(role));
        replies.Add(PbScidActivityInfoNtf, notices.BuildActivityInfoNtf(clientSettings.CountryCode));
        replies.Add(PbScidUpdateTaskInfoNtf, await creditPointTasks.IssueAsync(uid, registerTime));
        replies.Add(PbScidNewbieStateInfoNtf, NewbieManager.BuildNewbieStateInfoNtf(role));
        replies.Add(PbScidEnergyInfoNtf, energyInfo);
        replies.Add(PbScidNewbieGiftInfoNtf, await newbieGifts.BuildNewbieGiftInfoNtfAsync(role));
        replies.Add(PbScidProfileGainResNtf, firstGain);
        replies.Add(PbScidEnergyInfoNtf, energyInfo);
        replies.Add(PbScidGetCanopyListNtf, canopyList);
        replies.Add(PbScidWeeklySettleCookieNtf, LobbyNotices.BuildWeeklySettleCookieNtf());
        replies.Add(PbScidStarRoadSiteUpdateNtf, route.BuildSiteUpdateNtf(route.CurSiteID));
        replies.Add(await starRoad.BuildStarRoadInfoNtfAsync(role, route, isLoginNtf: true));
        replies.Add(PbScidQuickSpeakCfgNtf, new pbSCQuickSpeakCfgNtf { QuickSpeakInfo = settings.BuildQuickSpeakInfo(clientSettings) });
        await AddLobbyStateAsync(replies, role, canopyList);
        replies.AddRange(await wallets.GrantFirstLoginSpecialCoinsAsync(uid));
        replies.Add(PbScidUpdateTaskInfoNtf, ownershipTasks);
        foreach (var gain in laterGains)
        {
            replies.Add(PbScidProfileGainResNtf, gain);
            replies.Add(PbScidUpdateTaskInfoNtf, ownershipTasks);
        }

        replies.Add(PbScidUpdateTaskInfoNtf, ownershipTasks);
        replies.Add(PbScidWeeklySettleCookieNtf, LobbyNotices.BuildWeeklySettleCookieNtf());
        replies.Add(PbScidActivityLoginShowNtf, new pbSCActivityLoginShowNtf());
        replies.AddRange(await megaEvolution.BuildLoginNtfsAsync(role.GameUlongUID));
        AddRedDots(replies, clientSettings);
        replies.AddRange(await BuildCouponsRefreshPacketsAsync(uid));
        replies.Add(await tenGames.BuildInviteRedDotAsync(uid));
        if (HasOnlyGuestChannels(boundChannels))
        {
            replies.Add(PbScidRedDotReplaceNtf, LobbyNotices.BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdBindingAccount, BindingAccountBadgeNum));
        }

        replies.Add(PbScidGetBindChannelRsp, BuildGetBindChannelRsp(boundChannels));
        replies.AddRange(notices.BuildRefreshPackets(clientSettings.CountryCode));
        replies.Add(PbScidMonthUseCouponsNtf, LobbyNotices.BuildMonthUseCouponsNtf(registerTime));
        return replies.ToArray();
    }

    private async Task AddLobbyStateAsync(ReplyList replies, LMNRoleInfo role, pbSCGetCanopyListNtf canopyList)
    {
        var uid = role.GameUlongUID;
        var now = clock.GetUtcNow();
        replies.Add(PbScidGetPokemonListRsp, await pokemon.BuildGetPokemonListRspAsync(role));
        replies.Add(PbScidBanPokemonListNtf, LobbyNotices.BuildBanPokemonListNtf());
        replies.Add(PbScidCompetitionOpenInfoNtf, notices.BuildCompetitionOpenInfoNtf());
        replies.Add(PbScidOutSideItemPackageAllInfoRsp, await bag.BuildOutSideItemPackageAllInfoRspAsync(uid));
        replies.Add(PbScidLimitTimeReddotNtf, LobbyNotices.BuildLimitTimeReddotNtf());
        replies.Add(PbScidGetAvatarListRsp, BuildGetAvatarListRsp(role));
        replies.Add(PbScidBattlePassNtf, await battlePass.BuildBattlePassNtfAsync(role));
        replies.Add(PbScidEnergyInfoNtf, EnergyCenter.BuildEnergyInfoNtf(await energies.LoadAsync(uid)));
        replies.Add(PbScidTrainerExpNextGiftInfoNtf, trainerExpGiftManager.BuildTrainerExpNextGiftInfoNtf(role));
        replies.Add(PbScidCreditPointNtf, await credit.BuildCreditPointNtfAsync(uid));
        replies.Add(PbScidBossRushPeriodNtf, new pbSCBossRushPeriodNtf { PeriodID = NoActiveBossRushPeriodID });
        replies.Add(PbScidShopNewSaleInfoNtf, notices.BuildShopNewSaleInfoNtf());
        replies.Add(PbScidGetBadgeAllInfoRsp, await badges.BuildGetBadgeAllInfoRspAsync(uid));
        replies.Add(PbScidGetCanopyListNtf, canopyList);
        replies.Add(PbScidQueryTowerDefenseNtf, notices.BuildQueryTowerDefenseNtf());
        replies.Add(PbScidGetCardFaceInfoNtf, BuildGetCardFaceInfoNtf(role));
        replies.Add(LobbyNotices.PbScidPasterSprayerListNtf, LobbyNotices.BuildPasterSprayerListNtf());
        replies.Add(LobbyNotices.PbScidPasterSprayerAcntInfoNtf, notices.BuildPasterSprayerAcntInfoNtf(role, await newbies.GetRegisterTimeAsync(role)));
    }

    private async Task<StarRoadRoute> SyncStarRoadAsync(LMNRoleInfo role) =>
        (await starRoad.SyncOwnedPokemonAsync(role)).Route;

    private async Task StoreRegistRedDotsAsync(LMNRoleInfo role, pbSCProfileGainResNtf lastRegistGain)
    {
        var uid = role.GameUlongUID;
        var fourteenDay = await newbieGifts.FindFourteenDayRedDotAsync(role);
        var eeveeChallengeRedDot = await eeveeChallengeManager.FindRedDotAsync(role);
        var completeActivityTasks = (await activityManager.BuildTaskInfoListAsync(role))
            .Count(task => task.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);
        var countryCode = (await settings.LoadAsync(uid)).CountryCode;
        var pvePrivilegeOpen = pveChallengeManager.FindPrivilegeActivity(clock.GetUtcNow()) is not null;
        await settings.ReplaceRedDotsAsync(uid,
        [
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdPveChallengeNormal, pveChallenge.CountOpenNormalNodes(role)),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdLobbyBackground, role.CountNewTags(RoleItemType.LobbyBg)),
            .. await mail.BuildUnreadRedDotDetailsAsync(uid),
            .. notices.BuildStartActivityShowRedDots(countryCode),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdActivityReward, completeActivityTasks),
            .. pvePrivilegeOpen ? BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdPveChallengePrivilege, NewRolePveChallengePrivilegeBadgeNum) : [],
            .. eeveeChallengeRedDot is null ? [] : new[] { eeveeChallengeRedDot },
            .. notices.BuildRegistCreditPointRewardRedDots(),
            .. fourteenDay is null ? [] : new[] { fourteenDay },
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewEntrycardEditItem, CountNewTags(lastRegistGain)),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdSubscriptionPaster, NewRoleSubscriptionPasterBadgeNum),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdScoreFinMapNotice, NewRoleScoreFinMapNoticeBadgeNum),
            .. notices.BuildRegistShopRedDots(),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdTenGamesApply, NewRoleTenGamesApplyBadgeNum),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdTenGamesTeam, NewRoleTenGamesTeamBadgeNum),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdLocalLangugage, NewRoleLocalLangugageBadgeNum),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdBackDownload, NewRoleBackDownloadBadgeNum),
        ]);
        await settings.ReplaceBubTipsAsync(uid, LobbyNotices.BuildRegistBubTips());
    }

    private static pbCOMDT_RED_DOT_DETAIL[] BuildRedDot(PB_ENUM_RES_RED_DOT_ID redDotID, int redDotBadgeNum) =>
        redDotBadgeNum > 0 ? [new pbCOMDT_RED_DOT_DETAIL { RedDotID = (int)redDotID, RedDotBadgeNum = redDotBadgeNum }] : [];

    private async Task StoreMailRedDotsAsync(ulong uid)
    {
        var mailRedDots = await mail.BuildAllUnreadRedDotCountsAsync(uid);
        await settings.ReplaceRedDotsAsync(uid, [.. mailRedDots.Where(redDot => redDot.RedDotBadgeNum > 0)]);
        foreach (var unreadRedDot in mailRedDots.Where(redDot => redDot.RedDotBadgeNum == 0))
        {
            await settings.ClearRedDotAsync(uid, unreadRedDot.RedDotID);
        }
    }

    private async Task StoreFourteenDayRedDotAsync(LMNRoleInfo role)
    {
        if (await newbieGifts.FindFourteenDayRedDotAsync(role) is { } fourteenDay)
        {
            await settings.ReplaceRedDotsAsync(role.GameUlongUID, [fourteenDay]);
        }
        else
        {
            await settings.ClearRedDotAsync(role.GameUlongUID, NewbieGiftManager.FourteenDayRedDotID);
        }
    }

    private async Task StoreEeveeChallengeRedDotAsync(LMNRoleInfo role)
    {
        if (await eeveeChallengeManager.FindRedDotAsync(role) is { } eeveeChallengeRedDot)
        {
            await settings.ReplaceRedDotsAsync(role.GameUlongUID, [eeveeChallengeRedDot]);
        }
        else
        {
            await settings.ClearRedDotAsync(role.GameUlongUID, EeveeChallengeManager.EeveeChallengeRedDotID);
        }
    }

    private async Task<GamePacket[]> BuildCouponsRefreshPacketsAsync(ulong uid)
    {
        var coupons = (await wallets.LoadAsync(uid)).BuildAcntCouponsRsp();
        return notices.BuildCouponsRefreshPackets(coupons, grantSubscriptionOpen: true);
    }

    private async Task<pbSCGetCanopyListNtf> BuildCanopyListNtfAsync(LMNRoleInfo role) => new()
    {
        ResultInfo = new pbResultInfo(),
        CanopyList = profiles.BuildCanopyList(role),
        CurCanopyID = profiles.GetCurCanopyID(role, await profiles.LoadAsync(role.GameUlongUID)),
    };

    private pbSCGetCardFaceInfoNtf BuildGetCardFaceInfoNtf(LMNRoleInfo role) => new()
    {
        ResultInfo = new pbResultInfo(),
        CardFaceList = profiles.BuildCardFaceList(role),
    };

    private static pbSCGetAvatarListRsp BuildGetAvatarListRsp(LMNRoleInfo role) => new()
    {
        ResultInfo = new pbResultInfo(),
        AvatarList = { role.BuildAvatarList() },
    };

    private static pbSCGetBindChannelRsp BuildGetBindChannelRsp(LoginPlatform[] boundChannels) => new()
    {
        ResultInfo = new pbResultInfo(),
        ChannelList = { boundChannels.Select(channel => (int)channel) },
    };

    private static void AddRedDots(ReplyList replies, RoleSettings clientSettings)
    {
        replies.Add(PbScidRedDotInfoNtf, SettingsManager.BuildRedDotInfoNtf(clientSettings));
        replies.Add(PbScidBubTipInfoNtf, SettingsManager.BuildBubTipInfoNtf(clientSettings));
    }

    private static bool HasOnlyGuestChannels(LoginPlatform[] boundChannels) =>
        boundChannels.All(channel => channel == LoginPlatform.GUEST);

    private static int CountNewTags(pbSCProfileGainResNtf gain) =>
        gain.ContestantResList?.ResInfoList.Count(res => res.NewTag == NewTagShown) ?? 0;
}
