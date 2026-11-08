using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SpiritUNITE.Battle;
using SpiritUNITE.Database;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Credit;
using SpiritUNITE.Game.Achievement;
using SpiritUNITE.Game.ActivationCode;
using SpiritUNITE.Game.Login;
using SpiritUNITE.Game.Match;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.PokemonAvatar;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.TrainerExpGift;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Recharge;
using SpiritUNITE.Game.TenGames;
using SpiritUNITE.Game.BattleVoice;
using SpiritUNITE.Game.Accusation;
using SpiritUNITE.Auth;
using SpiritUNITE.Game.Chat;
using SpiritUNITE.Game.Energy;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Badge;
using SpiritUNITE.Game.BattlePass;
using SpiritUNITE.Game.HeldItem;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Guild;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Game.Newbie;
using SpiritUNITE.Game.Observe;
using SpiritUNITE.Game.MSDKNotice;
using SpiritUNITE.Game.FightHistory;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.PveChallenge;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Game.Shop;
using SpiritUNITE.Game.Video;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Logging;
using SpiritUNITE.Network;
using SpiritUNITE.Online;
using SpiritUNITE.Network.Lwip;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Handlers.Session;
using SpiritUNITE.Resource;
using SpiritUNITE.Servers.Http;
using SpiritUNITE.Servers.Dir;
using SpiritUNITE.Servers.Lobby;
using SpiritUNITE.Servers.Relay;
using SpiritUNITE.Servers.Update;

namespace SpiritUNITE;

