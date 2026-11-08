using System.Text.Encodings.Web;
using SpiritUnite.Auth;
using SpiritUnite.Game.GiftCode;

namespace SpiritUnite.Servers.Http;

public sealed record GiftCodeWebViewLink(string RoleId, string ItopEncodeParam);

public static class GiftCodePages
{
    public const string Title = "Gift Exchange";
    public const string Exchanged = "Gift code exchanged. You can claim the gift from your mail.";
    public const string LoginError = "The username, email or password is not correct.";
    public const string NoTrainer = "Create your trainer in the game first.";

    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    public static string FromGame(string path, GiftCodeWebViewLink link, string trainerName, string? error, string? notice) => AccountPages.Page(
        Title,
        Title,
        error,
        $"""
        {Notice(notice)}
        <p class="trainer">Trainer <strong>{Html.Encode(trainerName)}</strong></p>
        <form method="post" action="{Html.Encode(path)}">
          <input type="hidden" name="{GiftCodeEndpoints.RoleIdField}" value="{Html.Encode(link.RoleId)}">
          <input type="hidden" name="{GiftCodeEndpoints.ItopEncodeParamField}" value="{Html.Encode(link.ItopEncodeParam)}">
          {CodeField()}
          <button class="primary" type="submit">Exchange</button>
        </form>
        """);

    public static string WithLogin(string path, string login, string? error, string? notice) => AccountPages.Page(
        Title,
        Title,
        error,
        $"""
        {Notice(notice)}
        <form method="post" action="{Html.Encode(path)}">
          {CodeField()}
          <div class="field">
            <label class="visually-hidden" for="field-login">Username or email</label>
            <input id="field-login" name="{GiftCodeEndpoints.LoginField}" value="{Html.Encode(login)}" placeholder="Username or email" autocomplete="username" autocapitalize="none" spellcheck="false" maxlength="{AccountManager.MaximumEmailLength}" required>
          </div>
          <div class="field">
            <label class="visually-hidden" for="field-password">Password</label>
            <input id="field-password" name="{GiftCodeEndpoints.PasswordField}" type="password" placeholder="Password" autocomplete="current-password" maxlength="{AccountManager.MaximumPasswordLength}" required>
            <p class="hint">Log in with your SpiritUNITE account. Guest players open Gift Exchange in the game instead.</p>
          </div>
          <button class="primary" type="submit">Exchange</button>
        </form>
        """);

    public static string InvalidLink() => AccountPages.Page(
        Title,
        Title,
        null,
        """<p class="lead">This link is not valid or has expired. Close this page and open Gift Exchange in the game again.</p>""");

    public static string RedeemError(GiftCodeRedeemResult result) => result switch
    {
        GiftCodeRedeemResult.AlreadyUsed => "This gift code has already been used.",
        GiftCodeRedeemResult.UsedUp => "This gift code has been exchanged for items and can no longer be used.",
        GiftCodeRedeemResult.NotStarted => "This gift code cannot be used yet.",
        GiftCodeRedeemResult.Expired => "This gift code has expired.",
        _ => "This gift code is invalid. Please check the entered text.",
    };

    private static string CodeField() =>
        $"""
        <div class="field">
            <label class="visually-hidden" for="field-gift-code">Gift code</label>
            <input id="field-gift-code" name="{GiftCodeEndpoints.GiftCodeField}" placeholder="Enter Gift Code" autocomplete="off" autocapitalize="characters" spellcheck="false" maxlength="{GiftCodeManager.MaxGiftCodeLength}" pattern="[A-Za-z0-9]+" required autofocus>
          </div>
        """;

    private static string Notice(string? notice) =>
        notice is null ? string.Empty : $"""<p class="notice" role="status">{Html.Encode(notice)}</p>""";
}
