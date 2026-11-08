using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SpiritUnite.Auth;

namespace SpiritUnite.Servers.Http;

public static class AccountPages
{
    public const string LoginPath = "/oauth2/auth";
    public const string NintendoLoginPath = "/connect/1.0.0/authorize";
    public const string RegisterPath = "/oauth2/register";

    private const string CompletionScript =
        "(function(){var data=JSON.parse(document.getElementById('login-result').textContent);" +
        "if(window.PokemonLogin&&typeof window.PokemonLogin.onResult==='function'){window.PokemonLogin.onResult(data.result);}" +
        "else{window.location.replace(data.fallback);}})();";

    private const string PasswordRevealScript =
        "(function(){document.querySelectorAll('button[data-reveal]').forEach(function(button){" +
        "var input=document.getElementById(button.getAttribute('aria-controls'));if(!input){return;}" +
        "button.addEventListener('click',function(){var reveal=input.type==='password';" +
        "input.type=reveal?'text':'password';button.setAttribute('aria-pressed',reveal?'true':'false');input.focus();});});})();";

    public static readonly string ContentSecurityPolicy =
        $"default-src 'none'; script-src {ScriptHashSource(CompletionScript)} {ScriptHashSource(PasswordRevealScript)}; " +
        "style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";

    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    public static string Login(AuthorizationRequest request, string login, string? error) => Page(
        "Log in",
        "Log in to your SpiritUNITE account",
        error,
        $"""
        <form method="post" action="{LoginPath}">
          {HiddenFields(request)}
          <div class="field">
            <label class="visually-hidden" for="field-login">Username or email</label>
            <input id="field-login" name="login" value="{Html.Encode(login)}" placeholder="Username or email" autocomplete="username" autocapitalize="none" spellcheck="false" maxlength="{AccountManager.MaximumEmailLength}" required>
          </div>
          <div class="field">
            <label class="visually-hidden" for="field-password">Password</label>
            {PasswordInput("field-password", "password", "Password", "current-password", null, null)}
          </div>
          <button class="primary" type="submit">Log In</button>
        </form>
        <p class="switch">Don't have an account?</p>
        <a class="secondary" href="{Link(RegisterPath, request)}">Create Account</a>
        <script>{PasswordRevealScript}</script>
        """);

    public static string Register(AuthorizationRequest request, string userName, string email, string? error) => Page(
        "Create an account",
        "Create your SpiritUNITE account",
        error,
        $"""
        <form method="post" action="{RegisterPath}">
          {HiddenFields(request)}
          <div class="field">
            <label class="visually-hidden" for="field-user-name">Username</label>
            <input id="field-user-name" name="user_name" value="{Html.Encode(userName)}" placeholder="Username" autocomplete="username" autocapitalize="none" spellcheck="false" minlength="{AccountManager.MinimumUserNameLength}" maxlength="{AccountManager.MaximumUserNameLength}" pattern="[A-Za-z0-9_.\-]+" aria-describedby="hint-user-name" required>
            <p class="hint" id="hint-user-name">{AccountManager.MinimumUserNameLength} to {AccountManager.MaximumUserNameLength} letters, numbers, dots, dashes or underscores.</p>
          </div>
          <div class="field">
            <label class="visually-hidden" for="field-email">Email</label>
            <input id="field-email" name="email" type="email" value="{Html.Encode(email)}" placeholder="Email" autocomplete="email" autocapitalize="none" spellcheck="false" maxlength="{AccountManager.MaximumEmailLength}" required>
          </div>
          <div class="field">
            <label class="visually-hidden" for="field-password">Password</label>
            {PasswordInput("field-password", "password", "Password", "new-password", AccountManager.MinimumPasswordLength, "hint-password")}
            <p class="hint" id="hint-password">{AccountManager.MinimumPasswordLength} to {AccountManager.MaximumPasswordLength} characters. Use a password you do not use anywhere else.</p>
          </div>
          <div class="field">
            <label class="visually-hidden" for="field-password-confirmation">Confirm password</label>
            {PasswordInput("field-password-confirmation", "password_confirmation", "Confirm password", "new-password", null, null)}
          </div>
          <button class="primary" type="submit">Create Account</button>
        </form>
        <p class="switch">Already have an account?</p>
        <a class="secondary" href="{Link(LoginPath, request)}">Log In</a>
        <script>{PasswordRevealScript}</script>
        """);

