using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SpiritUNITE.Database;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Auth;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Logging;
using SpiritUNITE.Network;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Handlers;
using SpiritUNITE.Servers.Http;
using SpiritUNITE.Servers.Dir;
using SpiritUNITE.Servers.Lobby;

namespace SpiritUNITE;

public static class Server
{
    private const string LogFileTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u5}] {Message:lj}{NewLine}{Exception}";
    private const int RetainedLogFiles = 7;

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
            .BindConfiguration(TcpServerOptions.SectionName)
            .Validate(options => options.IsValid(), "TcpServer settings are invalid")
            .ValidateOnStart();

        builder.Services.AddOptions<LobbyAddressOptions>()
            .BindConfiguration(LobbyAddressOptions.SectionName)
            .Validate(options => options.IsValid(), "Lobby settings are invalid")
            .ValidateOnStart();
    }

    private static async Task AddDatabaseAsync(IHostApplicationBuilder builder)
    {
        var databaseOptions = await DatabaseManager.ConnectAsync(builder.Configuration["Database:ConnectionString"]);

        builder.Services.AddSingleton(databaseOptions);
        builder.Services.AddSingleton<IRoleRepository, RoleRepository>();
        builder.Services.AddSingleton<IAccountRepository, AccountRepository>();
        builder.Services.AddSingleton<IAccountTokenRepository, AccountTokenRepository>();
    }

    private static void AddGameData(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton(GameData.Load(Path.Combine(AppContext.BaseDirectory, GameData.DirectoryName)));

    private static void AddGame(IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<RoleInfoStore>();
        builder.Services.AddSingleton<LoginThrottle>();
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<AuthorizationCodeStore>();
        builder.Services.AddSingleton<AccountSessionService>();
    }

    private static void AddPackets(IHostApplicationBuilder builder) =>
        builder.Services.AddSingleton(services =>
            PacketDispatcher.Discover(typeof(DirHandlers).Assembly, services));

    private static void AddServers(IHostApplicationBuilder builder)
    {
        builder.Services.AddHostedService<DirServer>();
        builder.Services.AddHostedService<LobbyServer>();
        HttpServer.AddServices(builder.Services);
    }
}
