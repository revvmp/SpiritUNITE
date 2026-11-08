using System.Globalization;
using System.Security.Cryptography;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Game.ActivationCode;

public sealed class ActivationCodeManager(
    GameData gameData,
    ActivationCodeRepository repository,
    ActivitySchedule schedule,
    ServerContentRepository content,
    TimeProvider clock)
{
    public const int MaxActivationCodeLength = 32;
    public const int NotUsed = 0;
    public const int Used = 1;
    public const int DrawLocked = 0;
    public const int DrawOpen = 1;
    public const int DrawFinished = 2;
    public const long NoFreshTime = 0;
    public const int MaxHostActivationCodes = 1000;

    private const ulong NoUid = 0;
    private const int DrawnActivationCodeLength = 10;
    private const int MaxDrawAttempts = 8;
    private const string DrawnActivationCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string CfgTimeFormat = "yyyyMMddHHmmss";

    private readonly SemaphoreSlim useGate = new(1, 1);

    public static bool IsWellFormed(string activationCode) =>
        activationCode.Length is > 0 and <= MaxActivationCodeLength && activationCode.All(char.IsAsciiLetterOrDigit);

    public async Task<PB_ENUM_RES_ERROR_CODE> UseActivationCodeAsync(ulong gameUlongUID, string activationCode)
    {
        if (gameUlongUID == NoUid || !IsWellFormed(activationCode))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrActivationCodeNotExist;
        }

        await useGate.WaitAsync();
        try
        {
            return (await ListHostCodesAsync()).FirstOrDefault(host => host.ActivationCode == activationCode) is { } hostCode
                ? await UseHostCodeAsync(gameUlongUID, activationCode, hostCode)
                : await UseDrawnCodeAsync(gameUlongUID, activationCode);
        }
        finally
        {
            useGate.Release();
        }
    }

    public async Task<HostActivationCodeEntry[]> ListHostCodeEntriesAsync()
    {
        var uses = await repository.CountUsesByCodeAsync();
        return
        [
            .. (await ListHostCodesAsync()).Select(code => new HostActivationCodeEntry(
                code.ActivationCode,
                code.ValidEndTime,
                code.MaxUseCnt,
                uses.GetValueOrDefault(code.ActivationCode))),
        ];
    }

    public async Task<string?> AddHostCodeAsync(HostActivationCode code)
    {
        if (!IsWellFormed(code.ActivationCode))
        {
            return $"Codes are 1 to {MaxActivationCodeLength} letters or digits";
        }

        if (!IsCfgTime(code.ValidEndTime))
        {
            return "Valid until must be a date or empty";
        }

        if (code.MaxUseCnt < HostActivationCode.UnlimitedUseCnt)
        {
            return "Max uses must be 0 or more";
        }

        if (await content.CountHostActivationCodesAsync() >= MaxHostActivationCodes)
        {
            return $"There can be at most {MaxHostActivationCodes} codes";
        }

        if (await repository.FindDrawnAsync(code.ActivationCode) is not null)
        {
            return "A player already drew this code";
        }

        var row = new HostActivationCodeRow { ActivationCode = code.ActivationCode, ValidEndTime = code.ValidEndTime, MaxUseCnt = code.MaxUseCnt };
        return await content.TryInsertHostActivationCodeAsync(row) ? null : "This code exists already (upper and lower case count as the same)";
    }

    public Task<bool> RemoveHostCodeAsync(string activationCode) =>
        IsWellFormed(activationCode) ? content.DeleteHostActivationCodeAsync(activationCode) : Task.FromResult(false);

    public async Task<pbCOMDT_ACTIVATION_CODE_INFO> BuildActivationCodeInfoAsync(ulong gameUlongUID)
    {
        var info = new pbCOMDT_ACTIVATION_CODE_INFO();
        if (await repository.FindDrawnByOwnerAsync(gameUlongUID) is { } drawn)
        {
            info.ActivationCodes.Add(new pbCOMDT_ACTIVATION_CODE
            {
                UseState = drawn.IsUsed ? Used : NotUsed,
                GetTime = drawn.GetTime,
                ActivationCode = drawn.ActivationCode,
            });
        }

        return info;
    }

    public bool IsInvitationOpen() =>
        schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeInvitation, clock.GetUtcNow()) is not null;

    public async Task<int> GetDrawButtonStateAsync(LMNRoleInfo role) =>
        RoleProfile.GetLevel(gameData, role) < gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalActivationCodeLevel)
            ? DrawLocked
            : await repository.FindDrawnByOwnerAsync(role.GameUlongUID) is null
                ? DrawOpen
                : DrawFinished;

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, string ActivationCode)> DrawActivationCodeAsync(LMNRoleInfo role)
    {
        if (!IsInvitationOpen())
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, string.Empty);
        }

        if (await GetDrawButtonStateAsync(role) != DrawOpen)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, string.Empty);
        }

        var getTime = clock.GetUtcNow().ToUnixTimeSeconds();
        var hostCodes = await ListHostCodesAsync();
        for (var attempt = 0; attempt < MaxDrawAttempts; attempt++)
        {
            var code = RandomNumberGenerator.GetString(DrawnActivationCodeAlphabet, DrawnActivationCodeLength);
            if (hostCodes.Any(host => string.Equals(host.ActivationCode, code, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (await repository.TryInsertDrawnAsync(role.GameUlongUID, code, getTime))
            {
                return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, code);
            }

            if (await repository.FindDrawnByOwnerAsync(role.GameUlongUID) is not null)
            {
                return (PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, string.Empty);
            }
        }

        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, string.Empty);
    }

    private async Task<PB_ENUM_RES_ERROR_CODE> UseHostCodeAsync(ulong gameUlongUID, string code, HostActivationCode hostCode)
    {
        if (hostCode.IsExpired(UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow())))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrActivationCodeExpire;
        }

        if (await repository.HasUsedAsync(code, gameUlongUID)
            || (hostCode.MaxUseCnt != HostActivationCode.UnlimitedUseCnt && await repository.CountUsesAsync(code) >= hostCode.MaxUseCnt))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrActivationCodeUsed;
        }

        return await repository.TryInsertUseAsync(code, gameUlongUID, clock.GetUtcNow().ToUnixTimeSeconds())
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrActivationCodeUsed;
    }

    private async Task<PB_ENUM_RES_ERROR_CODE> UseDrawnCodeAsync(ulong gameUlongUID, string code)
    {
        if (await repository.FindDrawnAsync(code) is not { } drawn || drawn.ActivationCode != code)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrActivationCodeNotExist;
        }

        if (drawn.IsUsed || drawn.GameUlongUID == gameUlongUID)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrActivationCodeUsed;
        }

        return await repository.TryInsertUseAsync(code, gameUlongUID, clock.GetUtcNow().ToUnixTimeSeconds())
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrActivationCodeUsed;
    }

    private static bool IsCfgTime(long cfgTime) =>
        cfgTime == HostActivationCode.NoValidEndTime
        || DateTime.TryParseExact(cfgTime.ToString(CultureInfo.InvariantCulture), CfgTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private async Task<HostActivationCode[]> ListHostCodesAsync() =>
    [
        .. (await content.ListHostActivationCodesAsync()).Select(row => new HostActivationCode
        {
            ActivationCode = row.ActivationCode,
            ValidEndTime = row.ValidEndTime,
            MaxUseCnt = row.MaxUseCnt,
        }),
    ];
}
