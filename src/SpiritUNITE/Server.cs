using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SpiritUNITE.Network;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Handlers;
using SpiritUNITE.Servers.Dir;
using SpiritUNITE.Servers.Lobby;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Logging;

const string FileTemplate = "{Timestamp:HH:mm:ss.fff} [{Level:u5}] {Message:lj}{NewLine}{Exception}";
const int RetainedLogFiles = 7;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddSerilog(logger => logger
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .WriteTo.Async(sink => sink.Console(new SpiritConsoleFormatter()))
    .WriteTo.Async(sink => sink.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "spiritunite-.log"),
        outputTemplate: FileTemplate,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: RetainedLogFiles,
        shared: true)));

builder.Services.AddOptions<TcpServerOptions>()
    .BindConfiguration(TcpServerOptions.SectionName)
    .Validate(options => options.IsValid(), "TcpServer settings are invalid")
    .ValidateOnStart();

builder.Services.AddOptions<LobbyAddressOptions>()
    .BindConfiguration(LobbyAddressOptions.SectionName)
    .Validate(options => options.IsValid(), "Lobby settings are invalid")
    .ValidateOnStart();

builder.Services.AddSingleton(services => PacketDispatcher.Discover(typeof(DirHandlers).Assembly, services));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RoleInfoStore>();
builder.Services.AddHostedService<DirServer>();
builder.Services.AddHostedService<LobbyServer>();

await builder.Build().RunAsync();
