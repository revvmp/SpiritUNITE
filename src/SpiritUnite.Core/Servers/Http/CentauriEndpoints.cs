using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SpiritUnite.Servers.Http;

public static class CentauriEndpoints
{
    private const string MpInfoPath = "/v1/r/{offerID}/mobile_mp_info";
    private const string LogDataPath = "/cgi-bin/log_data.fcg";
    private const string HeartbeatPath = "/heartbeat/overseas_common_android";
    private const int Succeeded = 0;
    private const string NoErrorCode = "0";

    public static void Map(WebApplication app)
    {
        app.Map(MpInfoPath, MpInfo).DisableAntiforgery();
        app.MapPost(LogDataPath, Acknowledge).DisableAntiforgery();
        app.MapPost(HeartbeatPath, Acknowledge).DisableAntiforgery();
    }

    private static IResult Acknowledge() => Results.Json(new CentauriAck { Ret = Succeeded });

    private static IResult MpInfo(ILoggerFactory loggers)
    {
        loggers.CreateLogger(typeof(CentauriEndpoints)).LogInformation("Centauri mp info answered with no active offers");
        return Results.Json(new CentauriMpReply { Ret = Succeeded, ErrCode = NoErrorCode });
    }

    private sealed class CentauriMpReply
    {
        [JsonPropertyName("ret")]
        public required int Ret { get; init; }

        [JsonPropertyName("err_code")]
        public required string ErrCode { get; init; }
    }

    private sealed class CentauriAck
    {
        [JsonPropertyName("ret")]
        public required int Ret { get; init; }
    }
}
