using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Auth;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Badge;
using SpiritUNITE.Game.BattlePass;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Energy;
using SpiritUNITE.Game.Chat;
using SpiritUNITE.Game.Credit;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Guild;
using SpiritUNITE.Game.HeldItem;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Game.Match;
using SpiritUNITE.Game.Newbie;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Game.Shop;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.TenGames;
using SpiritUNITE.Game.TrainerExpGift;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using static SpiritUNITE.Packets.Messages.PB_ENUM_CS_MSGID_DEF;

namespace SpiritUNITE.Game.Login;

public sealed class LoginManager(
    RoleInfoStore roles,
    GameData gameData,
    ActivityManager activityManager,
    EeveeChallengeManager eeveeChallengeManager,
    BagManager bag,
    BadgeManager badges,
    ChatManager chat,
    HeldItemManager heldItems,
    PokemonManager pokemon,
    ProfileManager profiles,
    SettingsManager settings,
    ShopManager shop,
    FriendManager friends,
    GuildManager guilds,
    NewbieManager newbies,
    NewbieChallengeManager newbieChallenges,
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
    RankedManager ranked,
    BattlePassManager battlePass,
    WarmBattleManager warmBattles,
    InviteFriendManager inviteFriends,
    TenGamesManager tenGames,
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

    private readonly ConcurrentDictionary<ulong, int> loginCountryCodeByUnregisteredUid = new();
    private readonly ConcurrentDictionary<ulong, ulong> relayMD5ByUid = new();

    public Func<ulong, GamePacket?>? ConnRelayRecover { get; set; }

    public void RememberRelayMD5(ulong uid, ulong relayMD5) => relayMD5ByUid[uid] = relayMD5;

    public ulong FindRelayMD5(ulong uid) => relayMD5ByUid.GetValueOrDefault(uid);

    public async Task RecordAcntLoginAsync(ulong uid, int loginCountryCode)
    {
        await settings.AdoptLoginCountryCodeAsync(uid, loginCountryCode);
        await activityManager.RecordLoginDayAsync(uid);
    }

    public void RememberLoginCountry(ulong unregisteredUid, int loginCountryCode) =>
        loginCountryCodeByUnregisteredUid[unregisteredUid] = loginCountryCode;

    public async Task CompleteRegistrationAsync(ulong uid)
    {
        if (loginCountryCodeByUnregisteredUid.TryRemove(uid, out var countryCode))
        {
            await settings.AdoptLoginCountryCodeAsync(uid, countryCode);
        }

        await pokemon.CreateRegistUsedPokemonAsync(uid);
        logger.LogInformation("Trainer registration completed for uid {Uid}", uid);
        await mail.SendWelcomeMailAsync(uid);
        await activityManager.RecordLoginDayAsync(uid);
    }

    public Task RecordLoginDayAsync(ulong uid) => activityManager.RecordLoginDayAsync(uid);

    public async Task<pbSCAcntLoginRsp> BuildAcntLoginRspAsync(LMNRoleInfo role)
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
        return new pbSCAcntLoginRsp
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
            FriendShortId = await inviteFriends.FindFriendShortIdAsync(role.GameUlongUID),
            MonthUseCouponsInfo = LobbyNotices.BuildMonthUseCouponsInfo(registerTime),
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
            AcceptChatConvention = await chat.GetAcceptChatConventionAsync(role.GameUlongUID),
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
    }

    public async Task<GamePacket[]> BuildLoginFinishAsync(GamePacket request, LMNRoleInfo role, bool offingRestart)
    {
        var registGains = profiles.GrantRegistGive(role);
        var isFirstLogin = registGains.Length > 0;
        if (!isFirstLogin)
        {
            return await BuildReturningLoginAsync(request, role, offingRestart);
        }

        await roles.SaveAsync(role);
        return await BuildFirstLoginAsync(request, role, registGains);
    }

    public async Task<GamePacket[]> BuildFirstClientGameSettingPacketsAsync(LMNRoleInfo role) =>
    [
        GamePacket.Create(PbScidNewbieGiftInfoNtf, await newbieGifts.BuildNewbieGiftInfoNtfAsync(role)),
        GamePacket.Create(PbScidGetCanopyListNtf, BuildCanopyListNtf(role)),
        GamePacket.Create(PbScidPrivilePlatInfoNtf, new pbSCPrivilePlatInfoNtf { PrivilePlatInfo = new pbCOMDT_PRIVILE_PLAT_INFO() }),
    ];

    private async Task<GamePacket[]> BuildReturningLoginAsync(GamePacket request, LMNRoleInfo role, bool offingRestart)
    {
        await StoreFourteenDayRedDotAsync(role);
        await StoreEeveeChallengeRedDotAsync(role);
        var clientSettings = await settings.LoadAsync(role.GameUlongUID);
        var route = await SyncStarRoadAsync(role);
        var canopyList = BuildCanopyListNtf(role);

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
        AddRedDots(replies, clientSettings);
        replies.Add(await tenGames.BuildInviteRedDotAsync(role.GameUlongUID));
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
        var canopyList = BuildCanopyListNtf(role);
        var firstGain = registGains[0];
        var laterGains = registGains[1..];

        var replies = new ReplyList(request);
        replies.Add(PbScidLoginFinishNtf, new pbSCLoginFinishNtf());
        replies.Add(PbScidBattlePassNtf, await battlePass.BuildBattlePassNtfAsync(role));
        replies.AddRange(await friends.BuildFriendDataPacketsAsync(role));
        replies.Add(PbScidActivityInfoNtf, notices.BuildActivityInfoNtf(clientSettings.CountryCode));
        replies.Add(PbScidUpdateTaskInfoNtf, notices.BuildCreditPointTaskInfoNtf(registerTime));
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
        await settings.ReplaceRedDotsAsync(uid,
        [
            .. await mail.BuildUnreadRedDotDetailsAsync(uid),
            .. notices.BuildRegistRedDots(CountNewTags(lastRegistGain)),
            .. fourteenDay is null ? [] : new[] { fourteenDay },
            .. eeveeChallengeRedDot is null ? [] : new[] { eeveeChallengeRedDot },
        ]);
        await settings.ReplaceBubTipsAsync(uid, LobbyNotices.BuildRegistBubTips());
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

    private pbSCGetCanopyListNtf BuildCanopyListNtf(LMNRoleInfo role) => new()
    {
        ResultInfo = new pbResultInfo(),
        CanopyList = profiles.BuildCanopyList(role),
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
