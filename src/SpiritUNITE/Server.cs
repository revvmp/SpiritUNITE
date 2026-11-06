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
using SpiritUNITE.Game.Achievement;
using SpiritUNITE.Game.Login;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.TrainerExpGift;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Auth;
using SpiritUNITE.Game.Chat;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Badge;
using SpiritUNITE.Game.HeldItem;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Game.Newbie;
using SpiritUNITE.Game.MSDKNotice;
using SpiritUNITE.Game.FightHistory;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.PveChallenge;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Game.Shop;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Logging;
using SpiritUNITE.Network;
using SpiritUNITE.Online;
using SpiritUNITE.Network.Lwip;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Handlers.Session;
using SpiritUNITE.Servers.Http;
using SpiritUNITE.Servers.Dir;
using SpiritUNITE.Servers.Lobby;
using SpiritUNITE.Servers.Relay;

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
            .Validate(options => options.IsValid(), "Wallet settings are invalid: starting balances must be 0 or more and FirstLoginSpecialCoins must map special coin ids to positive counts")
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

        builder.Services.AddOptions<BattleSettlementOptions>()
            .BindConfiguration(BattleSettlementOptions.SectionName)
            .Validate(options => options.IsValid(), "BattleSettlement settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<HeldItemOptions>()
            .BindConfiguration(HeldItemOptions.SectionName)
            .Validate(options => options.IsValid(), "HeldItem settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<GVoiceOptions>()
            .BindConfiguration(GVoiceOptions.SectionName)
            .Validate(options => options.IsValid(), "GVoice settings are invalid")
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
        builder.Services.AddSingleton<PokemonUsedRepository>();
        builder.Services.AddSingleton<HeldItemRepository>();
        builder.Services.AddSingleton<BadgeRepository>();
        builder.Services.AddSingleton<AccountRepository>();
        builder.Services.AddSingleton<AccountTokenRepository>();
        builder.Services.AddSingleton<ActivityRepository>();
        builder.Services.AddSingleton<AchievementRepository>();
        builder.Services.AddSingleton<StarRoadRepository>();
        builder.Services.AddSingleton<TrainerExpGiftRepository>();
        builder.Services.AddSingleton<ProfileRepository>();
        builder.Services.AddSingleton<FightHistoryRepository>();
        builder.Services.AddSingleton<SettingsRepository>();
        builder.Services.AddSingleton<NewbieChallengeRepository>();
        builder.Services.AddSingleton<TaskProcessRepository>();
        builder.Services.AddSingleton<BattleSettlementRepository>();
        builder.Services.AddSingleton<EeveeChallengeRepository>();
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
        builder.Services.AddSingleton<MailManager>();
        builder.Services.AddSingleton<ChatManager>();
        builder.Services.AddSingleton<RoleInfoStore>();
        builder.Services.AddSingleton<WalletManager>();
        builder.Services.AddSingleton<ShopManager>();
        builder.Services.AddSingleton<BagManager>();
        builder.Services.AddSingleton<HeldItemManager>();
        builder.Services.AddSingleton<PokemonManager>();
        builder.Services.AddSingleton<BadgeManager>();
        builder.Services.AddSingleton<ActivitySchedule>();
        builder.Services.AddSingleton<ActivityTasks>();
        builder.Services.AddSingleton<EeveeChallengePool>();
        builder.Services.AddSingleton<EeveeChallenge>();
        builder.Services.AddSingleton<AchievementTasks>();
        builder.Services.AddSingleton<TaskUpdates>();
        builder.Services.AddSingleton<StarRoadManager>();
        builder.Services.AddSingleton<TrainerExpGifts>();
        builder.Services.AddSingleton<AppearanceGift>();
        builder.Services.AddSingleton<FightHistoryRecords>();
        builder.Services.AddSingleton<ProfileManager>();
        builder.Services.AddSingleton<SettingsManager>();
        builder.Services.AddSingleton<LoginThrottle>();
        builder.Services.AddSingleton<AccountManager>();
        builder.Services.AddSingleton<AuthorizationCodeStore>();
        builder.Services.AddSingleton<AccountSessionManager>();
        builder.Services.AddSingleton<RoomManager>();
        builder.Services.AddSingleton<RoomInvites>();
        builder.Services.AddSingleton<DeskManager>();
        builder.Services.AddSingleton<PveChallengeProgress>();
        builder.Services.AddSingleton<NewbieManager>();
        builder.Services.AddSingleton<NewbieGiftManager>();
        builder.Services.AddSingleton<NewbieChallengeManager>();
        builder.Services.AddSingleton<NewbieChallengeTasks>();
        builder.Services.AddSingleton<BattleSettlement>();
        builder.Services.AddSingleton<RelayManager>();
    }

    private static void AddPackets(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton(services =>
            PacketDispatcher.Discover(typeof(PacketDispatcher).Assembly, services));

    private static void AddServers(IHostApplicationBuilder builder)
    {
        builder.Services.AddHostedService<DirServer>();
        builder.Services.AddHostedService<LobbyServer>();
        builder.Services.AddHostedService<RelayServer>();
        builder.Services.AddHostedService<LobbyRefreshTick>();
        builder.Services.AddHostedService<RedDotRecorder>();
        HttpServer.AddServices(builder.Services);
    }
}
