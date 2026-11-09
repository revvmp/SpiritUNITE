using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using SpiritUnite.Battle;
using SpiritUnite.Database;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Credit;
using SpiritUnite.Game.Accusation;
using SpiritUnite.Game.BattleVoice;
using SpiritUnite.Game.Achievement;
using SpiritUnite.Game.Admin;
using SpiritUnite.Game.ActivationCode;
using SpiritUnite.Game.GiftCode;
using SpiritUnite.Game.Login;
using SpiritUnite.Game.Match;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Game.TrainerExpGift;
using SpiritUnite.Game.StarRoad;
using SpiritUnite.Game.Activity;
using SpiritUnite.Auth;
using SpiritUnite.Game.Chat;
using SpiritUnite.Game.Energy;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Badge;
using SpiritUnite.Game.BattlePass;
using SpiritUnite.Game.HeldItem;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Databin;
using SpiritUnite.Game.Friend;
using SpiritUnite.Game.Guild;
using SpiritUnite.Game.TenGames;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.Newbie;
using SpiritUnite.Game.Observe;
using SpiritUnite.Game.MSDKNotice;
using SpiritUnite.Game.FightHistory;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.PveChallenge;
using SpiritUnite.Game.Recharge;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.TowerDefense;
using SpiritUnite.Game.RallyChallenge;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Room;
using SpiritUnite.Game.ServerContent;
using SpiritUnite.Game.Settings;
using SpiritUnite.Game.Shop;
using SpiritUnite.Game.Video;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Logging;
using SpiritUnite.Tools;
using SpiritUnite.Network;
using SpiritUnite.Online;
using SpiritUnite.Network.Lwip;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Handlers.Session;
using SpiritUnite.Resource;
using SpiritUnite.Servers.Http;
using SpiritUnite.Servers.Dir;
using SpiritUnite.Servers.Lobby;
using SpiritUnite.Servers.Relay;
using SpiritUnite.Servers.Update;
using SpiritUnite.Servers.Voice;

namespace SpiritUnite;

