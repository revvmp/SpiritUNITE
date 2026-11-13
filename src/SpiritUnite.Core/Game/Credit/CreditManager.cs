using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Mail;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.Credit;

public sealed record CreditPenalty(bool IsPenalized, int CreditPoint, long BanEndTime);

public sealed record CreditSettlementPlan(long Day, int Recovery, int DailyLimit, int Deduction);

public sealed record HangupPenalty(int ReducePoint, int CreditPoint, pbSCCreditPointNtf? CreditPointNtf);

public sealed record HangupPunishNotice(
    Guid SettlementID,
    ulong Uid,
    string InGameUsrName,
    PB_ENUM_ENM_HANGUP_TYPE HangupType,
    long GameStartTime,
    HangupPenalty Penalty,
    IReadOnlyList<ulong> TeammateUids);

public sealed record CreditReduceBattle(int MapId, long GameStartTime, int HeroID, string DeskUniqId)
{
    public static string BattleIDOf(int deskID, long deskCrtSeq, int relayEntity) => $"{deskID}_{deskCrtSeq}_{relayEntity}";
}

public sealed class CreditManager(
    CreditRepository repository,
    GameData gameData,
    MailManager mail,
    SettlementMailRepository settlementMails,
    LobbyRoles lobbyRoles,
    BackgroundOperations background,
    TimeProvider clock,
    ILogger<CreditManager> logger)
{
    public const int StartingCreditPoint = (int)PB_MACRO_COM_CREDIT_POINT_INIT_VALUE.PbComCreditPointInitValue;

    public static readonly TimeSpan UnconfirmedMatchMailDelay = TimeSpan.FromSeconds(3);

    public static readonly TimeSpan HangupPunishMailDelay = TimeSpan.FromSeconds(3);

    private const long SecondsPerDay = 86400;
    private const long MillisecondsPerSecond = 1000;
    private const int MinCreditPoint = 0;
    private const int MaxCreditPoint = StartingCreditPoint;
    private const int NoBanSec = 0;
    private const long NoBanEndTime = 0;
    private const int UnconfirmedMatchMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeHangupPunish;
    private const int MildHangupPunishMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeMildHangupPunish;
    private const int SevereMaliciousHangupPunishMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeSevereMaliciousHangupPunish;
    private const int SysAutoPunishMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeSysAutoPunish;
    private const string PunishMailSubject = "Mail_TextTitle_12_001";
    private const string HangupPunishMailContent = "System_Behaviour_Notice_Email_12030_003";
    private const string SysAutoPunishMailContent = "System_Behaviour_Notice_23924_001";
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
    private const int NoMapMode = 0;
    private const int NoMapSubMode = 0;
    private const string NoDeskUniqId = "";

    private static readonly SystemMail UnconfirmedMatchMail = new(
        Subject: PunishMailSubject,
        Content: "Mail_TextContent_12_001",
        Attachments: [],
        MailType: AccusationMailType,
        MailID: UnconfirmedMatchMailID);

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> gates = new();
    private readonly ConcurrentDictionary<Guid, List<HangupPunishNotice>> hangupPunishBatches = new();

    public async Task<int> GetCreditPointAsync(ulong uid) => (await LoadAsync(uid)).CreditPoint;

    public static bool IsCreditPoint(int creditPoint) => creditPoint is >= MinCreditPoint and <= MaxCreditPoint;

    public async Task<bool> SetCreditPointAsync(ulong uid, int creditPoint)
    {
        if (!IsCreditPoint(creditPoint))
        {
            return false;
        }

        var creditPointNtf = await UpdateAsync(uid, record =>
        {
            logger.LogInformation("Uid {Uid} credit set by an admin: {Before} -> {After}", uid, record.CreditPoint, creditPoint);
            return (record with { CreditPoint = creditPoint }, (CreditReduceRecord?)null, new pbSCCreditPointNtf { CurrentCreditPoint = creditPoint });
        });
        await lobbyRoles.PushAsync(uid, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidCreditPointNtf, creditPointNtf));
        return true;
    }

    public async Task<bool> ClearBattleBanAsync(ulong uid) =>
        await UpdateAsync(uid, record => record.BanEndTime == NoBanEndTime
            ? (record, (CreditReduceRecord?)null, false)
            : (record with { BanEndTime = NoBanEndTime }, null, true));

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
                ? CeilingUnixSeconds(now.AddSeconds(banSec))
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
                : BuildUnconfirmedMatchReduceRecord(CeilingUnixSeconds(now), record.CreditPoint - creditPoint);
            return (record with { CreditPoint = creditPoint, UnconfirmedDay = today, UnconfirmedCount = unconfirmedCount, BanEndTime = banEndTime }, reduceRecord, new CreditPenalty(isPenalized, creditPoint, banEndTime));
        });
    }

    public Task SendUnconfirmedMatchMailAsync(ulong uid) => SendPunishMailAsync(uid, UnconfirmedMatchMail);

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

    public int ReduceHangupIncome(int income, PB_ENUM_ENM_HANGUP_TYPE hangupType) =>
        income - (income * HangupIncomeReduceRatioOf(hangupType) / HangupIncomeRatioBase);

    public bool HangupForfeitsIncome(PB_ENUM_ENM_HANGUP_TYPE hangupType) =>
        HangupIncomeReduceRatioOf(hangupType) == HangupIncomeRatioBase;

    public int HangupIncomeReduceRatioOf(PB_ENUM_ENM_HANGUP_TYPE hangupType) => Math.Clamp(
        hangupType switch
        {
            PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMild => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalMildHangupIncomeReduceRatio),
            PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeSevere => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalSevereHangupIncomeReduceRatio),
            PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMalicious => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalMaliciousHangupReduceRatio),
            _ => 0,
        },
        0,
        HangupIncomeRatioBase);

    public Task QueueHangupPunishMailsAsync(HangupPunishNotice notice, SettlementKey key, long now)
    {
        if (key.GameUlongUID != notice.Uid || key.SettlementID != notice.SettlementID.ToString("N"))
        {
            throw new ArgumentException("Settlement identity does not match punishment notice", nameof(key));
        }

        var mails = new List<RoleMail>
        {
            mail.CreateSystemMail(notice.Uid, BuildHangupPunishMail(notice)),
        };
        mails.AddRange(notice.TeammateUids.Distinct().Where(uid => uid != notice.Uid)
            .Select(uid => mail.CreateSystemMail(uid, BuildSysAutoPunishMail(notice))));
        foreach (var saved in mails)
        {
            saved.ExpireTime = now + saved.ExpireTime - saved.SendTime;
            saved.SendTime = now;
        }

        return settlementMails.QueueAsync(key, new SettlementMailPlan(now + (long)HangupPunishMailDelay.TotalSeconds, [.. mails]));
    }

    public void ScheduleHangupPunishMails(HangupPunishNotice notice)
    {
        var isFirstOfBattle = false;
        var batch = hangupPunishBatches.GetOrAdd(notice.SettlementID, _ =>
        {
            isFirstOfBattle = true;
            return [];
        });
        lock (batch)
        {
            batch.Add(notice);
        }

        if (!isFirstOfBattle)
        {
            return;
        }

        if (!background.Run($"hang-up punish mails of settlement {notice.SettlementID:N}", async token =>
            {
                await Task.Delay(HangupPunishMailDelay, clock, token);
                await SendHangupPunishMailsAsync(notice.SettlementID);
            }))
        {
            hangupPunishBatches.TryRemove(notice.SettlementID, out _);
            logger.LogWarning("Uid {Uid} gets no hang-up punish mails: the server is stopping", notice.Uid);
        }
    }

    public CreditSettlementPlan PrepareSettlement(int mapId, bool isManMachine, PB_ENUM_ENM_HANGUP_TYPE hangupType, long settledAt)
    {
        var scheme = HangupSchemeIDOf(mapId);
        return new CreditSettlementPlan(
            settledAt / SecondsPerDay,
            Math.Max(0, SchemeValueOf(scheme, isManMachine ? ManmachineResumeCreditKey : BattleResumeCreditKey)),
            gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalCompleteResumeCreditDailyLimit),
            HangupDeductionOf(scheme, hangupType));
    }

    private int HangupDeductionOf(int hangupSchemeID, PB_ENUM_ENM_HANGUP_TYPE hangupType) => hangupType switch
    {
        PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMild => SchemeValueOf(hangupSchemeID, MildHandupDeduceCreditpointKey),
        PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeSevere => SchemeValueOf(hangupSchemeID, SevereHandupDeduceCreditpointKey),
        PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMalicious => SchemeValueOf(hangupSchemeID, MaliciousHandupDeduceCreditpointKey),
        _ => 0,
    };

    public async Task<HangupPenalty> PenalizeHangupAsync(ulong uid, CreditReduceBattle battle, PB_ENUM_ENM_HANGUP_TYPE hangupType, SettlementKey? settlementKey = null, CreditSettlementPlan? prepared = null)
    {
        var hangupSchemeID = HangupSchemeIDOf(battle.MapId);
        var deduction = prepared?.Deduction ?? HangupDeductionOf(hangupSchemeID, hangupType);
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
            var reducePoint = record.CreditPoint - creditPoint;
            return reducePoint == 0
                ? (record, (CreditReduceRecord?)null, new HangupPenalty(reducePoint, creditPoint, null))
                : (record with { CreditPoint = creditPoint },
                    BuildReduceRecord(battle, PB_ENUM_ENM_CREDIT_DEDUCE_REASON.PbEnmCreditDeduceReasonHangup, reducePoint),
                    new HangupPenalty(reducePoint, creditPoint, new pbSCCreditPointNtf { CurrentCreditPoint = creditPoint }));
        }, settlementKey, "credit.hangup");
    }

    private async Task SendHangupPunishMailsAsync(Guid settlementID)
    {
        if (!hangupPunishBatches.TryRemove(settlementID, out var batch))
        {
            return;
        }

        HangupPunishNotice[] notices;
        lock (batch)
        {
            notices = [.. batch];
        }

        var mailsByReceiver = new Dictionary<ulong, List<SystemMail>>();
        foreach (var notice in notices)
        {
            AddPunishMail(mailsByReceiver, notice.Uid, BuildHangupPunishMail(notice));
        }

        foreach (var notice in notices)
        {
            foreach (var teammateUid in notice.TeammateUids)
            {
                AddPunishMail(mailsByReceiver, teammateUid, BuildSysAutoPunishMail(notice));
            }

            logger.LogInformation(
                "Uid {Uid} gets the {HangupType} hang-up punish mail; teammates {TeammateUids} get the notice",
                notice.Uid,
                notice.HangupType,
                notice.TeammateUids);
        }

        foreach (var (receiverUid, mails) in mailsByReceiver)
        {
            await mail.SendSystemMailBatchAsync(receiverUid, AccusationMailType, mails);
        }
    }

    private static void AddPunishMail(Dictionary<ulong, List<SystemMail>> mailsByReceiver, ulong receiverUid, SystemMail punishMail)
    {
        if (!mailsByReceiver.TryGetValue(receiverUid, out var mails))
        {
            mailsByReceiver[receiverUid] = mails = [];
        }

        mails.Add(punishMail);
    }

    private SystemMail BuildHangupPunishMail(HangupPunishNotice notice) =>
        new(
            Subject: PunishMailSubject,
            Content: HangupPunishMailContent,
            Attachments: [],
            MailType: AccusationMailType,
            MailID: notice.HangupType == PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMild ? MildHangupPunishMailID : SevereMaliciousHangupPunishMailID,
            ContentParams:
            [
                MailContentParam.Time(notice.GameStartTime),
                MailContentParam.Text(HangupTypeNameOf(notice.HangupType)),
                MailContentParam.Number(notice.Penalty.ReducePoint),
                MailContentParam.Number(notice.Penalty.CreditPoint),
                MailContentParam.Number(HangupIncomeReduceRatioOf(notice.HangupType)),
            ]);

    private static SystemMail BuildSysAutoPunishMail(HangupPunishNotice notice) =>
        new(
            Subject: PunishMailSubject,
            Content: SysAutoPunishMailContent,
            Attachments: [],
            MailType: AccusationMailType,
            MailID: SysAutoPunishMailID,
            FromUid: notice.Uid,
            ContentParams:
            [
                MailContentParam.Time(notice.GameStartTime),
                MailContentParam.Text(notice.InGameUsrName),
                MailContentParam.Text(HangupTypeNameOf(notice.HangupType)),
                MailContentParam.Text(notice.InGameUsrName),
                MailContentParam.Number(notice.Penalty.ReducePoint),
            ]);

    private async Task SendPunishMailAsync(ulong uid, SystemMail punishMail)
    {
        await mail.SendSystemMailAsync(uid, punishMail);
    }

    private static string HangupTypeNameOf(PB_ENUM_ENM_HANGUP_TYPE hangupType) => hangupType switch
    {
        PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMild => "ENM_HANGUP_TYPE_MILD",
        PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeSevere => "ENM_HANGUP_TYPE_SEVERE",
        PB_ENUM_ENM_HANGUP_TYPE.PbEnmHangupTypeMalicious => "ENM_HANGUP_TYPE_MALICIOUS",
        _ => throw new ArgumentOutOfRangeException(nameof(hangupType), hangupType, "Only an idle hang-up type has a punish mail"),
    };

    public async Task<Func<pbSCCreditPointNtf?>> EnlistRecoverAfterBattleAsync(SettlementSharedEffects shared, ulong uid, int mapId, bool isManMachine, SettlementKey settlementKey, long settledAt, CreditSettlementPlan? prepared)
    {
        if (settlementKey.GameUlongUID != uid)
        {
            throw new ArgumentException("Settlement uid does not match credit uid", nameof(settlementKey));
        }

        var hangupSchemeID = HangupSchemeIDOf(mapId);
        var recovery = prepared?.Recovery ?? Math.Max(0, SchemeValueOf(hangupSchemeID, isManMachine ? ManmachineResumeCreditKey : BattleResumeCreditKey));
        var dailyLimit = prepared?.DailyLimit ?? gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalCompleteResumeCreditDailyLimit);
        var today = prepared?.Day ?? settledAt / SecondsPerDay;
        var ran = false;
        pbSCCreditPointNtf? creditPointNtf = null;
        await shared.EnlistGatedAsync(
            gates.GetOrAdd(uid, _ => new SemaphoreSlim(1, 1)),
            async database =>
            {
                creditPointNtf = await CreditRepository.RecoverBattleAsync(database, uid, settlementKey, today, recovery, dailyLimit, MaxCreditPoint, StartingCreditPoint);
                ran = true;
            });
        return () => ran ? creditPointNtf : throw new InvalidOperationException($"Credit recovery of uid {uid} was read before its settlement transaction ran");
    }

    public async Task<pbSCCreditPointNtf?> RecoverAfterBattleAsync(ulong uid, int mapId, bool isManMachine, SettlementKey? settlementKey = null, long? settledAt = null, CreditSettlementPlan? prepared = null)
    {
        if (settlementKey is not null && settlementKey.GameUlongUID != uid)
        {
            throw new ArgumentException("Settlement uid does not match credit uid", nameof(settlementKey));
        }

        var hangupSchemeID = HangupSchemeIDOf(mapId);
        var recovery = prepared?.Recovery ?? Math.Max(0, SchemeValueOf(hangupSchemeID, isManMachine ? ManmachineResumeCreditKey : BattleResumeCreditKey));
        var dailyLimit = prepared?.DailyLimit ?? gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalCompleteResumeCreditDailyLimit);
        var today = prepared?.Day ?? (settledAt ?? clock.GetUtcNow().ToUnixTimeSeconds()) / SecondsPerDay;
        return await WithGateAsync(uid, () => repository.RecoverBattleAsync(uid, settlementKey, today, recovery, dailyLimit, MaxCreditPoint, StartingCreditPoint));
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

    private Task<T> UpdateAsync<T>(ulong uid, Func<CreditRecord, (CreditRecord Saved, CreditReduceRecord? Reduce, T Result)> change, SettlementKey? settlementKey = null, string effect = "") =>
        WithGateAsync(uid, async () =>
        {
            if (settlementKey is not null)
            {
                if (settlementKey.GameUlongUID != uid)
                {
                    throw new ArgumentException("Settlement uid does not match credit uid", nameof(settlementKey));
                }

                return await repository.ApplySettlementAsync(settlementKey, effect,
                    new CreditRecord(StartingCreditPoint, 0, 0, 0, 0, 0), MaxDeduceCreditPointRecordNum, change);
            }

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

    private static CreditReduceRecord BuildUnconfirmedMatchReduceRecord(long gameStartTime, int reducePoint) =>
        new(gameStartTime, NoMapMode, NoMapSubMode, (int)PB_ENUM_ENM_CREDIT_DEDUCE_REASON.PbEnmCreditDeduceReasonNotConfirmMatch, reducePoint, NoHeroID, NoDeskUniqId);

    private static long CeilingUnixSeconds(DateTimeOffset time) =>
        (time.ToUnixTimeMilliseconds() + MillisecondsPerSecond - 1) / MillisecondsPerSecond;

    private int HangupSchemeIDOf(int mapId) => gameData.SceneMap.Find(mapId)?.HangupSchemeID ?? 0;

    private bool ReachesHangup(int hangupSchemeID, int keyID, int hangupSeconds) =>
        SchemeValueOf(hangupSchemeID, keyID) is > 0 and var threshold && hangupSeconds >= threshold;

    private int SchemeValueOf(int hangupSchemeID, int keyID) =>
        gameData.HangupScheme.Find(pbHangupScheme.Key(hangupSchemeID, keyID))?.Value ?? 0;

    private async Task<CreditRecord> LoadAsync(ulong uid) =>
        await repository.FindAsync(uid) ?? new CreditRecord(StartingCreditPoint, 0, 0, 0, 0, 0);
}
