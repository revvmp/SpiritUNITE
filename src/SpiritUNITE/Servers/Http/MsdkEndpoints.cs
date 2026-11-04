using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Auth;

namespace SpiritUNITE.Servers.Http;

public static class MsdkEndpoints
{
    public const int GuestChannelId = 3;
    public const int PokemonChannelId = 24;
    public const int Nintendo_Unite = 26;

    private const string LoginPath = "/v2/auth/login";
    private const string CheckAndLoginPath = "/v2/auth/check_and_login";
    private const string AutoLoginPath = "/v2/auth/auto_login";
    private const string UnsupportedPath = "/v2/{**action}";
    private const int MaximumBodyBytes = 64 * 1024;
    private const int Succeeded = 0;
    private const int Refused = 1;
    private const int FirstLogin = 1;
    private const int ReturningLogin = 0;
    private const string ChannelInfoKey = "channel_info";
    private const string GuestIdKey = "uuid";
    private const string GuestDisplayName = "guest";

    public static void Map(WebApplication app)
    {
        app.MapPost(LoginPath, LoginAsync).DisableAntiforgery();
        app.MapPost(CheckAndLoginPath, LoginAsync).DisableAntiforgery();
        app.MapPost(AutoLoginPath, AutoLoginAsync).DisableAntiforgery();
        app.Map(UnsupportedPath, Unsupported);
    }

    private static async Task<IResult> LoginAsync(HttpRequest request, AccountSessionManager sessions, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(MsdkEndpoints));
        var query = MsdkQuery.Read(request);
        if (!IsLoginChannel(query.ChannelId))
        {
            logger.LogInformation("MSDK login refused: channel {ChannelId} is not supported", query.ChannelId);
            return Refuse(query, "channel not supported");
        }

