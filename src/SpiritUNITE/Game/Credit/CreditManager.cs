using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Credit;

public sealed record CreditPenalty(bool IsPenalized, int CreditPoint, long BanEndTime);

public sealed record CreditReduceBattle(int MapId, long GameStartTime, int HeroID, string DeskUniqId)
{
    public static string BattleIDOf(int deskID, long deskCrtSeq, int relayEntity) => $"{deskID}_{deskCrtSeq}_{relayEntity}";
}

public sealed class CreditManager(
    CreditRepository repository,
    GameData gameData,
    MailManager mail,
    LobbyRoles lobbyRoles,
    TimeProvider clock,
    ILogger<CreditManager> logger)
{
    public const int StartingCreditPoint = (int)PB_MACRO_COM_CREDIT_POINT_INIT_VALUE.PbComCreditPointInitValue;

    public static readonly TimeSpan UnconfirmedMatchMailDelay = TimeSpan.FromSeconds(3);

    private const long SecondsPerDay = 86400;
    private const long MillisecondsPerSecond = 1000;
    private const int MinCreditPoint = 0;
    private const int MaxCreditPoint = StartingCreditPoint;
    private const int NoBanSec = 0;
    private const int UnconfirmedMatchMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeHangupPunish;
    private const int AccusationMailType = (int)PB_ENUM_RES_MAIL_TYPE.PbResMailTypeAccusation;
    private const int GameStartUnconfirmKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalGameStartUnconfirm;
    private const int FirstMatchUnconfirmKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalMatchUnconfirm1;
    private const int LastMatchUnconfirmKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalMatchUnconfirmMoreThan6;
    private const int BattleResumeCreditKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalBattleResumeCredit;
    private const int ManmachineResumeCreditKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalManmachineResumeCredit;
    private const int GameStartNeedCreditKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalGameStartNeedCredit;
    private const int MildHandupMinAfkTimeKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalMildHandupMinAfkTime;
    private const int SevereHandupMinHandupTimeKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalSevereHandupMinHandupTime;
    private const int MaliciousHandupTotalTimeKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalMaliciousHandupTotalTime;
    private const int MildHandupDeduceCreditpointKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalMildHandupDeduceCreditpoint;
    private const int SevereHandupDeduceCreditpointKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalSevereHandupDeduceCreditpoint;
    private const int MaliciousHandupDeduceCreditpointKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalMaliciousHandupDeduceCreditpoint;
    private const int HangupIncomeRatioBase = 100;
    private const int MaxDeduceCreditPointRecordNum = (int)PB_MACRO_COM_MAX_DEDUCE_CREDIT_POINT_RECORD_NUM.PbComMaxDeduceCreditPointRecordNum;
    private const int NoHeroID = 0;
    private const string NoDeskUniqId = "";

    private static readonly SystemMail UnconfirmedMatchMail = new(
        Subject: "Mail_TextTitle_12_001",
        Content: "Mail_TextContent_12_001",
        Attachments: [],
        MailType: AccusationMailType,
        MailID: UnconfirmedMatchMailID);

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> gates = new();

    public async Task<int> GetCreditPointAsync(ulong uid) => (await LoadAsync(uid)).CreditPoint;

    public async Task<pbSCCreditPointNtf> BuildCreditPointNtfAsync(ulong uid) => new()
    {
        CurrentCreditPoint = await GetCreditPointAsync(uid),
    };

    public async Task<long?> FindBattleBanEndAsync(ulong uid)
    {
        var banEndTime = (await LoadAsync(uid)).BanEndTime;
        return banEndTime > clock.GetUtcNow().ToUnixTimeSeconds() ? banEndTime : null;
    }

    public async Task<CreditPenalty> PenalizeUnconfirmedMatchAsync(ulong uid, int mapId)
    {
        var hangupSchemeID = gameData.SceneMap.Find(mapId)?.HangupSchemeID ?? 0;
        return await UpdateAsync(uid, record =>
        {
            var now = clock.GetUtcNow();
            var today = now.ToUnixTimeSeconds() / SecondsPerDay;
            var unconfirmedCount = record.UnconfirmedDay == today ? record.UnconfirmedCount + 1 : 1;
            var matchUnconfirmKey = Math.Min(FirstMatchUnconfirmKey + unconfirmedCount - 1, LastMatchUnconfirmKey);
            var banSec = SchemeValueOf(hangupSchemeID, matchUnconfirmKey);
            var isPenalized = banSec > NoBanSec;
            var creditPoint = isPenalized
                ? Math.Max(MinCreditPoint, record.CreditPoint - SchemeValueOf(hangupSchemeID, GameStartUnconfirmKey))
                : record.CreditPoint;
            var banEndTime = isPenalized
                ? (now.ToUnixTimeMilliseconds() + (banSec * MillisecondsPerSecond) + MillisecondsPerSecond - 1) / MillisecondsPerSecond
                : record.BanEndTime;
            logger.LogInformation(
                "Uid {Uid} left match {UnconfirmedCount} of the day unconfirmed on hang-up scheme {HangupSchemeID}: credit {CreditPoint}, battle ban until {BanEndTime}",
                uid,
                unconfirmedCount,
                hangupSchemeID,
                creditPoint,
                banEndTime);
            var reduceRecord = creditPoint == record.CreditPoint
                ? null
                : BuildReduceRecord(mapId, now.ToUnixTimeSeconds(), PB_ENUM_ENM_CREDIT_DEDUCE_REASON.PbEnmCreditDeduceReasonNotConfirmMatch, record.CreditPoint - creditPoint, NoHeroID, NoDeskUniqId);
            return (record with { CreditPoint = creditPoint, UnconfirmedDay = today, UnconfirmedCount = unconfirmedCount, BanEndTime = banEndTime }, reduceRecord, new CreditPenalty(isPenalized, creditPoint, banEndTime));
        });
    }

    public async Task SendUnconfirmedMatchMailAsync(ulong uid)
    {
        await mail.SendSystemMailAsync(uid, UnconfirmedMatchMail);
        await lobbyRoles.PushAsync(uid, await mail.BuildUnreadRedDotPacketsAsync(uid, AccusationMailType));
    }

    public int FindGameStartNeedCredit(int mapId) =>
        SchemeValueOf(gameData.SceneMap.Find(mapId)?.HangupSchemeID ?? 0, GameStartNeedCreditKey);

    public PB_ENUM_ENM_HANGUP_TYPE FindHangupType(int mapId, int hangupSeconds)
    {
        var hangupSchemeID = HangupSchemeIDOf(mapId);
        return ReachesHangup(hangupSchemeID, MaliciousHandupTotalTimeKey, hangupSeconds) ? PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMalicious
            : ReachesHangup(hangupSchemeID, SevereHandupMinHandupTimeKey, hangupSeconds) ? PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeSevere
            : ReachesHangup(hangupSchemeID, MildHandupMinAfkTimeKey, hangupSeconds) ? PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMild
            : PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeNone;
    }

    public int ReduceHangupIncome(int income, PB_ENUM_ENM_HANGUP_TYPE hangupType)
    {
        var reduceRatio = hangupType switch
        {
            PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMild => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalMildHangupIncomeReduceRatio),
            PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeSevere => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalSevereHangupIncomeReduceRatio),
            PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMalicious => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalMaliciousHangupReduceRatio),
            _ => 0,
        };
        return income - (income * Math.Clamp(reduceRatio, 0, HangupIncomeRatioBase) / HangupIncomeRatioBase);
    }

    public async Task<pbSCCreditPointNtf?> PenalizeHangupAsync(ulong uid, CreditReduceBattle battle, PB_ENUM_ENM_HANGUP_TYPE hangupType)
    {
        var hangupSchemeID = HangupSchemeIDOf(battle.MapId);
        var deduction = hangupType switch
        {
            PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMild => SchemeValueOf(hangupSchemeID, MildHandupDeduceCreditpointKey),
            PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeSevere => SchemeValueOf(hangupSchemeID, SevereHandupDeduceCreditpointKey),
            PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMalicious => SchemeValueOf(hangupSchemeID, MaliciousHandupDeduceCreditpointKey),
            _ => 0,
        };
        return await UpdateAsync(uid, record =>
        {
            var creditPoint = Math.Max(MinCreditPoint, record.CreditPoint - Math.Max(0, deduction));
            logger.LogInformation(
                "Uid {Uid} idled ({HangupType}) on hang-up scheme {HangupSchemeID}: credit {Before} -> {After}",
                uid,
                hangupType,
                hangupSchemeID,
                record.CreditPoint,
                creditPoint);
            return creditPoint == record.CreditPoint
                ? (record, (CreditReduceRecord?)null, (pbSCCreditPointNtf?)null)
                : (record with { CreditPoint = creditPoint },
                    BuildReduceRecord(battle, PB_ENUM_ENM_CREDIT_DEDUCE_REASON.PbEnmCreditDeduceReasonHangup, record.CreditPoint - creditPoint),
                    new pbSCCreditPointNtf { CurrentCreditPoint = creditPoint });
        });
    }

    public async Task<pbSCCreditPointNtf?> RecoverAfterBattleAsync(ulong uid, int mapId, bool isManMachine)
    {
        var hangupSchemeID = HangupSchemeIDOf(mapId);
        var recovery = Math.Max(0, SchemeValueOf(hangupSchemeID, isManMachine ? ManmachineResumeCreditKey : BattleResumeCreditKey));
        var dailyLimit = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalCompleteResumeCreditDailyLimit);
        return await UpdateAsync(uid, record =>
        {
            var today = clock.GetUtcNow().ToUnixTimeSeconds() / SecondsPerDay;
            var resumedToday = record.ResumeDay == today ? record.ResumeCredit : 0;
            var allowed = Math.Clamp(dailyLimit - resumedToday, 0, recovery);
            var creditPoint = Math.Min(MaxCreditPoint, record.CreditPoint + allowed);
            var resumed = Math.Max(0, creditPoint - record.CreditPoint);
            logger.LogInformation(
                "Uid {Uid} recovers credit after a battle on hang-up scheme {HangupSchemeID}: {Before} -> {After}, {ResumedToday} of {DailyLimit} recovered today",
                uid,
                hangupSchemeID,
                record.CreditPoint,
                record.CreditPoint + resumed,
                resumedToday + resumed,
                dailyLimit);
            return resumed == 0
                ? (record, (CreditReduceRecord?)null, (pbSCCreditPointNtf?)null)
                : (record with { CreditPoint = record.CreditPoint + resumed, ResumeDay = today, ResumeCredit = resumedToday + resumed },
                    null,
                    new pbSCCreditPointNtf { CurrentCreditPoint = record.CreditPoint + resumed });
        });
    }

    public async Task<pbCOMDT_CREDIT_REDUCE_RECORD_LIST> BuildReduceRecordListAsync(ulong uid) => new()
    {
        RecordList =
        {
            (await repository.ListReduceRecordsAsync(uid)).Select(saved => new pbCOMDT_CREDIT_REDUCE_RECORD
            {
                GameStartTime = saved.GameStartTime,
                MapMode = saved.MapMode,
                MapSubMode = saved.MapSubMode,
                Reason = saved.Reason,
                ReducePoint = saved.ReducePoint,
                HeroID = saved.HeroID,
                DeskUniqId = saved.DeskUniqId,
            }),
        },
    };

    public async Task<int> PunishAccusedHangupAsync(ulong accusedUid, string battleID)
    {
        var punishCredit = Math.Max(0, gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalAccusationPunishCreditOnHangup));
        var creditPointNtf = await WithGateAsync(accusedUid, async () =>
        {
            var recordsOfDesk = await repository.FindReduceRecordsOfDeskAsync(accusedUid, battleID);
            var hangupRecord = recordsOfDesk.FirstOrDefault(saved => saved.Reason == (int)PB_ENUM_ENM_CREDIT_DEDUCE_REASON.PbEnmCreditDeduceReasonHangup);
            var alreadyPunished = recordsOfDesk.Any(saved => saved.Reason == (int)PB_ENUM_ENM_CREDIT_DEDUCE_REASON.PbEnmCreditDeduceReasonAccusationHangup);
            if (hangupRecord is null || alreadyPunished || punishCredit == 0)
            {
                return null;
            }

            var record = await LoadAsync(accusedUid);
            var creditPoint = Math.Max(MinCreditPoint, record.CreditPoint - punishCredit);
            logger.LogInformation(
                "Uid {Uid} reported for idling in battle {BattleID}: credit {Before} -> {After}",
                accusedUid,
                battleID,
                record.CreditPoint,
                creditPoint);
            var reduceRecord = hangupRecord with
            {
                Reason = (int)PB_ENUM_ENM_CREDIT_DEDUCE_REASON.PbEnmCreditDeduceReasonAccusationHangup,
                ReducePoint = record.CreditPoint - creditPoint,
            };
            await repository.SaveAsync(accusedUid, record with { CreditPoint = creditPoint }, reduceRecord, MaxDeduceCreditPointRecordNum);
            return new pbSCCreditPointNtf { CurrentCreditPoint = creditPoint };
        });
        if (creditPointNtf is null)
        {
            return 0;
        }

        await lobbyRoles.PushAsync(accusedUid, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidCreditPointNtf, creditPointNtf));
        return punishCredit;
    }

    private Task<T> UpdateAsync<T>(ulong uid, Func<CreditRecord, (CreditRecord Saved, CreditReduceRecord? Reduce, T Result)> change) =>
        WithGateAsync(uid, async () =>
        {
            var record = await LoadAsync(uid);
            var (saved, reduce, result) = change(record);
            if (saved != record || reduce is not null)
            {
                await repository.SaveAsync(uid, saved, reduce, MaxDeduceCreditPointRecordNum);
            }

            return result;
        });

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

    private CreditReduceRecord BuildReduceRecord(CreditReduceBattle battle, PB_ENUM_ENM_CREDIT_DEDUCE_REASON reason, int reducePoint) =>
        BuildReduceRecord(battle.MapId, battle.GameStartTime, reason, reducePoint, battle.HeroID, battle.DeskUniqId);

    private CreditReduceRecord BuildReduceRecord(int mapId, long gameStartTime, PB_ENUM_ENM_CREDIT_DEDUCE_REASON reason, int reducePoint, int heroID, string deskUniqId)
    {
        var sceneMap = gameData.SceneMap.Find(mapId);
        return new CreditReduceRecord(gameStartTime, sceneMap?.MapMode ?? 0, sceneMap?.MapSubMode ?? 0, (int)reason, reducePoint, heroID, deskUniqId);
    }

    private int HangupSchemeIDOf(int mapId) => gameData.SceneMap.Find(mapId)?.HangupSchemeID ?? 0;

    private bool ReachesHangup(int hangupSchemeID, int keyID, int hangupSeconds) =>
        SchemeValueOf(hangupSchemeID, keyID) is > 0 and var threshold && hangupSeconds >= threshold;

    private int SchemeValueOf(int hangupSchemeID, int keyID) =>
        gameData.HangupScheme.Find(pbHangupScheme.Key(hangupSchemeID, keyID))?.Value ?? 0;

    private async Task<CreditRecord> LoadAsync(ulong uid) =>
        await repository.FindAsync(uid) ?? new CreditRecord(StartingCreditPoint, 0, 0, 0, 0, 0);
}
