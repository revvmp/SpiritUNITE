using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SpiritUNITE.Database;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Logging;
using SpiritUNITE.Network;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Handlers;
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
        AddGame(builder);
        AddPackets(builder);
        AddServers(builder);

        await builder.Build().RunAsync();
    }

    private static HostApplicationBuilder CreateBuilder(string[] args) =>
        Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

    private static void AddLogging(HostApplicationBuilder builder) =>
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

    private static void AddSettings(HostApplicationBuilder builder)
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

    private static async Task AddDatabaseAsync(HostApplicationBuilder builder)
    {
        var databaseOptions = await DatabaseManager.ConnectAsync(builder.Configuration["Database:ConnectionString"]);

        builder.Services.AddSingleton(databaseOptions);
        builder.Services.AddSingleton<IRoleRepository, RoleRepository>();
    }

    private static void AddGame(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<RoleInfoStore>();
    }

    private static void AddPackets(HostApplicationBuilder builder) =>
        builder.Services.AddSingleton(services =>
            PacketDispatcher.Discover(typeof(DirHandlers).Assembly, services));

    private static void AddServers(HostApplicationBuilder builder)
    {
        builder.Services.AddHostedService<DirServer>();
        builder.Services.AddHostedService<LobbyServer>();
    }
}