        using var body = await ReadBodyAsync(request);
        var channelInfo = body is null ? null : ReadObject(body.RootElement, ChannelInfoKey);
        return query.ChannelId == GuestChannelId
            ? await GuestLoginAsync(query, channelInfo, request.HttpContext.Connection.RemoteIpAddress, sessions, logger)
            : await AuthorizationCodeLoginAsync(query, channelInfo, sessions, logger);
    }

    private static async Task<IResult> GuestLoginAsync(
        MsdkQuery query,
        JsonElement? channelInfo,
        IPAddress? clientAddress,
        AccountSessionManager sessions,
        ILogger logger)
    {
        if ((channelInfo is { } info ? ReadString(info, GuestIdKey) : null) is not { } guestId)
        {
            logger.LogInformation("MSDK guest login refused: no guest id");
            return Refuse(query, "missing uuid");
        }

        var login = await sessions.GuestLoginAsync(guestId, clientAddress);
        if (login.Session is not { } session)
        {
            logger.LogInformation("MSDK guest login refused from {Address}: {Result}, guest id {GuestIdLength} characters",
                clientAddress, login.Result, guestId.Length);
            return Refuse(query, login.Result == GuestLoginResult.Throttled ? "too many new guests" : "invalid uuid");
        }

        logger.LogInformation("MSDK guest login issued a token to guest account {AccountId}, new account {IsNewAccount}",
            session.Account.AccountId, session.IsNewAccount);
        return Succeed(query, session);
    }

    private static async Task<IResult> AuthorizationCodeLoginAsync(
        MsdkQuery query,
        JsonElement? channelInfo,
        AccountSessionManager sessions,
        ILogger logger)
    {
        var code = channelInfo is { } info ? ReadString(info, "code") : null;
        if (code is null)
        {
            logger.LogInformation("MSDK login refused: no authorization code");
            return Refuse(query, "missing code");
        }

        if (await sessions.ExchangeCodeAsync(code, ReadString(channelInfo!.Value, "code_verifier")) is not { } session)
        {
            logger.LogInformation("MSDK login refused on channel {ChannelId}: authorization code is invalid, used or expired", query.ChannelId);
            return Refuse(query, "invalid code");
        }

        logger.LogInformation("MSDK login on channel {ChannelId} issued a token to account {AccountId} ({UserName})",
            query.ChannelId, session.Account.AccountId, session.Account.UserName);
        return Succeed(query, session);
    }

    private static async Task<IResult> AutoLoginAsync(HttpRequest request, AccountSessionManager sessions, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(MsdkEndpoints));
        var query = MsdkQuery.Read(request);
        using var body = await ReadBodyAsync(request);
        var openId = body is null ? null : ReadString(body.RootElement, "openid");
        var token = body is null ? null : ReadString(body.RootElement, "token");
        if (!IsLoginChannel(query.ChannelId) || openId is null || token is null ||
            await sessions.ResumeAsync(openId, token) is not { } session ||
            session.Account.IsGuest != (query.ChannelId == GuestChannelId))
        {
            logger.LogInformation("MSDK auto login refused for channel {ChannelId}", query.ChannelId);
            return Refuse(query, "login expired");
        }

        logger.LogInformation("MSDK auto login renewed account {AccountId} ({UserName})",
            session.Account.AccountId, session.Account.UserName ?? GuestDisplayName);
        return Succeed(query, session);
    }

    private static IResult Unsupported(HttpRequest request, ILoggerFactory loggers)
    {
        loggers.CreateLogger(typeof(MsdkEndpoints)).LogInformation("MSDK call {Method} {Path} is not supported", request.Method, request.Path);
        return Refuse(MsdkQuery.Read(request), "not supported");
    }

    private static bool IsLoginChannel(int channelId) => channelId is GuestChannelId or PokemonChannelId or Nintendo_Unite;

    private static IResult Succeed(MsdkQuery query, AccountSession session) => Results.Json(new MsdkLoginReply
    {
        Ret = Succeeded,
        Msg = "success",
        Seq = query.Seq,
        ChannelId = query.ChannelId,
        OpenId = AccountSessionManager.ToOpenId(session.Account.AccountId),
        Token = session.Token,
        TokenExpireTime = session.ExpiresAt,
        First = session.IsNewAccount ? FirstLogin : ReturningLogin,
        UserName = session.Account.UserName ?? string.Empty,
    });

    private static IResult Refuse(MsdkQuery query, string message) => Results.Json(new MsdkLoginReply
    {
        Ret = Refused,
        Msg = message,
        Seq = query.Seq,
        ChannelId = query.ChannelId,
    });

    private static async Task<JsonDocument?> ReadBodyAsync(HttpRequest request)
    {
        if (request.ContentLength > MaximumBodyBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await request.Body.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > MaximumBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        try
        {
            var document = JsonDocument.Parse(buffer.ToArray());
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document;
            }

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? ReadObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        try
        {
            using var nested = JsonDocument.Parse(value.GetString()!);
            return nested.RootElement.ValueKind == JsonValueKind.Object ? nested.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private readonly record struct MsdkQuery(int ChannelId, string Seq)
    {
        public static MsdkQuery Read(HttpRequest request) => new(
            int.TryParse(request.Query["channelid"], NumberStyles.None, CultureInfo.InvariantCulture, out var channelId) ? channelId : 0,
            request.Query["seq"].Count == 1 ? request.Query["seq"].ToString() : string.Empty);
    }

    private sealed class MsdkLoginReply
    {
        [JsonPropertyName("ret")]
        public int Ret { get; init; }

        [JsonPropertyName("msg")]
        public string Msg { get; init; } = string.Empty;

        [JsonPropertyName("seq")]
        public string Seq { get; init; } = string.Empty;

        [JsonPropertyName("channelid")]
        public int ChannelId { get; init; }

        [JsonPropertyName("openid")]
        public string OpenId { get; init; } = string.Empty;

        [JsonPropertyName("token")]
        public string Token { get; init; } = string.Empty;

        [JsonPropertyName("token_expire_time")]
        public long TokenExpireTime { get; init; }

        [JsonPropertyName("first")]
        public int First { get; init; }

        [JsonPropertyName("user_name")]
        public string UserName { get; init; } = string.Empty;

        [JsonPropertyName("need_name_auth")]
        public bool NeedNameAuth { get; init; }

        [JsonPropertyName("channel_info")]
        public JsonElement ChannelInfo { get; init; } = EmptyObject;

        [JsonPropertyName("pf")]
        public string Pf { get; init; } = string.Empty;

        [JsonPropertyName("pf_key")]
        public string PfKey { get; init; } = string.Empty;

        [JsonPropertyName("reg_channel_dis")]
        public string RegChannelDis { get; init; } = string.Empty;

        [JsonPropertyName("picture_url")]
        public string PictureUrl { get; init; } = string.Empty;

        [JsonPropertyName("gender")]
        public int Gender { get; init; }

        [JsonPropertyName("birthdate")]
        public string Birthdate { get; init; } = string.Empty;

        [JsonPropertyName("bind_list")]
        public string BindList { get; init; } = string.Empty;

        private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();
    }
}
