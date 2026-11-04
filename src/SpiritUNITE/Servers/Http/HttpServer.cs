using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SpiritUNITE.Auth;
using SpiritUNITE.Network;

namespace SpiritUNITE.Servers.Http;

public static class HttpServer
{
    private const string FormRateLimitPolicy = "account-form";
    private const string HtmlContentType = "text/html; charset=utf-8";
    private const string LoginField = "login";
    private const string UserNameField = "user_name";
    private const string EmailField = "email";
    private const string PasswordField = "password";
    private const string PasswordConfirmationField = "password_confirmation";
    private const string CrossSiteFetch = "cross-site";
    private const string NoStore = "no-store";
    private const string NoSniff = "nosniff";
    private const string DenyFraming = "DENY";
    private const string ReferrerPolicyHeader = "Referrer-Policy";
    private const string NoReferrer = "no-referrer";
    private const int MaximumRequestBodyBytes = 64 * 1024;

    public static void AddServices(IServiceCollection services)
    {
        services.AddOptions<HttpServerOptions>()
            .BindConfiguration(HttpServerOptions.SectionName)
            .Validate(options => options.IsValid(), "HttpServer settings are invalid")
            .ValidateOnStart();

        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<HttpServerOptions>>().Value;
            return new PasswordHashing(options.PasswordHashSlots, options.QueuedPasswordHashes);
        });

        services.Configure<KestrelServerOptions>(kestrel => kestrel.Limits.MaxRequestBodySize = MaximumRequestBodyBytes);

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<HttpServerOptions>>((forwarded, server) =>
        {
            forwarded.ForwardedHeaders = server.Value.TrustedProxies.Length > 0
                ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                : ForwardedHeaders.None;
            forwarded.KnownProxies.Clear();
            forwarded.KnownIPNetworks.Clear();
            foreach (var proxy in server.Value.TrustedProxies)
            {
                if (HttpServerOptions.TryParseProxyNetwork(proxy, out var network))
                {
                    forwarded.KnownIPNetworks.Add(network);
                }
            }
        });

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(FormRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                ClientAddress.Partition(context.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IOptions<HttpServerOptions>>().Value.FormSubmissionsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });
    }

    public static void Map(WebApplication app)
    {
        app.UseForwardedHeaders();
        app.Use(ApplySecurityHeaders);
        app.UseRateLimiter();
        app.MapGet(AccountPages.LoginPath, ShowLogin);
        app.MapPost(AccountPages.LoginPath, SubmitLogin).RequireRateLimiting(FormRateLimitPolicy).DisableAntiforgery();
        app.MapGet(AccountPages.NintendoLoginPath, ShowLogin);
        app.MapPost(AccountPages.NintendoLoginPath, SubmitLogin).RequireRateLimiting(FormRateLimitPolicy).DisableAntiforgery();
        app.MapGet(AccountPages.RegisterPath, ShowRegister);
        app.MapPost(AccountPages.RegisterPath, SubmitRegister).RequireRateLimiting(FormRateLimitPolicy).DisableAntiforgery();
        MsdkEndpoints.Map(app);
        CentauriEndpoints.Map(app);
    }

    private static IResult ShowLogin(HttpRequest request, IOptions<HttpServerOptions> options) =>
        ReadAuthorizationRequest(request.Query, options.Value) is { } authorization
            ? Html(AccountPages.Login(authorization, string.Empty, null))
            : InvalidRequest();

    private static IResult ShowRegister(HttpRequest request, IOptions<HttpServerOptions> options) =>
        ReadAuthorizationRequest(request.Query, options.Value) is { } authorization
            ? Html(AccountPages.Register(authorization, string.Empty, string.Empty, null))
            : InvalidRequest();

    private static async Task<IResult> SubmitLogin(
        HttpRequest request,
        IOptions<HttpServerOptions> options,
        AccountManager accounts,
        AuthorizationCodeStore codes,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (await ReadSameSiteFormAsync(request, cancellationToken) is not { } form ||
            ReadAuthorizationRequest(form, options.Value) is not { } authorization)
        {
            return InvalidRequest();
        }

        var login = SingleValue(form[LoginField]) ?? string.Empty;
        var clientAddress = request.HttpContext.Connection.RemoteIpAddress;
        var attempt = await accounts.AuthenticateAsync(login, SingleValue(form[PasswordField]) ?? string.Empty, clientAddress, cancellationToken);
        var logger = loggers.CreateLogger(typeof(HttpServer));
        if (attempt.Account is not { } account)
        {
            logger.LogInformation("Account login {Result} from {Address}", attempt.Result, clientAddress);
            return Html(AccountPages.Login(authorization, login, AccountPages.LoginError(attempt.Result)), LoginStatusCode(attempt.Result));
        }

        logger.LogInformation("Account {AccountId} ({UserName}) logged in on the account page", account.AccountId, account.UserName);
        return CompleteAuthorization(authorization, account, isNewAccount: false, codes);
    }

    private static async Task<IResult> SubmitRegister(
        HttpRequest request,
        IOptions<HttpServerOptions> options,
        AccountManager accounts,
        AuthorizationCodeStore codes,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (await ReadSameSiteFormAsync(request, cancellationToken) is not { } form ||
            ReadAuthorizationRequest(form, options.Value) is not { } authorization)
        {
            return InvalidRequest();
        }

        var userName = SingleValue(form[UserNameField]) ?? string.Empty;
        var email = SingleValue(form[EmailField]) ?? string.Empty;
        var creation = await accounts.RegisterAsync(
            userName,
            email,
            SingleValue(form[PasswordField]) ?? string.Empty,
            SingleValue(form[PasswordConfirmationField]) ?? string.Empty,
            cancellationToken);
        var logger = loggers.CreateLogger(typeof(HttpServer));
        if (creation.Account is not { } account)
        {
            logger.LogInformation("Account registration refused: {Result}", creation.Result);
            var statusCode = creation.Result == AccountRegistrationResult.Busy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;
            return Html(AccountPages.Register(authorization, userName, email, AccountPages.RegistrationError(creation.Result)), statusCode);
        }

        logger.LogInformation("Account {AccountId} ({UserName}) created", account.AccountId, account.UserName);
        return CompleteAuthorization(authorization, account, isNewAccount: true, codes);
    }

    private static IResult CompleteAuthorization(
        AuthorizationRequest authorization,
        PlayerAccount account,
        bool isNewAccount,
        AuthorizationCodeStore codes)
    {
        var result = new Dictionary<string, string?>
        {
            [AuthorizationRequest.CodeParameter] = codes.Issue(account.AccountId, isNewAccount, authorization),
            [AuthorizationRequest.StateParameter] = authorization.State,
            [AuthorizationRequest.ScopeParameter] = authorization.Scope,
        };
        var presentResult = result.Where(pair => pair.Value is not null).ToDictionary();
        var fallbackRedirect = QueryHelpers.AddQueryString(authorization.RedirectUri, presentResult);
        return Html(AccountPages.Complete(JsonSerializer.Serialize(presentResult), fallbackRedirect));
    }

    private static AuthorizationRequest? ReadAuthorizationRequest(
        IEnumerable<KeyValuePair<string, StringValues>> source,
        HttpServerOptions options)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, values) in source)
        {
            if (!AuthorizationRequest.ParameterNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            if (SingleValue(values) is not { } value)
            {
                return null;
            }

            parameters[name] = value;
        }

        return AuthorizationRequest.TryCreate(parameters, options.RedirectUris, HttpServerOptions.CodeChallengeOptionalRedirectUri);
    }

    private static async Task<IFormCollection?> ReadSameSiteFormAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType || request.Headers["Sec-Fetch-Site"] == CrossSiteFetch)
        {
            return null;
        }

        try
        {
            return await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static int LoginStatusCode(AccountLoginResult result) => result switch
    {
        AccountLoginResult.Throttled => StatusCodes.Status429TooManyRequests,
        AccountLoginResult.Busy => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status200OK,
    };

    private static Task ApplySecurityHeaders(HttpContext context, RequestDelegate next)
    {
        var headers = context.Response.Headers;
        headers.CacheControl = NoStore;
        headers.XContentTypeOptions = NoSniff;
        headers.XFrameOptions = DenyFraming;
        headers.ContentSecurityPolicy = AccountPages.ContentSecurityPolicy;
        headers[ReferrerPolicyHeader] = NoReferrer;
        return next(context);
    }

    private static string? SingleValue(StringValues values) => values.Count == 1 ? values[0] : null;

    private static IResult Html(string page, int statusCode = StatusCodes.Status200OK) =>
        Results.Content(page, HtmlContentType, statusCode: statusCode);

    private static IResult InvalidRequest() =>
        Results.Content(AccountPages.InvalidRequest(), HtmlContentType, statusCode: StatusCodes.Status400BadRequest);
}