public static class Server
{
    private const string LogFileTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u5}] {Message:lj}{NewLine}{Exception}";
    private const int RetainedLogFiles = 7;
    private const string GameSettingsFile = "gamesettings.json";

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
            .Validate(options => options.IsValid(), "LobbyRules settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<WalletOptions>()
            .BindConfiguration(WalletOptions.SectionName)
            .Validate(options => options.IsValid(), "Wallet settings are invalid: starting balances must be 0 or more")
            .ValidateOnStart();

        builder.Services.AddOptions<PokemonOptions>()
            .BindConfiguration(PokemonOptions.SectionName)
            .Validate(options => options.IsValid(), "Pokemon settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<NoticeOptions>()
            .BindConfiguration(NoticeOptions.SectionName)
            .Validate(options => options.IsValid(), "MSDKNotice settings are invalid")
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
            .ValidateOnStart();

        builder.Services.AddOptions<BattleSettlementOptions>()
            .BindConfiguration(BattleSettlementOptions.SectionName)
            .Validate(options => options.IsValid(), "BattleSettlement settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<HeroPickOptions>()
            .BindConfiguration(HeroPickOptions.SectionName)
            .Validate(options => options.IsValid(), "HeroPick settings are invalid: NpcHeroMaxReleaseOrder must be 0 or more")
            .ValidateOnStart();

        builder.Services.AddOptions<ActivationCodeOptions>()
            .BindConfiguration(ActivationCodeOptions.SectionName)
            .Validate(options => options.IsValid(), "ActivationCode settings are invalid: codes must be distinct ASCII letters or digits of at most 32 characters, with a yyyyMMddHHmmss ValidEndTime or 0 and a MaxUseCnt of 0 or more")
            .ValidateOnStart();

        builder.Services.AddOptions<HeldItemOptions>()
            .BindConfiguration(HeldItemOptions.SectionName)
            .Validate(options => options.IsValid(), "HeldItem settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<GVoiceOptions>()
            .BindConfiguration(GVoiceOptions.SectionName)
            .Validate(options => options.IsValid(), "GVoice settings are invalid")
            .ValidateOnStart();

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
        builder.Services.AddSingleton<CouponsFlowRepository>();
        builder.Services.AddSingleton<PveChallengeRepository>();
        builder.Services.AddSingleton<ActivityExchangeRepository>();
        builder.Services.AddSingleton<FirstChargeRepository>();
        builder.Services.AddSingleton<InviteFriendRepository>();
        builder.Services.AddSingleton<TenGamesRepository>();
        builder.Services.AddSingleton<CampBattleRepository>();
        builder.Services.AddSingleton<AccusationRepository>();
        builder.Services.AddSingleton<FabulousRepository>();
        builder.Services.AddSingleton<AchievementRepository>();
        builder.Services.AddSingleton<StarRoadRepository>();
        builder.Services.AddSingleton<TrainerExpGiftRepository>();
        builder.Services.AddSingleton<ProfileRepository>();
        builder.Services.AddSingleton<FightHistoryRepository>();
        builder.Services.AddSingleton<SettingsRepository>();
        builder.Services.AddSingleton<NewbieChallengeRepository>();
        builder.Services.AddSingleton<TaskProcessRepository>();
        builder.Services.AddSingleton<BattleSettlementRepository>();
        builder.Services.AddSingleton<EnergyRepository>();
        builder.Services.AddSingleton<EeveeChallengeRepository>();
        builder.Services.AddSingleton<CreditRepository>();
        builder.Services.AddSingleton<WarmBattleRepository>();
        builder.Services.AddSingleton<RankedRepository>();
        builder.Services.AddSingleton<BattlePassRepository>();
        builder.Services.AddSingleton<RoleTmpNameRepository>();
        builder.Services.AddSingleton<CarryEquipmentRepository>();
        builder.Services.AddSingleton<ActivationCodeRepository>();
        builder.Services.AddSingleton<VideoHistoryRepository>();
        builder.Services.AddSingleton<GuildRepository>();
    }

    private static void AddGameData(IHostApplicationBuilder builder)
    {
        var gameData = GameData.Load(Path.Combine(AppContext.BaseDirectory, GameData.DirectoryName));
        CoinID.UseResIDTypeMatch(gameData.ResIDTypeMatchBase.Rows);
        builder.Services.AddSingleton(gameData);
    }

    private static void AddGame(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<LobbyRoles>();
        builder.Services.AddSingleton<RelayRoles>();
        builder.Services.AddSingleton<LwipTickets>();
        builder.Services.AddSingleton<OffingRestartSessions>();
        builder.Services.AddSingleton<PendingLoginFinishes>();
        builder.Services.AddSingleton<LobbyNotices>();
        builder.Services.AddSingleton<LoginManager>();
        builder.Services.AddSingleton<FriendPresence>();
        builder.Services.AddSingleton<RoleBriefBuilder>();
        builder.Services.AddSingleton<FriendManager>();
        builder.Services.AddSingleton<GuildInfoBuilder>();
        builder.Services.AddSingleton<GuildComments>();
        builder.Services.AddSingleton<GuildActivityManager>();
        builder.Services.AddSingleton<GuildActivityTasks>();
        builder.Services.AddSingleton<GuildManager>();
        builder.Services.AddSingleton<MailManager>();
        builder.Services.AddSingleton<CreditManager>();
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
        builder.Services.AddSingleton<ShopManager>();
        builder.Services.AddSingleton<BagManager>();
        builder.Services.AddSingleton<HeldItemManager>();
        builder.Services.AddSingleton<PokemonManager>();
        builder.Services.AddSingleton<BadgeManager>();
        builder.Services.AddSingleton<TmpNameManager>();
        builder.Services.AddSingleton<PokemonAvatarManager>();
        builder.Services.AddSingleton<ActivationCodeManager>();
        builder.Services.AddSingleton<ActivitySchedule>();
        builder.Services.AddSingleton<ActivityManager>();
        builder.Services.AddSingleton<AccusationManager>();
        builder.Services.AddSingleton<BattleVoiceManager>();
        builder.Services.AddSingleton<SettleFabulous>();
        builder.Services.AddSingleton<MysteryShopManager>();
        builder.Services.AddSingleton<ActivityProgressPushes>();
        builder.Services.AddSingleton<PveChallengeManager>();
        builder.Services.AddSingleton<ActivityExchangeManager>();
        builder.Services.AddSingleton<RechargeManager>();
        builder.Services.AddSingleton<FirstChargeManager>();
        builder.Services.AddSingleton<InviteFriendManager>();
        builder.Services.AddSingleton<CampBattleManager>();
        builder.Services.AddSingleton<MatchPredictionManager>();
        builder.Services.AddSingleton<TenGamesBPActivityManager>();
        builder.Services.AddSingleton<TenGamesSchedule>();
        builder.Services.AddSingleton<TenGamesTeamInfoBuilder>();
        builder.Services.AddSingleton<TenGamesManager>();
        builder.Services.AddSingleton<TenGamesBattleRecorder>();
        builder.Services.AddSingleton<EeveeChallengePool>();
        builder.Services.AddSingleton<EeveeChallengeManager>();
        builder.Services.AddSingleton<AchievementManager>();
        builder.Services.AddSingleton<TaskUpdates>();
        builder.Services.AddSingleton<StarRoadManager>();
        builder.Services.AddSingleton<TrainerExpGiftManager>();
        builder.Services.AddSingleton<AppearanceGift>();
        builder.Services.AddSingleton<FightHistoryManager>();
        builder.Services.AddSingleton<VideoHistoryManager>();
        builder.Services.AddSingleton<ProfileManager>();
        builder.Services.AddSingleton<SettingsManager>();
        builder.Services.AddSingleton<AccountManager>();
        builder.Services.AddSingleton<AuthorizationCodeStore>();
        builder.Services.AddSingleton<AccountSessionManager>();
        builder.Services.AddSingleton<RoomManager>();
        builder.Services.AddSingleton<RoomInvites>();
        builder.Services.AddSingleton<MatchScheduler>();
        builder.Services.AddHostedService(services => services.GetRequiredService<MatchScheduler>());
        builder.Services.AddSingleton<SettleTeamPresence>();
        builder.Services.AddSingleton<ActivityRewardNotices>();
        builder.Services.AddSingleton<DeskManager>();
        builder.Services.AddSingleton<PveChallengeProgress>();
        builder.Services.AddSingleton<NewbieManager>();
        builder.Services.AddSingleton<NewbieGiftManager>();
        builder.Services.AddSingleton<NewbieChallengeManager>();
        builder.Services.AddSingleton<NewbieChallengeTaskManager>();
        builder.Services.AddSingleton<MvpScore>();
        builder.Services.AddSingleton<RankPointMedals>();
        builder.Services.AddSingleton<BattleSettlement>();
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
        builder.Services.AddHostedService<LobbyRefreshTick>();
        builder.Services.AddHostedService<RedDotRecorder>();
        HttpServer.AddServices(builder.Services);
    }
}