public static class Server
{
    private const string LogFileTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u5}] {Message:lj}{NewLine}{Exception}";
    private const int RetainedLogFiles = 7;
    private const string GameSettingsFile = "gamesettings.json";

    private static readonly string[] MovedToDatabaseSettings =
        ["MSDKNotice", "ActivationCode", "Pokemon:FreePokemonList", "LobbyRules:NewSaleShopIDs", "LobbyRules:SubscriptionFree"];

    public static async Task Main(string[] args)
    {
        var builder = CreateBuilder(args);

        AddLogging(builder);
        AddSettings(builder);
        await AddDatabaseAsync(builder);
        AddGameData(builder);
        AddGame(builder);
        AddPackets(builder);
        AddServers(builder);

        var app = builder.Build();
        HttpServer.Map(app);
        await app.RunAsync();
    }

    private static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration
            .AddJsonFile(GameSettingsFile, optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        builder.WebHost.ConfigureKestrel((context, kestrel) =>
        {
            var http = context.Configuration.GetSection(HttpServerOptions.SectionName).Get<HttpServerOptions>() ?? new HttpServerOptions();
            if (IPAddress.TryParse(http.BindAddress, out var address))
            {
                kestrel.Listen(address, http.Port);
            }
        });
        return builder;
    }

    private static void AddLogging(IHostApplicationBuilder builder) =>
        builder.Services.AddSerilog(logger => logger
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Filter.ByExcluding(GiftCodeEndpoints.CarriesWebViewToken)
            .WriteTo.Async(sink => sink.Console(new SpiritConsoleFormatter()))
            .WriteTo.Async(sink => sink.File(
                Path.Combine(AppContext.BaseDirectory, "logs", "spiritunite-.log"),
                outputTemplate: LogFileTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedLogFiles,
                shared: true)));

    private static void AddSettings(IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<TcpServerOptions>()
            .BindConfiguration(TcpServerOptions.SectionName, options => options.ErrorOnUnknownConfiguration = true)
            .Validate(options => options.IsValid(), "TcpServer settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<DirServerOptions>()
            .BindConfiguration(DirServerOptions.SectionName, options => options.ErrorOnUnknownConfiguration = true)
            .Validate(options => options.IsValid(), "DirServer settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<LobbyOptions>()
            .BindConfiguration(LobbyOptions.SectionName)
            .Validate(
                _ => !MovedToDatabaseSettings.Any(key => builder.Configuration.GetSection(key).Exists()),
                $"{string.Join(", ", MovedToDatabaseSettings)} moved to the database: remove them from gamesettings.json and edit them on the admin panel")
            .Validate(options => options.IsValid(), "LobbyRules settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<WalletOptions>()
            .BindConfiguration(WalletOptions.SectionName)
            .Validate(options => options.IsValid(), "Wallet settings are invalid: starting balances must be 0 or more")
            .ValidateOnStart();

        builder.Services.AddOptions<LobbyAddressOptions>()
            .BindConfiguration(LobbyAddressOptions.SectionName, options => options.ErrorOnUnknownConfiguration = true)
            .Validate(_ => !builder.Configuration.GetSection("Lobby").Exists(), "Rename the Lobby settings section to LobbyServer")
            .Validate(options => options.IsValid(), "LobbyServer settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<RelayAddressOptions>()
            .BindConfiguration(RelayAddressOptions.SectionName, options => options.ErrorOnUnknownConfiguration = true)
            .Validate(options => options.IsValid(), "RelayServer settings are invalid: Host must be empty or an address without spaces, Port 1-65535")
            .ValidateOnStart();

        builder.Services.AddOptions<RoomOptions>()
            .BindConfiguration(RoomOptions.SectionName)
            .Validate(options => options.IsValid(), "Room settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<MatchOptions>()
            .BindConfiguration(MatchOptions.SectionName)
            .Validate(options => options.IsValid(), "Match settings are invalid: BotFillSearchSec must be 1-3600 and every WarmBattleWaitTime a MapSubMode with [min, max] seconds, 0 <= min <= max")
            .ValidateOnStart();

        builder.Services.AddOptions<RankedOptions>()
            .BindConfiguration(RankedOptions.SectionName)
            .Validate(options => options.IsValid(), "Ranked settings are invalid: WinPoint and LoseBasePoint need one value per BigRankedDivision (points 0 or more), TeamMMRScore 0 or more and FakePlayerWinPointMin at most FakePlayerWinPointMax")
            .Validate<RankedLadder>((options, ladder) => options.FitsLadder(ladder), "Ranked settings are invalid: WinPoint and LoseBasePoint need one value per pbRankedDivision BigRankedDivision, and SeasonInheritRankedDivision must map pbRankedDivision RankedDivision ids")
            .ValidateOnStart();

        builder.Services.AddOptions<BattleSettlementOptions>()
            .BindConfiguration(BattleSettlementOptions.SectionName)
            .Validate(options => options.IsValid(), "BattleSettlement settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<HeroPickOptions>()
            .BindConfiguration(HeroPickOptions.SectionName)
            .Validate(options => options.IsValid(), "HeroPick settings are invalid: NpcHeroMaxReleaseOrder must be 0 or more")
            .ValidateOnStart();

        builder.Services.AddOptions<MegaEvolutionOptions>()
            .BindConfiguration(MegaEvolutionOptions.SectionName)
            .Validate(options => options.IsValid(), "MegaEvolution settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<HeldItemOptions>()
            .BindConfiguration(HeldItemOptions.SectionName)
            .Validate(options => options.IsValid(), "HeldItem settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<VoiceServerOptions>()
            .BindConfiguration(VoiceServerOptions.SectionName, options => options.ErrorOnUnknownConfiguration = true)
            .Validate(options => options.IsValid(), "VoiceServer settings are invalid: Host must be empty or an address without spaces, Port 1-65535")
            .ValidateOnStart();

        builder.Services.AddOptions<GVoiceOptions>()
            .Configure<IOptions<VoiceServerOptions>, IOptions<LobbyAddressOptions>>((gVoice, voiceServer, lobbyAddress) =>
                gVoice.UseVoiceServer(voiceServer.Value.AdvertisedHost(lobbyAddress.Value), voiceServer.Value.Port));

        builder.Services.AddOptions<ResourceArchiveOptions>()
            .BindConfiguration(ResourceArchiveOptions.SectionName, options => options.ErrorOnUnknownConfiguration = true)
            .Validate(options => options.IsValid(), "ResourceArchive settings are invalid: Directory must be set and BaseUrl must be empty or an absolute http or https URL with no query, fragment or user info")
            .ValidateOnStart();

        builder.Services.AddOptions<UpdateServerOptions>()
            .BindConfiguration(UpdateServerOptions.SectionName, options => options.ErrorOnUnknownConfiguration = true)
            .Validate(options => options.IsValid(), "UpdateServer settings are invalid: Host must be empty or an address without spaces, Port 1-65535")
            .ValidateOnStart();
    }

    private static async Task AddDatabaseAsync(IHostApplicationBuilder builder)
    {
        var databaseOptions = await DatabaseManager.ConnectAsync(builder.Configuration["Database:ConnectionString"]);

        builder.Services.AddSingleton(databaseOptions);
        builder.Services.AddSingleton<RoleRepository>();
        builder.Services.AddSingleton<FriendRepository>();
        builder.Services.AddSingleton<FriendGiftRepository>();
        builder.Services.AddSingleton<MailRepository>();
        builder.Services.AddSingleton<ChatRepository>();
        builder.Services.AddSingleton<WalletRepository>();
        builder.Services.AddSingleton<ShopRepository>();
        builder.Services.AddSingleton<NewbieGiftRepository>();
        builder.Services.AddSingleton<NewbieDropRepository>();
        builder.Services.AddSingleton<BagRepository>();
        builder.Services.AddSingleton<PasterRepository>();
        builder.Services.AddSingleton<PokemonUsedRepository>();
        builder.Services.AddSingleton<HeldItemRepository>();
        builder.Services.AddSingleton<BadgeRepository>();
        builder.Services.AddSingleton<AccountRepository>();
        builder.Services.AddSingleton<AccountTokenRepository>();
        builder.Services.AddSingleton<ActivityRepository>();
        builder.Services.AddSingleton<AchievementRepository>();
        builder.Services.AddSingleton<StarRoadRepository>();
        builder.Services.AddSingleton<PveChallengeRepository>();
        builder.Services.AddSingleton<ActivityExchangeRepository>();
        builder.Services.AddSingleton<CouponsFlowRepository>();
        builder.Services.AddSingleton<FirstChargeRepository>();
        builder.Services.AddSingleton<TrainerExpGiftRepository>();
        builder.Services.AddSingleton<ProfileRepository>();
        builder.Services.AddSingleton<FightHistoryRepository>();
        builder.Services.AddSingleton<SettingsRepository>();
        builder.Services.AddSingleton<NewbieChallengeRepository>();
        builder.Services.AddSingleton<TaskProcessRepository>();
        builder.Services.AddSingleton<BattleSettlementRepository>();
        builder.Services.AddSingleton<PveSettlementRepository>();
        builder.Services.AddSingleton<SettlementJournal>();
        builder.Services.AddSingleton<EnergyRepository>();
        builder.Services.AddSingleton<InviteFriendRepository>();
        builder.Services.AddSingleton<EeveeChallengeRepository>();
        builder.Services.AddSingleton<CreditRepository>();
        builder.Services.AddSingleton<AccusationRepository>();
        builder.Services.AddSingleton<WarmBattleRepository>();
        builder.Services.AddSingleton<FabulousRepository>();
        builder.Services.AddSingleton<RoomMicTipsRepository>();
        builder.Services.AddSingleton<RankedRepository>();
        builder.Services.AddSingleton<BattlePassRepository>();
        builder.Services.AddSingleton<RoleTmpNameRepository>();
        builder.Services.AddSingleton<CarryEquipmentRepository>();
        builder.Services.AddSingleton<ActivationCodeRepository>();
        builder.Services.AddSingleton<GiftCodeRepository>();
        builder.Services.AddSingleton<VideoHistoryRepository>();
        builder.Services.AddSingleton<GuildRepository>();
        builder.Services.AddSingleton<TenGamesRepository>();
        builder.Services.AddSingleton<CampBattleRepository>();
        builder.Services.AddSingleton<AdminRepository>();
        builder.Services.AddSingleton<ServerContentRepository>();
    }

    private static void AddGameData(IHostApplicationBuilder builder)
    {
        var gameData = GameData.Load(Path.Combine(AppContext.BaseDirectory, GameData.DirectoryName));
        CoinID.UseResIDTypeMatch(gameData.ResIDTypeMatchBase.Rows);
        builder.Services.AddSingleton(gameData);
        builder.Services.AddSingleton(GameNames.Load(Path.Combine(AppContext.BaseDirectory, GameNames.FilePath)));
    }

    private static void AddGame(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<BackgroundOperations>();
        builder.Services.AddHostedService(services => services.GetRequiredService<BackgroundOperations>());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<LobbyRoles>();
        builder.Services.AddSingleton<RelayRoles>();
        builder.Services.AddSingleton<LwipTickets>();
        builder.Services.AddSingleton<OffingRestartSessions>();
        builder.Services.AddSingleton<ServerLists>();
        builder.Services.AddHostedService(services => services.GetRequiredService<ServerLists>());
        builder.Services.AddSingleton<NoticeManager>();
        builder.Services.AddSingleton<LobbyNotices>();
        builder.Services.AddSingleton<LobbyBroadcast>();
        builder.Services.AddSingleton<LoginManager>();
        builder.Services.AddSingleton<FriendPresence>();
        builder.Services.AddSingleton<RoleBriefBuilder>();
        builder.Services.AddSingleton<FriendManager>();
        builder.Services.AddSingleton<FriendGiftManager>();
        builder.Services.AddSingleton<GuildInfoBuilder>();
        builder.Services.AddSingleton<GuildComments>();
        builder.Services.AddSingleton<GuildActivityManager>();
        builder.Services.AddSingleton<BossRushGuildActivity>();
        builder.Services.AddSingleton<RallyChallengeRepository>();
        builder.Services.AddSingleton<RallyChallengeManager>();
        builder.Services.AddSingleton<AllPkmChallengeRepository>();
        builder.Services.AddSingleton<AllPkmChallengeManager>();
        builder.Services.AddSingleton<TowerDefenseRepository>();
        builder.Services.AddSingleton<TowerDefenseManager>();
        builder.Services.AddSingleton<MegaEvolutionRepository>();
        builder.Services.AddSingleton<MegaEvolutionManager>();
        builder.Services.AddSingleton<EventBattleRecorder>();
        builder.Services.AddSingleton<GuildActivityTasks>();
        builder.Services.AddSingleton<GuildManager>();
        builder.Services.AddSingleton<MailManager>();
        builder.Services.AddSingleton<GrantUpdates>();
        builder.Services.AddSingleton<CreditManager>();
        builder.Services.AddSingleton<CreditPointTaskManager>();
        builder.Services.AddSingleton<AccusationManager>();
        builder.Services.AddSingleton<BattleVoiceManager>();
        builder.Services.AddSingleton<GVoiceRoomManager>();
        builder.Services.AddSingleton<RankedLadder>();
        builder.Services.AddSingleton<RankedRewards>();
        builder.Services.AddSingleton<RankedManager>();
        builder.Services.AddSingleton<BattlePassSeasons>();
        builder.Services.AddSingleton<BattlePassLeveling>();
        builder.Services.AddSingleton<BattlePassRewards>();
        builder.Services.AddSingleton<BattlePassManager>();
        builder.Services.AddSingleton<BattlePassTasks>();
        builder.Services.AddSingleton<BattlePassActivities>();
        builder.Services.AddSingleton<RankedRankingBoard>();
        builder.Services.AddSingleton<WarmBattleManager>();
        builder.Services.AddSingleton<ChatManager>();
        builder.Services.AddSingleton<RoleInfoStore>();
        builder.Services.AddSingleton<WalletManager>();
        builder.Services.AddSingleton<EnergyManager>();
        builder.Services.AddSingleton<InviteFriendManager>();
        builder.Services.AddSingleton<ShopManager>();
        builder.Services.AddSingleton<MysteryShopManager>();
        builder.Services.AddSingleton<BagManager>();
        builder.Services.AddSingleton<HeldItemManager>();
        builder.Services.AddSingleton<PokemonManager>();
        builder.Services.AddSingleton<BadgeManager>();
        builder.Services.AddSingleton<TmpNameManager>();
        builder.Services.AddSingleton<PokemonAvatarManager>();
        builder.Services.AddSingleton<ActivationCodeManager>();
        builder.Services.AddSingleton<GiftCodeManager>();
        builder.Services.AddSingleton<ActivitySchedule>();
        builder.Services.AddSingleton<ActivityManager>();
        builder.Services.AddSingleton<EeveeChallengePool>();
        builder.Services.AddSingleton<EeveeChallengeManager>();
        builder.Services.AddSingleton<CampBattleManager>();
        builder.Services.AddSingleton<MatchPredictionManager>();
        builder.Services.AddSingleton<TenGamesBPActivityManager>();
        builder.Services.AddSingleton<TenGamesSchedule>();
        builder.Services.AddSingleton<TenGamesTeamInfoBuilder>();
        builder.Services.AddSingleton<TenGamesManager>();
        builder.Services.AddSingleton<TenGamesBattleRecorder>();
        builder.Services.AddSingleton<AchievementManager>();
        builder.Services.AddSingleton<TaskUpdates>();
        builder.Services.AddSingleton<StarRoadManager>();
        builder.Services.AddSingleton<TrainerExpGiftManager>();
        builder.Services.AddSingleton<AppearanceGift>();
        builder.Services.AddSingleton<FightHistoryManager>();
        builder.Services.AddSingleton<VideoHistoryManager>();
        builder.Services.AddSingleton<VideoPlayback>();
        builder.Services.AddSingleton<ProfileManager>();
        builder.Services.AddSingleton<SettingsManager>();
        builder.Services.AddSingleton<AccountManager>();
        builder.Services.AddSingleton<AuthorizationCodeStore>();
        builder.Services.AddSingleton<AccountSessionManager>();
        builder.Services.AddSingleton<WebIdentity>();
        builder.Services.AddSingleton<AdminTokenManager>();
        builder.Services.AddSingleton<AdminAuditLog>();
        builder.Services.AddSingleton<AdminCatalog>();
        builder.Services.AddSingleton<AdminMail>();
        builder.Services.AddSingleton<AdminGiftCodes>();
        builder.Services.AddSingleton<AdminPlayers>();
        builder.Services.AddSingleton<AdminSettings>();
        builder.Services.AddSingleton<AdminServerState>();
        builder.Services.AddSingleton<AdminReports>();
        builder.Services.AddSingleton<AdminSquads>();
        builder.Services.AddSingleton<AdminGiveAll>();
        builder.Services.AddSingleton<RoomManager>();
        builder.Services.AddSingleton<RoomInvites>();
        builder.Services.AddSingleton<MatchScheduler>();
        builder.Services.AddHostedService(services => services.GetRequiredService<MatchScheduler>());
        builder.Services.AddSingleton<SettleTeamPresence>();
        builder.Services.AddSingleton<RoomMicTips>();
        builder.Services.AddSingleton<ActivityRewardNotices>();
        builder.Services.AddSingleton<ActivityProgressPushes>();
        builder.Services.AddSingleton<DeskManager>();
        builder.Services.AddSingleton<PveChallengeProgress>();
        builder.Services.AddSingleton<PveChallengeManager>();
        builder.Services.AddSingleton<ActivityExchangeManager>();
        builder.Services.AddSingleton<RechargeManager>();
        builder.Services.AddSingleton<FirstChargeManager>();
        builder.Services.AddSingleton<NewbieManager>();
        builder.Services.AddSingleton<NewbieGiftManager>();
        builder.Services.AddSingleton<NewbieChallengeManager>();
        builder.Services.AddSingleton<NewbieChallengeTaskManager>();
        builder.Services.AddSingleton<MvpScore>();
        builder.Services.AddSingleton<RankPointMedals>();
        builder.Services.AddSingleton<SettleFabulous>();
        builder.Services.AddSingleton<BattleSettlement>();
        builder.Services.AddSingleton<SettlementProcessor>();
        builder.Services.AddHostedService(services => services.GetRequiredService<SettlementProcessor>());
        builder.Services.AddSingleton<RelayManager>();
        builder.Services.AddSingleton<ObserveManager>();
        builder.Services.AddSingleton<ObserveBattleManager>();
    }

    private static void AddPackets(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton(services =>
            PacketDispatcher.Discover(typeof(PacketDispatcher).Assembly, services));

    private static void AddServers(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ResourceArchiveCatalog>();
        builder.Services.AddHostedService(services => services.GetRequiredService<ResourceArchiveCatalog>());
        builder.Services.AddSingleton<ImageCdnFolder>();
        builder.Services.AddHostedService(services => services.GetRequiredService<ImageCdnFolder>());
        builder.Services.AddHostedService<DirServer>();
        builder.Services.AddHostedService<LobbyServer>();
        builder.Services.AddHostedService<RelayServer>();
        builder.Services.AddSingleton<UpdateServiceReplies>();
        builder.Services.AddHostedService<UpdateServer>();
        builder.Services.AddHostedService<VoiceServer>();
        builder.Services.AddHostedService<LobbyRefreshTick>();
        builder.Services.AddHostedService<RedDotRecorder>();
        HttpServer.AddServices(builder.Services);
    }
}