    public static string Complete(string resultJson, string fallbackRedirect) => Page(
        "Logged in",
        "Logged in",
        null,
        $"""
        <div class="status" role="status">
          <span class="spinner" aria-hidden="true"></span>
          <p class="lead">You are logged in. Returning to the game&hellip;</p>
        </div>
        <script type="application/json" id="login-result">{JsonSerializer.Serialize(new { result = resultJson, fallback = fallbackRedirect })}</script>
        <script>{CompletionScript}</script>
        """);

    public static string InvalidRequest() => Page(
        "Login unavailable",
        "Login unavailable",
        null,
        """<p class="lead">This login link is not valid or has expired. Close this page and try logging in again from the game.</p>""");

    public const string LoginError = "The username, email or password is not correct.";

    public static string RegistrationError(AccountRegistrationResult result) => result switch
    {
        AccountRegistrationResult.UserNameInvalid =>
            $"Usernames are {AccountManager.MinimumUserNameLength} to {AccountManager.MaximumUserNameLength} letters, numbers, dots, dashes or underscores.",
        AccountRegistrationResult.EmailInvalid => "Enter a valid email address.",
        AccountRegistrationResult.PasswordInvalid =>
            $"Passwords are {AccountManager.MinimumPasswordLength} to {AccountManager.MaximumPasswordLength} characters.",
        AccountRegistrationResult.PasswordConfirmationMismatch => "The passwords do not match.",
        AccountRegistrationResult.UserNameTaken => "That username is already taken.",
        AccountRegistrationResult.EmailTaken => "An account with that email already exists. Log in instead.",
        _ => "The account could not be created.",
    };

