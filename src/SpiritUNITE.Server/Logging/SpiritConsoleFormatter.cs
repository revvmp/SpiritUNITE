using Serilog.Events;
using Serilog.Formatting;
using SpiritUNITE.Network;

namespace SpiritUNITE.Server.Logging;

public sealed class SpiritConsoleFormatter : ITextFormatter
{
    private const string Reset = "\u001b[0m";
    private const string Gray = "\u001b[38;5;245m";
    private const string White = "\u001b[38;5;252m";
    private const string Cyan = "\u001b[38;5;45m";
    private const string Yellow = "\u001b[38;5;220m";
    private const string Red = "\u001b[38;5;196m";
    private const string Green = "\u001b[38;5;114m";
    private const string Magenta = "\u001b[38;5;177m";

    public void Format(LogEvent logEvent, TextWriter output)
    {
        output.Write($"{Gray}{logEvent.Timestamp:HH:mm:ss.fff}{Reset} ");
        output.Write($"{LevelColor(logEvent.Level)}[{LevelName(logEvent.Level)}]{Reset} ");
        output.Write($"{MessageColor(logEvent)}{logEvent.RenderMessage().Replace("\"", string.Empty)}{Reset}");
        output.WriteLine();

        if (logEvent.Exception is not null)
        {
            output.WriteLine($"{Red}{logEvent.Exception}{Reset}");
        }
    }

    private static string MessageColor(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue(PacketLog.DirectionProperty, out var direction))
        {
            return White;
        }

        return direction is ScalarValue { Value: PacketLog.Inbound } ? Green : Magenta;
    }

    private static string LevelColor(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose or LogEventLevel.Debug => Gray,
        LogEventLevel.Information => Cyan,
        LogEventLevel.Warning => Yellow,
        _ => Red,
    };

    private static string LevelName(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "TRACE",
        LogEventLevel.Debug => "DEBUG",
        LogEventLevel.Information => "INFO ",
        LogEventLevel.Warning => "WARN ",
        LogEventLevel.Error => "ERROR",
        _ => "FATAL",
    };
}
