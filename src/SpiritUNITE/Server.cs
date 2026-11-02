using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using SpiritUNITE.Network;
using SpiritUNITE.Protocol;
using SpiritUNITE.Handlers;
using SpiritUNITE.Logging;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddSerilog(logger => logger
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .WriteTo.Async(sink => sink.Console(new SpiritConsoleFormatter())));

builder.Services.AddOptions<TcpServerOptions>()
    .BindConfiguration(TcpServerOptions.SectionName)
    .Validate(options => options.IsValid(), "TcpServer settings are invalid")
    .ValidateOnStart();

builder.Services.AddOptions<LobbyAddressOptions>()
    .BindConfiguration(LobbyAddressOptions.SectionName)
    .Validate(options => options.IsValid(), "Lobby settings are invalid")
    .ValidateOnStart();

builder.Services.AddSingleton(services => PacketDispatcher.Discover(typeof(DirHandlers).Assembly, services));
builder.Services.AddHostedService<TcpServer>();

await builder.Build().RunAsync();