    private static string ScriptHashSource(string script) =>
        $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)))}'";

    private static string PasswordInput(string id, string name, string placeholder, string autocomplete, int? minimumLength, string? hintId) =>
        $"""
        <div class="password">
              <input id="{id}" name="{name}" type="password" placeholder="{placeholder}" autocomplete="{autocomplete}"{(minimumLength is { } minimum ? $" minlength=\"{minimum}\"" : string.Empty)} maxlength="{AccountManager.MaximumPasswordLength}"{(hintId is null ? string.Empty : $" aria-describedby=\"{hintId}\"")} required>
              <button class="reveal" type="button" data-reveal aria-controls="{id}" aria-pressed="false" aria-label="Show password">
                <svg class="eye-shown" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/></svg>
                <svg class="eye-hidden" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M10.6 5.1A10.6 10.6 0 0 1 12 5c6.4 0 10 7 10 7a17.7 17.7 0 0 1-2.8 3.8M6.6 6.6C3.8 8.4 2 12 2 12s3.6 7 10 7a9.8 9.8 0 0 0 5.4-1.6"/><path d="M9.9 9.9a3 3 0 0 0 4.2 4.2"/><path d="M3 3l18 18"/></svg>
              </button>
            </div>
        """;

    private static string HiddenFields(AuthorizationRequest request) => string.Concat(
        request.ToParameters().Select(parameter =>
            $"""<input type="hidden" name="{Html.Encode(parameter.Key)}" value="{Html.Encode(parameter.Value ?? string.Empty)}">"""));

    private static string Link(string path, AuthorizationRequest request) =>
        Html.Encode(path + QueryString.Create(request.ToParameters()));

    internal static string Page(string title, string heading, string? error, string body) =>
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
        <title>{{Html.Encode(title)}} - SpiritUNITE</title>
        <style>
          :root {
            color-scheme: light;
            --page: #ffffff;
            --text: #1a1a1a;
            --muted: #6b6b6b;
            --field: #333333;
            --field-hover: #3d3d3d;
            --field-text: #ffffff;
            --placeholder: #b8b8b8;
            --green: #1f9d48;
            --green-hover: #1a8a3f;
            --green-text: #ffffff;
            --focus: rgba(31, 157, 72, .35);
            --brand-start: #5b4ce0;
            --brand-end: #0f8f80;
            --track: #d9d9d9;
            --error-bg: #fff0ef;
            --error-border: #f5c2bd;
            --error-text: #a3261d;
            --notice-bg: #eefaf2;
            --notice-border: #b5e3c4;
            --notice-text: #17703a;
          }
          * { box-sizing: border-box; }
          html { -webkit-text-size-adjust: 100%; text-size-adjust: 100%; }
          body {
            margin: 0;
            min-height: 100vh;
            min-height: 100dvh;
            display: flex;
            flex-direction: column;
            align-items: center;
            background: var(--page);
            color: var(--text);
            font: 16px/1.5 system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", "Helvetica Neue", Arial, sans-serif;
          }
          main {
            flex: 1;
            width: 100%;
            max-width: 400px;
            padding: max(48px, env(safe-area-inset-top)) max(20px, env(safe-area-inset-right)) 32px max(20px, env(safe-area-inset-left));
          }
          .visually-hidden {
            position: absolute;
            width: 1px;
            height: 1px;
            padding: 0;
            margin: -1px;
            overflow: hidden;
            clip: rect(0, 0, 0, 0);
            white-space: nowrap;
            border: 0;
          }
          .logo { display: flex; align-items: center; justify-content: center; gap: 14px; margin: 0 0 40px; }
          .emblem { width: 76px; height: 76px; flex: none; }
          .wordmark { display: flex; flex-direction: column; font-weight: 900; line-height: .92; letter-spacing: -.02em; text-transform: uppercase; }
          .wordmark-top { font-size: 34px; }
          .wordmark-bottom {
            font-size: 40px;
            background: linear-gradient(90deg, var(--brand-start), var(--brand-end));
            -webkit-background-clip: text;
            background-clip: text;
            color: transparent;
          }
          h1 { margin: 0 0 32px; text-align: center; font-size: 24px; line-height: 1.25; font-weight: 800; letter-spacing: -.01em; }
          .lead { margin: 0; text-align: center; color: var(--muted); }
          .field { margin-bottom: 16px; }
          input {
            display: block;
            width: 100%;
            height: 56px;
            padding: 0 18px;
            border: 0;
            border-radius: 6px;
            background: var(--field);
            color: var(--field-text);
            font: inherit;
            font-size: 17px;
            transition: background-color .15s, box-shadow .15s;
          }
          input::placeholder { color: var(--placeholder); opacity: 1; }
          input:hover { background: var(--field-hover); }
          input:focus { outline: none; box-shadow: 0 0 0 3px var(--focus); }
          input:-webkit-autofill { -webkit-text-fill-color: var(--field-text); box-shadow: inset 0 0 0 100px var(--field); caret-color: var(--field-text); }
          input:-webkit-autofill:focus { box-shadow: inset 0 0 0 100px var(--field), 0 0 0 3px var(--focus); }
          .password { position: relative; }
          .password input { padding-right: 60px; }
          .reveal {
            position: absolute;
            top: 0;
            right: 0;
            width: 56px;
            height: 56px;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 0;
            border: 0;
            border-radius: 6px;
            background: transparent;
            color: var(--placeholder);
            cursor: pointer;
          }
          .reveal:hover { color: var(--field-text); }
          .reveal:focus-visible { outline: none; box-shadow: inset 0 0 0 2px var(--green); }
          .reveal svg { width: 24px; height: 24px; fill: none; stroke: currentColor; stroke-width: 1.8; stroke-linecap: round; stroke-linejoin: round; }
          .reveal .eye-hidden, .reveal[aria-pressed="true"] .eye-shown { display: none; }
          .reveal[aria-pressed="true"] .eye-hidden { display: block; }
          .hint { margin: 6px 2px 0; color: var(--muted); font-size: 13px; line-height: 1.4; }
          .primary, .secondary {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 100%;
            height: 64px;
            padding: 0 20px;
            border-radius: 6px;
            font: inherit;
            font-size: 18px;
            font-weight: 800;
            text-decoration: none;
            cursor: pointer;
            transition: background-color .15s, color .15s, transform .1s;
          }
          .primary { margin-top: 28px; border: 0; background: var(--green); color: var(--green-text); }
          .primary:hover { background: var(--green-hover); }
          .secondary { border: 2px solid var(--green); background: transparent; color: var(--green); }
          .secondary:hover { background: var(--focus); }
          .primary:active, .secondary:active { transform: translateY(1px); }
          .primary:focus-visible, .secondary:focus-visible { outline: none; box-shadow: 0 0 0 4px var(--focus); }
          .switch { margin: 40px 0 14px; text-align: center; font-weight: 700; }
          .error {
            margin: 0 0 24px;
            padding: 12px 16px;
            border: 1px solid var(--error-border);
            border-radius: 6px;
            background: var(--error-bg);
            color: var(--error-text);
            font-weight: 550;
          }
          .notice {
            margin: 0 0 24px;
            padding: 12px 16px;
            border: 1px solid var(--notice-border);
            border-radius: 6px;
            background: var(--notice-bg);
            color: var(--notice-text);
            font-weight: 550;
          }
          .trainer { margin: -16px 0 28px; text-align: center; color: var(--muted); }
          .trainer strong { color: var(--text); }
          .status { display: flex; flex-direction: column; align-items: center; gap: 18px; }
          .spinner { width: 32px; height: 32px; flex: none; border-radius: 50%; border: 3px solid var(--track); border-top-color: var(--green); animation: spin .8s linear infinite; }
          @keyframes spin { to { transform: rotate(360deg); } }
          @media (prefers-reduced-motion: reduce) { * { transition: none !important; } .spinner { animation-duration: 2.4s; } }
          footer { width: 100%; max-width: 400px; padding: 0 20px max(24px, env(safe-area-inset-bottom)); text-align: center; color: var(--muted); font-size: 12px; line-height: 1.5; }
          footer p { margin: 0; }
          @media (max-height: 560px) {
            main { padding-top: 24px; }
            .logo { margin-bottom: 24px; }
          }
        </style>
        </head>
        <body>
        <main>
        <div class="logo" role="img" aria-label="SpiritUNITE">
          <svg class="emblem" viewBox="0 0 64 64" aria-hidden="true" focusable="false">
            <defs>
              <linearGradient id="emblem-fill" x1="0" y1="0" x2="1" y2="1">
                <stop offset="0" stop-color="#5b4ce0"/>
                <stop offset="1" stop-color="#12a594"/>
              </linearGradient>
            </defs>
            <circle cx="32" cy="32" r="31" fill="url(#emblem-fill)"/>
            <circle cx="32" cy="32" r="25.5" fill="none" stroke="#ffffff" stroke-width="2.5" opacity=".35"/>
            <path d="M30 12c1.8 11.2 5.8 15.2 17 17-11.2 1.8-15.2 5.8-17 17-1.8-11.2-5.8-15.2-17-17 11.2-1.8 15.2-5.8 17-17Z" fill="#ffffff"/>
            <circle cx="46" cy="46" r="4.6" fill="#ffffff" opacity=".85"/>
          </svg>
          <span class="wordmark" aria-hidden="true"><span class="wordmark-top">Spirit</span><span class="wordmark-bottom">Unite</span></span>
        </div>
        <h1>{{Html.Encode(heading)}}</h1>
        {{(error is null ? string.Empty : $"""<p class="error" role="alert">{Html.Encode(error)}</p>""")}}
        {{body}}
        </main>
        <footer><p>SpiritUNITE is a community server and is not affiliated with Nintendo, The Pok&eacute;mon Company or Tencent. Do not reuse your Pok&eacute;mon Trainer Club password here.</p></footer>
        </body>
        </html>
        """;
}
