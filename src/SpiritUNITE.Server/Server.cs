using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using SpiritUNITE.Network;

const string ConsoleTemplate =
    "{Timestamp:HH:mm:ss.fff} {Level:u3} {SourceContext:l} {Message:lj}{NewLine}{Exception}";

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddSerilog(logger => logger
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .WriteTo.Async(sink => sink.Console(outputTemplate: ConsoleTemplate, theme: AnsiConsoleTheme.Code)));

builder.Services.AddOptions<TcpServerOptions>()
    .BindConfiguration(TcpServerOptions.SectionName)
    .Validate(options => options.IsValid(), "TcpServer settings are invalid")
    .ValidateOnStart();

builder.Services.AddHostedService<TcpServer>();

await builder.Build().RunAsync();
