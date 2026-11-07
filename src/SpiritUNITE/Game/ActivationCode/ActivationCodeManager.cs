using System.Security.Cryptography;
using Microsoft.Extensions.Options;
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
    IOptions<ActivationCodeOptions> options,
    TimeProvider clock)
{
    public const int MaxActivationCodeLength = 32;
    public const int NotUsed = 0;
    public const int Used = 1;
    public const int DrawLocked = 0;
    public const int DrawOpen = 1;
    public const int DrawFinished = 2;
    public const long NoFreshTime = 0;

    private const ulong NoUid = 0;
    private const int DrawnActivationCodeLength = 10;
    private const int MaxDrawAttempts = 8;
    private const string DrawnActivationCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

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
            return options.Value.Codes.FirstOrDefault(host => host.ActivationCode == activationCode) is { } hostCode
                ? await UseHostCodeAsync(gameUlongUID, activationCode, hostCode)
                : await UseDrawnCodeAsync(gameUlongUID, activationCode);
        }
        finally
        {
            useGate.Release();
        }
    }

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
        for (var attempt = 0; attempt < MaxDrawAttempts; attempt++)
        {
            var code = RandomNumberGenerator.GetString(DrawnActivationCodeAlphabet, DrawnActivationCodeLength);
            if (options.Value.Codes.Any(host => string.Equals(host.ActivationCode, code, StringComparison.OrdinalIgnoreCase)))
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
}
