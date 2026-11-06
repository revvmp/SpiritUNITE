using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Online;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Credit;

public sealed record CreditPenalty(bool IsPenalized, int CreditPoint, long BanEndTime);

public sealed class CreditManager(
    CreditRepository repository,
    GameData gameData,
    MailManager mail,
    LobbyRoles lobbyRoles,
    IOptions<LobbyOptions> lobbyOptions,
    TimeProvider clock,
    ILogger<CreditManager> logger)
{
    public static readonly TimeSpan UnconfirmedMatchMailDelay = TimeSpan.FromSeconds(3);

    private const long SecondsPerDay = 86400;
    private const long MillisecondsPerSecond = 1000;
    private const int MinCreditPoint = 0;
    private const int MaxCreditPoint = 100;
    private const int NoBanSec = 0;
    private const int UnconfirmedMatchMailID = 12;
    private const int AccusationMailType = (int)PB_ENUM_RES_MAIL_TYPE.PbResMailTypeAccusation;
    private const int GameStartUnconfirmKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalGameStartUnconfirm;
    private const int FirstMatchUnconfirmKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalMatchUnconfirm1;
    private const int LastMatchUnconfirmKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalMatchUnconfirmMoreThan6;
    private const int BattleResumeCreditKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalBattleResumeCredit;
    private const int ManmachineResumeCreditKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalManmachineResumeCredit;
    private const int GameStartNeedCreditKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalGameStartNeedCredit;

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
            return (new CreditRecord(creditPoint, today, unconfirmedCount, banEndTime), new CreditPenalty(isPenalized, creditPoint, banEndTime));
        });
    }

    public async Task SendUnconfirmedMatchMailAsync(ulong uid)
    {
        await mail.SendSystemMailAsync(uid, UnconfirmedMatchMail);
        await lobbyRoles.PushAsync(uid, await mail.BuildUnreadRedDotPacketsAsync(uid, AccusationMailType));
    }

    public int FindGameStartNeedCredit(int mapId) =>
        SchemeValueOf(gameData.SceneMap.Find(mapId)?.HangupSchemeID ?? 0, GameStartNeedCreditKey);

    public async Task<pbSCCreditPointNtf?> RecoverAfterBattleAsync(ulong uid, int mapId, bool isManMachine)
    {
        var hangupSchemeID = gameData.SceneMap.Find(mapId)?.HangupSchemeID ?? 0;
        var recovery = SchemeValueOf(hangupSchemeID, isManMachine ? ManmachineResumeCreditKey : BattleResumeCreditKey);
        return await UpdateAsync(uid, record =>
        {
            var creditPoint = Math.Min(MaxCreditPoint, record.CreditPoint + Math.Max(0, recovery));
            logger.LogInformation(
                "Uid {Uid} recovers credit after a battle on hang-up scheme {HangupSchemeID}: {Before} -> {After}",
                uid,
                hangupSchemeID,
                record.CreditPoint,
                creditPoint);
            return creditPoint == record.CreditPoint
                ? (record, (pbSCCreditPointNtf?)null)
                : (record with { CreditPoint = creditPoint }, new pbSCCreditPointNtf { CurrentCreditPoint = creditPoint });
        });
    }

    private async Task<T> UpdateAsync<T>(ulong uid, Func<CreditRecord, (CreditRecord Saved, T Result)> change)
    {
        var gate = gates.GetOrAdd(uid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var record = await LoadAsync(uid);
            var (saved, result) = change(record);
            if (saved != record)
            {
                await repository.SaveAsync(uid, saved);
            }

            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private int SchemeValueOf(int hangupSchemeID, int keyID) =>
        gameData.HangupScheme.Find(pbHangupScheme.Key(hangupSchemeID, keyID))?.Value ?? 0;

    private async Task<CreditRecord> LoadAsync(ulong uid) =>
        await repository.FindAsync(uid) ?? new CreditRecord(lobbyOptions.Value.StartingCreditPoint, 0, 0, 0);
}
