using System.Globalization;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Online;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Game.GiftCode;

public enum GiftCodeRedeemResult
{
    Redeemed,
    Invalid,
    AlreadyUsed,
    NotStarted,
    Expired,
    UsedUp,
}

public sealed record GiftCodeDraft(string GiftCode, long ValidStartTime, long ValidEndTime, int MaxUseCnt, IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_INFO> Awards);

public sealed record GiftCodeEntry(
    string GiftCode,
    long ValidStartTime,
    long ValidEndTime,
    int MaxUseCnt,
    int UseCnt,
    bool IsEnabled,
    long CreateTime,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_INFO> Awards);

public sealed record GiftCodeUseEntry(ulong GameUlongUID, long UseTime, int MailIndex);

public sealed class GiftCodeManager(
    GiftCodeRepository repository,
    MailManager mail,
    LobbyRoles lobbyRoles,
    TimeProvider clock,
    ILogger<GiftCodeManager> logger)
{
    public const int MaxGiftCodeLength = 32;
    public const long NoValidStartTime = 0;
    public const int UnlimitedUseCnt = 0;
    public const int MaxGiftCodes = 1000;
    public const int MaxListedUses = 500;
    public const int MaxAwards = MailBox.MaxMailAccess;
    public const string MailSubject = "OpsEvents_CDK_Email_Subject_7405_001";
    public const string MailContent = "OpsEvents_CDK_Email_Content_7406_001";
    private const string SingleAwardMailSubject = "OpsEvents_CDK_Email_Subject_7405_Single_001";
    private const string SingleAwardMailContent = "OpsEvents_CDK_Email_Content_7406_Single_001";
    private const int GiftCodeMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeGiftCode;
    private const int GiftCodeSingleMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeGiftCodeSingle;
    private const int SingleAward = 1;
    private const string CfgTimeFormat = "yyyyMMddHHmmss";

    public static string? Normalize(string? input)
    {
        var trimmed = input?.Trim();
        return trimmed is { Length: > 0 and <= MaxGiftCodeLength } && trimmed.All(char.IsAsciiLetterOrDigit)
            ? trimmed.ToUpperInvariant()
            : null;
    }

    public async Task<string?> CreateAsync(GiftCodeDraft draft)
    {
        if (Normalize(draft.GiftCode) is not { } giftCode)
        {
            return $"Codes are 1 to {MaxGiftCodeLength} letters or digits";
        }

        if (draft.ValidStartTime != NoValidStartTime && !IsCfgTime(draft.ValidStartTime))
        {
            return "Valid from must be a date or empty";
        }

        if (!IsCfgTime(draft.ValidEndTime) || draft.ValidEndTime <= draft.ValidStartTime)
        {
            return "Valid until must be a date after Valid from";
        }

        if (draft.MaxUseCnt < UnlimitedUseCnt)
        {
            return "Max uses must be 0 or more";
        }

        if (draft.Awards.Count is 0 or > MaxAwards)
        {
            return $"Add 1 to {MaxAwards} rewards";
        }

        if (draft.Awards.FirstOrDefault(award => award.ResCnt <= 0 || !mail.CanAttach(award)) is { } refused)
        {
            return $"Reward {refused.ResID} x{refused.ResCnt} cannot be sent";
        }

        if (await repository.CountAsync() >= MaxGiftCodes)
        {
            return $"There can be at most {MaxGiftCodes} codes";
        }

        var row = new GiftCodeRow
        {
            GiftCode = giftCode,
            ValidStartTime = draft.ValidStartTime,
            ValidEndTime = draft.ValidEndTime,
            MaxUseCnt = draft.MaxUseCnt,
            IsEnabled = true,
            CreateTime = clock.GetUtcNow().ToUnixTimeSeconds(),
        };
        GiftCodeAward[] awards =
        [
            .. draft.Awards.Select((award, index) => new GiftCodeAward
            {
                GiftCode = giftCode,
                AwardIndex = (byte)index,
                ResType = award.ResType,
                ResID = award.ResID,
                ResCnt = award.ResCnt,
            }),
        ];
        return await repository.TryInsertAsync(row, awards) ? null : "This code exists already";
    }

    public async Task<GiftCodeEntry[]> ListAsync()
    {
        var awards = (await repository.ListAwardsAsync()).ToLookup(award => award.GiftCode, StringComparer.OrdinalIgnoreCase);
        return
        [
            .. (await repository.ListAsync()).Select(code => new GiftCodeEntry(
                code.GiftCode,
                code.ValidStartTime,
                code.ValidEndTime,
                code.MaxUseCnt,
                code.UseCnt,
                code.IsEnabled,
                code.CreateTime,
                [.. awards[code.GiftCode].Select(ToAward)])),
        ];
    }

    public async Task<GiftCodeEntry?> FindAsync(string giftCode) =>
        Normalize(giftCode) is { } normalized
            ? (await ListAsync()).FirstOrDefault(entry => entry.GiftCode == normalized)
            : null;

    public async Task<GiftCodeUseEntry[]?> ListUsesAsync(string giftCode)
    {
        if (Normalize(giftCode) is not { } normalized || !await repository.ExistsAsync(normalized))
        {
            return null;
        }

        return [.. (await repository.ListUsesAsync(normalized, MaxListedUses)).Select(use => new GiftCodeUseEntry(use.GameUlongUID, use.UseTime, use.MailIndex))];
    }

    public Task<bool> SetEnabledAsync(string giftCode, bool isEnabled) =>
        Normalize(giftCode) is { } normalized ? repository.SetEnabledAsync(normalized, isEnabled) : Task.FromResult(false);

    public Task<bool> DeleteAsync(string giftCode) =>
        Normalize(giftCode) is { } normalized ? repository.DeleteAsync(normalized) : Task.FromResult(false);

    public async Task<GiftCodeRedeemResult> RedeemAsync(ulong gameUlongUID, string? input)
    {
        if (Normalize(input) is not { } giftCode)
        {
            return GiftCodeRedeemResult.Invalid;
        }

        var now = clock.GetUtcNow();
        SystemMail? sent = null;
        var commit = await repository.RedeemAsync(
            giftCode,
            gameUlongUID,
            UTCTimeUtil.ServerTime2CfgTime(now),
            now.ToUnixTimeSeconds(),
            awards =>
            {
                sent = BuildMail(awards);
                return mail.CreateSystemMail(gameUlongUID, sent);
            });
        if (commit.Result != GiftCodeRedeemResult.Redeemed || commit.Mail is not { } saved || sent is null)
        {
            logger.LogInformation("Gift code redemption by uid {Uid} refused: {Result}", gameUlongUID, commit.Result);
            return commit.Result;
        }

        logger.LogInformation("Gift code {GiftCode} redeemed by uid {Uid}", giftCode, gameUlongUID);
        mail.LogSystemMailSent(saved, sent);
        await mail.PushMailNewAsync(saved);
        foreach (var packet in await mail.BuildUnreadRedDotPacketsAsync(gameUlongUID, saved.MailType))
        {
            await lobbyRoles.PushAsync(gameUlongUID, packet);
        }

        return GiftCodeRedeemResult.Redeemed;
    }

    private static SystemMail BuildMail(IReadOnlyList<GiftCodeAward> awards)
    {
        var single = awards.Count == SingleAward;
        return new SystemMail(
            single ? SingleAwardMailSubject : MailSubject,
            single ? SingleAwardMailContent : MailContent,
            [.. awards.Select(ToAward)],
            MailID: single ? GiftCodeSingleMailID : GiftCodeMailID);
    }

    private static pbCOMDT_ONE_RESOURCE_AWARD_INFO ToAward(GiftCodeAward award) =>
        new() { ResType = award.ResType, ResID = award.ResID, ResCnt = award.ResCnt };

    private static bool IsCfgTime(long cfgTime) =>
        DateTime.TryParseExact(cfgTime.ToString(CultureInfo.InvariantCulture), CfgTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
