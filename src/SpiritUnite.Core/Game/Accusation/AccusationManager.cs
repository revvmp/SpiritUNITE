using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Credit;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Accusation;

public sealed record AccusationOutcome(PB_ENUM_RES_ERROR_CODE Result, ulong Uid, string RoleName, int AccusationResult, int CreditPunish);

public sealed class AccusationManager(
    AccusationRepository repository,
    RoleInfoStore roles,
    CreditManager credit,
    GameData gameData,
    TimeProvider clock,
    ILogger<AccusationManager> logger)
{
    public const int VoiceAccusationsPerRequest = 1;

    private const int MaxAccusationReasonNum = (int)PB_MACRO_COM_MAX_ACCUSATION_REASON_NUM.PbComMaxAccusationReasonNum;
    private const int MaxAccusationDescLen = (int)PB_MACRO_COM_MAX_ACCUSATION_DESC_LEN.PbComMaxAccusationDescLen;
    private const int MaxAccusationContentLen = (int)PB_MACRO_COM_MAX_ACCUSATION_CONTENT_LEN.PbComMaxAccusationContentLen;
    private const int MaxAccountIDLength = 64;
    private const int MaxRoleNameLength = 64;
    private const int MaxBattleIDLength = 64;
    private const int MaxRelayIDLength = 32;
    private const int NoCreditPunish = 0;
    private const int GameScene = (int)PB_ENUM_ENM_ACCUSATION_SCENE.PbEnmAccusationGame;
    private const int ProfileScene = (int)PB_ENUM_ENM_ACCUSATION_SCENE.PbEnmAccusationProfile;
    private const int VoiceScene = (int)PB_ENUM_ENM_ACCUSATION_SCENE.PbEnmAccusationVoice;
    private const int IllegalChatCategory = (int)PB_ENUM_ENM_ACCUSATION_CATEGORY.PbEnmAccusationIllegalChat;
    private const int GameActionCategory = (int)PB_ENUM_ENM_ACCUSATION_CATEGORY.PbEnmAccusationGameAction;
    private const int HangupReason = (int)PB_ENUM_ENM_GAME_ACCUSATION_REASON.PbEnmGameAccusationHangup;
    private const int PendingResult = (int)PB_ENUM_ENM_ACCUSATION_RESULT.PbEnmAccusationResultPending;
    private const int SuccessResult = (int)PB_ENUM_ENM_ACCUSATION_RESULT.PbEnmAccusationResultSuccess;

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> gates = new();

    public async Task<AccusationOutcome> AccuseAsync(ulong uid, pbCOMDT_ONE_PLAYER_ACCUSATION_INFO? info)
    {
        var acntInfo = info?.AcntInfo;
        var businessData = info?.BusinessData;
        if (acntInfo is null || businessData is null || !HasValidLengths(acntInfo, businessData)
            || businessData.Category is not (IllegalChatCategory or GameActionCategory)
            || businessData.Scene is not (GameScene or ProfileScene))
        {
            return Refuse(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        var accused = await FindAccusedAsync(acntInfo.RoleID);
        if (accused is null)
        {
            return Refuse(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        if (accused.GameUlongUID == uid)
        {
            return Refuse(PB_ENUM_RES_ERROR_CODE.PbErrAccusationSelf, accused);
        }

        if (!AreValidReasons(businessData.Category, businessData.Reason))
        {
            return Refuse(PB_ENUM_RES_ERROR_CODE.PbErrAccusationInvalidReason, accused);
        }

        return await WithGateAsync(uid, async () =>
        {
            var isBattleAccusation = businessData.Scene == GameScene && businessData.BattleID.Length > 0;
            if (isBattleAccusation && await repository.ExistsAsync(uid, accused.GameUlongUID, GameScene, businessData.BattleID))
            {
                return Refuse(PB_ENUM_RES_ERROR_CODE.PbErrAccusationAlready, accused);
            }

            var creditPunish = isBattleAccusation && businessData.Category == GameActionCategory && businessData.Reason.Contains(HangupReason)
                ? await credit.PunishAccusedHangupAsync(accused.GameUlongUID, businessData.BattleID)
                : NoCreditPunish;
            var accusationResult = creditPunish > NoCreditPunish ? SuccessResult : PendingResult;
            await repository.AddAsync([BuildRow(uid, accused, acntInfo, businessData, businessData.Scene, 0, accusationResult, creditPunish)]);
            logger.LogInformation(
                "Uid {Uid} reported uid {AccusedUid} (category {Category}, scene {Scene}, reasons {Reasons}, battle {BattleID}): result {AccusationResult}, credit punish {CreditPunish}",
                uid,
                accused.GameUlongUID,
                businessData.Category,
                businessData.Scene,
                string.Join(',', businessData.Reason),
                businessData.BattleID,
                accusationResult,
                creditPunish);
            return new AccusationOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, accused.GameUlongUID, accused.InGameUsrName, accusationResult, creditPunish);
        });
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> AccuseVoiceAsync(ulong uid, IReadOnlyList<pbCOMDT_ONE_PLAYER_ACCUSATION_INFO> infos, int voiceAccusationSource)
    {
        if (infos.Count != VoiceAccusationsPerRequest)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        var rows = new List<RoleAccusation>(infos.Count);
        foreach (var info in infos)
        {
            var acntInfo = info.AcntInfo;
            var businessData = info.BusinessData ?? new pbCOMDT_ACCUSATION_BUSINESS_DATA();
            if (acntInfo is null || !HasValidLengths(acntInfo, businessData))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            }

            var accused = await FindAccusedAsync(acntInfo.RoleID);
            if (accused is null)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            }

            if (accused.GameUlongUID == uid)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrAccusationSelf;
            }

            rows.Add(BuildRow(uid, accused, acntInfo, businessData, VoiceScene, voiceAccusationSource, PendingResult, NoCreditPunish));
        }

        await repository.AddAsync(rows);
        logger.LogInformation(
            "Uid {Uid} reported the voice chat of uids {AccusedUids} (source {VoiceAccusationSource}, battle {BattleIDs})",
            uid,
            string.Join(',', rows.Select(row => row.AccusedUid)),
            voiceAccusationSource,
            string.Join(',', rows.Select(row => row.BattleID)));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static bool HasValidLengths(pbCOMDT_ACCUSATION_ACNT_INFO acntInfo, pbCOMDT_ACCUSATION_BUSINESS_DATA businessData) =>
        acntInfo.AccountID.Length <= MaxAccountIDLength
        && acntInfo.RoleName.Length <= MaxRoleNameLength
        && businessData.BattleID.Length <= MaxBattleIDLength
        && businessData.RelayID.Length <= MaxRelayIDLength
        && businessData.Desc.Length <= MaxAccusationDescLen
        && businessData.Content.Length <= MaxAccusationContentLen;

    private static AccusationOutcome Refuse(PB_ENUM_RES_ERROR_CODE result, LMNRoleInfo? accused = null) =>
        new(result, accused?.GameUlongUID ?? 0, accused?.InGameUsrName ?? "", PendingResult, NoCreditPunish);

    private bool AreValidReasons(int category, IReadOnlyCollection<int> reasons) =>
        reasons.Count is > 0 and <= MaxAccusationReasonNum
        && reasons.Distinct().Count() == reasons.Count
        && reasons.All(reason => gameData.ReportText.Find(pbReportText.Key(category, reason)) is not null);

    private async Task<LMNRoleInfo?> FindAccusedAsync(string roleID) =>
        ulong.TryParse(roleID, NumberStyles.None, CultureInfo.InvariantCulture, out var accusedUid) && accusedUid != 0
            ? await roles.FindAsync(accusedUid)
            : null;

    private RoleAccusation BuildRow(
        ulong uid,
        LMNRoleInfo accused,
        pbCOMDT_ACCUSATION_ACNT_INFO acntInfo,
        pbCOMDT_ACCUSATION_BUSINESS_DATA businessData,
        int scene,
        int voiceAccusationSource,
        int accusationResult,
        int creditPunish) => new()
        {
            GameUlongUID = uid,
            AccusedUid = accused.GameUlongUID,
            AccusedAccountID = acntInfo.AccountID,
            AccusedRoleName = accused.InGameUsrName,
            Category = businessData.Category,
            Scene = scene,
            Reason = string.Join(',', businessData.Reason),
            BattleID = businessData.BattleID,
            BattleTime = businessData.BattleTime,
            RelayID = businessData.RelayID,
            Desc = businessData.Desc,
            Content = businessData.Content,
            VoiceAccusationSource = voiceAccusationSource,
            AccusationResult = accusationResult,
            CreditPunish = creditPunish,
            AccusationTime = clock.GetUtcNow().ToUnixTimeSeconds(),
        };

    private async Task<T> WithGateAsync<T>(ulong uid, Func<Task<T>> action)
    {
        var gate = gates.GetOrAdd(uid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
