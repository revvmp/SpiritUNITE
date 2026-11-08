using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.Ranked;

public sealed record RankedState(
    int ProtectCardSelectStatus,
    long BanRankRaceEndTime,
    long NoRemindedBuyProtectCardDay,
    RankedSeasonRecord Season,
    bool SeasonStored,
    RankedSeasonRecord? PendingSeason,
    IReadOnlySet<int> AwardedRankedDivisions);

public sealed record RankedSessionRecord(int BattleCnt, int ContinuousWin, int ContinuousLose)
{
    public static readonly RankedSessionRecord Empty = new(0, 0, 0);

    public RankedSessionRecord After(bool won, bool isDraw) => new(
        BattleCnt + 1,
        won ? ContinuousWin + 1 : 0,
        !won && !isDraw ? ContinuousLose + 1 : 0);
}

public sealed record RankedStartRefusal(PB_ENUM_RES_ERROR_CODE Result, pbArgList? Args = null);

public sealed record ExpiredProtectCard(int ItemId, int Cnt, int CoinId, long CoinCnt);

public sealed record RankedBattle(bool Won, bool IsDraw, IReadOnlyList<pbCOMDT_SETTLE_MEDAlPOINT_INFO> Medals);

public sealed record RankedSettlement(pbCOMDT_CATCH_SETTLE_RESULT CatchSettleResult, pbSCRankedMatchInfoNtf InfoNtf);

public sealed record FreeTicketCardUse(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<BagItem> ChangedItems, pbSCRankedMatchInfoNtf? InfoNtf);

public sealed record RankedSeasonRollover(
    pbCOMDT_RANKED_DIVISION OldRankedDivision,
    pbCOMDT_RANKED_DIVISION NewRankedDivision,
    RankedGrant SeasonAward,
    RankedGrantCommit Commit);

public sealed record RankedTaskAwardClaim(PB_ENUM_RES_ERROR_CODE Result, RankedGrant? Grant, RankedGrantCommit? Commit)
{
    public static RankedTaskAwardClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null, null);
}

public enum RankedSeasonStatus
{
    Current,
    AwaitingSeasonRewardConfirm,
    AwaitingSeasonRollover,
}

public sealed class RankedManager
{
    public const int UseProtectCard = 0;
    public const int NotUseProtectCard = 1;

    private const int FallProtected = 1;
    private const int OneFreeTicketCard = 1;
    private const int FreeTicketInUse = 1;
    private const int ThreeRankedTeam = (int)PB_ENUM_ENM_RANKED_TEAM_MEMBER_NUM_TYPE.PbEnmRankedTeamMemberNumTypeThree;
    private const int FiveRankedTeam = (int)PB_ENUM_ENM_RANKED_TEAM_MEMBER_NUM_TYPE.PbEnmRankedTeamMemberNumTypeFive;
    private const int SingleRankedTeam = (int)PB_ENUM_ENM_RANKED_TEAM_MEMBER_NUM_TYPE.PbEnmRankedTeamMemberNumTypeSingle;
    private const int DefaultProtectCardItemID = (int)PB_ENUM_ENM_RESOURCES_ID.PbEnmResourcesIdRankProtectCard;
    private const int NoProtectCardItemID = 0;
    private const int NoAwardGroupID = 0;
    private const int NoTicketConsume = 0;
    private const int NoMedalPoint = 0;
    private const int NotHungUpPercent = 10000;
    private const int NoSeasonAwardID = 0;
    private const long NotBanned = 0;
    private const long NoRemindedDay = 0;
    private const int TodaySetNoReminded = 1;
    private const int TodayNotSetNoReminded = 0;
    private const long SecondsPerDay = 86400;
    private const int ExpireTransNumScale = 10000;
    private const int ProtectCardSeasonParam = 0;
    private const int RankedUngetAwardMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeRankedUngetAward;
    private const string RankedUngetAwardMailSubject = "System_BattlePass_Rewards_Email_Subject_8925_001";
    private const string RankedUngetAwardMailContent = "System_BattlePass_Rewards_Email_Content_8926_001";
    private const int ItemExpiredMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeItemexpired;
    private const string ItemExpiredMailSubject = "System_Email_ItemExpired_Subject_001";
    private const string ItemExpiredMailContent = "System_Email_ItemExpired_Content_002";

    private static readonly HashSet<int> SeasonBoundProtectCardTypes =
    [
        (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeRankProtectCard,
        (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeFreeTicketCard,
    ];

    private readonly RankedRepository repository;
    private readonly GameData gameData;
    private readonly RankedLadder ladder;
    private readonly BagManager bag;
    private readonly RankedRewards rewards;
    private readonly MailManager mail;
    private readonly LobbyNotices notices;
    private readonly IOptionsMonitor<RankedOptions> rankedOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<RankedManager> logger;
    private readonly ConcurrentDictionary<ulong, RankedState> states = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> gates = new();
    private readonly ConcurrentDictionary<ulong, RankedSessionRecord> sessions = new();

    public RankedManager(
        RankedRepository repository,
        GameData gameData,
        RankedLadder ladder,
        BagManager bag,
        RankedRewards rewards,
        MailManager mail,
        LobbyNotices notices,
        IOptionsMonitor<RankedOptions> options,
        TimeProvider clock,
        ILogger<RankedManager> logger)
    {
        this.repository = repository;
        this.gameData = gameData;
        this.ladder = ladder;
        this.bag = bag;
        this.rewards = rewards;
        this.mail = mail;
        this.notices = notices;
        rankedOptions = options;
        this.clock = clock;
        this.logger = logger;
    }

    public RankedLadder Ladder => ladder;

    private RankedOptions Options => rankedOptions.CurrentValue;

    public int CurrentSeasonId => RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId;

    public int MasterPointMatchBand => ladder.MasterPointClassWidth;

    public async Task<RankedState> LoadAsync(ulong uid, bool reload = false)
    {
        var seasonId = CurrentSeasonId;
        if (!reload && states.TryGetValue(uid, out var cached) && cached.Season.SeasonId == seasonId)
        {
            return cached;
        }

        if (reload)
        {
            sessions.TryRemove(uid, out _);
        }

        var role = await repository.FindRoleAsync(uid);
        var stored = await repository.FindSeasonAsync(uid, seasonId);
        var pending = stored is null && await repository.FindLatestSeasonAsync(uid) is { } latest && latest.SeasonId < seasonId
            ? latest
            : null;
        var state = new RankedState(
            role?.ProtectCardSelectStatus ?? UseProtectCard,
            role?.BanRankRaceEndTime ?? NotBanned,
            role?.NoRemindedBuyProtectCardDay ?? NoRemindedDay,
            stored ?? StartingSeason(seasonId),
            stored is not null,
            pending,
            (await repository.FindTaskAwardsAsync(uid, seasonId)).ToHashSet());
        states[uid] = state;
        return state;
    }

    public async Task LoadAllAsync(IEnumerable<ulong> uids)
    {
        foreach (var uid in uids)
        {
            await LoadAsync(uid);
        }
    }

    public RankedPosition PositionOf(ulong uid) =>
        states.TryGetValue(uid, out var state) && state.Season.SeasonId == CurrentSeasonId
            ? state.Season.Position
            : RankedLadder.UnplacedPosition;

    public int BigOf(ulong uid) => ladder.BigOf(PositionOf(uid).RankedDivision);

    public bool MatchesByMasterPoint(ulong uid) =>
        ladder.IsAtLeast(
            PositionOf(uid).RankedDivision,
            gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalRankedMatchMasterLegendMasterRankedVisision));

    public bool NeedsSeasonRollover(ulong uid) =>
        states.TryGetValue(uid, out var state)
        && (state.PendingSeason is not null || (state.SeasonStored && state.Season.SeasonId != CurrentSeasonId));

    public int ProtectCardSelectStatusOf(ulong uid) =>
        states.TryGetValue(uid, out var state) ? state.ProtectCardSelectStatus : UseProtectCard;

    public pbCOMDT_RANKED_DIVISION BuildRankedDivision(ulong uid)
    {
        return ToDivision(PositionOf(uid), CurrentSeasonId);
    }

    public async Task<pbCSDT_RANKED_MATCH_DETAIL> BuildDetailAsync(ulong uid)
    {
        var state = await LoadAsync(uid);
        return BuildDetail(
            state.Season,
            RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).RankedMatchState,
            state.AwardedRankedDivisions,
            await BuildHighestRankedDivisionInHistoryAsync(uid));
    }

    public async Task<pbCOMDT_RANKED_DIVISION> BuildHighestRankedDivisionInHistoryAsync(ulong uid)
    {
        var highest = (await repository.ListSeasonsAsync(uid))
            .Where(season => season.Position.RankedDivision != RankedLadder.UnplacedRankedDivision)
            .OrderByDescending(season => ladder.IndexOf(season.Position.RankedDivision))
            .ThenByDescending(season => season.Position.MasterPoint)
            .ThenByDescending(season => season.Position.WinPoint)
            .FirstOrDefault();
        return highest is null ? new pbCOMDT_RANKED_DIVISION() : ToDivision(highest.Position, highest.SeasonId);
    }

    public async Task<RankedSeasonStatus> FindSeasonStatusAsync(ulong uid)
    {
        var state = await LoadAsync(uid);
        if (state.PendingSeason is not { } pending)
        {
            return RankedSeasonStatus.Current;
        }

        return NeedsSeasonRewardConfirm(pending.SeasonId)
            ? RankedSeasonStatus.AwaitingSeasonRewardConfirm
            : RankedSeasonStatus.AwaitingSeasonRollover;
    }

    public async Task<pbCSDT_RANKED_MATCH_DETAIL?> BuildPendingSeasonDetailAsync(ulong uid) =>
        (await LoadAsync(uid)).PendingSeason is { } pending
            ? BuildDetail(pending, PB_ENUM_COM_RANK_STATE_TYPE.PbComRankStateOfSeasonover, new HashSet<int>(), await BuildHighestRankedDivisionInHistoryAsync(uid))
            : null;

    public bool NeedsSeasonRewardConfirm(int seasonId) =>
        gameData.RankedMatchSeason.Find(seasonId) is not { AwardGroupID: > NoAwardGroupID };

    public async Task<RankedSeasonRollover?> RolloverSeasonAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        RankedSeasonRollover? rollover = null;
        await UpdateAsync(uid, async state =>
        {
            if (state.PendingSeason is not { } old || state.SeasonStored)
            {
                return (state, false);
            }

            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            var newSeasonId = state.Season.SeasonId;
            var inherited = InheritedPosition(old.Position);
            var newSeason = new RankedSeasonRecord(newSeasonId, inherited, inherited.RankedDivision, inherited.MasterPoint, 0, 0, Entry: null);
            var expiredCards = await PlanExpiredProtectCardsAsync(uid, newSeasonId);
            var seasonAward = WithCosts(PlanSeasonAward(role, old, now), expiredCards);
            var commit = await repository.CommitSeasonRolloverAsync(uid, newSeason, seasonAward, now);
            if (!commit.Committed)
            {
                logger.LogInformation("Uid {Uid} ranked season {SeasonId} rollover not committed: the season row already exists", uid, newSeasonId);
                return (await LoadAsync(uid, reload: true), true);
            }

            foreach (var item in seasonAward.Items)
            {
                role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
            }

            rollover = new RankedSeasonRollover(ToDivision(old.Position, old.SeasonId), ToDivision(inherited, newSeasonId), seasonAward, commit);
            await MailExpiredProtectCardsAsync(uid, expiredCards);
            await MailUnclaimedSeasonTaskAwardsAsync(role, old, now);
            logger.LogInformation(
                "Uid {Uid} ranked season {OldSeasonId} -> {NewSeasonId}: division {OldDivision} ({OldWinPoint} wp, {OldMasterPoint} mp, highest {HighestDivision}) inherits {NewDivision}; season award of {AwardCount} rewards",
                uid,
                old.SeasonId,
                newSeasonId,
                old.Position.RankedDivision,
                old.Position.WinPoint,
                old.Position.MasterPoint,
                old.HighestRankedDivision,
                inherited.RankedDivision,
                seasonAward.Awards.Count);
            return (state with { Season = newSeason, SeasonStored = true, PendingSeason = null, AwardedRankedDivisions = new HashSet<int>() }, true);
        });
        return rollover;
    }

    public async Task<RankedTaskAwardClaim> ClaimSeasonTaskAwardAsync(LMNRoleInfo role, int rankedDivision)
    {
        var uid = role.GameUlongUID;
        RankedTaskAwardClaim? claim = null;
        await UpdateAsync(uid, async state =>
        {
            var season = state.Season;
            var rows = SeasonTaskAwards(season.SeasonId).ToArray();
            pbRankedMatchSeasonTaskAward[] requested = rankedDivision == RankedMatch.AllSeasonTaskAwards
                ? [.. rows.Where(row => !state.AwardedRankedDivisions.Contains(row.RankedDivision) && CanGainSeasonTaskAward(row, season))]
                : [.. rows.Where(row => row.RankedDivision == rankedDivision)];
            if (RefuseSeasonTaskAward(state, season, rankedDivision, requested) is { } refusal)
            {
                claim = RankedTaskAwardClaim.Refused(refusal);
                return (state, false);
            }

            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            if (rewards.Plan(role, requested.Select(row => row.SDropID), now) is not { } grant)
            {
                claim = RankedTaskAwardClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
                return (state, false);
            }

            int[] claimed = [.. requested.Select(row => row.RankedDivision).Distinct()];
            var commit = await repository.CommitTaskAwardAsync(uid, season.SeasonId, claimed, grant, now);
            if (!commit.Committed)
            {
                claim = RankedTaskAwardClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
                return (await LoadAsync(uid, reload: true), true);
            }

            foreach (var item in grant.Items)
            {
                role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
            }

            claim = new RankedTaskAwardClaim(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, grant, commit);
            logger.LogInformation(
                "Uid {Uid} claimed ranked season {SeasonId} task awards [{RankedDivisions}]: {AwardCount} rewards",
                uid,
                season.SeasonId,
                string.Join(", ", claimed),
                grant.Awards.Count);
            return (state with { AwardedRankedDivisions = state.AwardedRankedDivisions.Union(claimed).ToHashSet() }, true);
        });
        claim ??= RankedTaskAwardClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        if (claim.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Ranked season task award {RankedDivision} refused for uid {Uid}: {Result}", rankedDivision, uid, claim.Result);
        }

        return claim;
    }

    public async Task<int> TakeIsTodaySetNoRemindedAsync(ulong uid)
    {
        var today = Today;
        var isTodaySetNoReminded = TodayNotSetNoReminded;
        await UpdateAsync(uid, async state =>
        {
            if (state.NoRemindedBuyProtectCardDay == today)
            {
                isTodaySetNoReminded = TodaySetNoReminded;
                return (state, false);
            }

            await repository.SaveNoRemindedBuyProtectCardDayAsync(uid, today);
            return (state with { NoRemindedBuyProtectCardDay = today }, true);
        });
        return isTodaySetNoReminded;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetNoRemindedBuyProtectCardAsync(ulong uid, int isTodaySetNoReminded)
    {
        if (isTodaySetNoReminded is not (TodaySetNoReminded or TodayNotSetNoReminded))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        var noRemindedDay = isTodaySetNoReminded == TodaySetNoReminded ? Today : NoRemindedDay;
        await UpdateAsync(uid, async state =>
        {
            await repository.SaveNoRemindedBuyProtectCardDayAsync(uid, noRemindedDay);
            return (state with { NoRemindedBuyProtectCardDay = noRemindedDay }, true);
        });
        logger.LogInformation("Uid {Uid} set the protection card offer reminder to {Reminder} for today", uid, isTodaySetNoReminded == TodaySetNoReminded ? "off" : "on");
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private long Today => clock.GetUtcNow().ToUnixTimeSeconds() / SecondsPerDay;

    public async Task<PB_ENUM_RES_ERROR_CODE> SetProtectCardSelectStatusAsync(ulong uid, int selectStatus)
    {
        if (selectStatus is not (UseProtectCard or NotUseProtectCard))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await UpdateAsync(uid, async state =>
        {
            await repository.SaveProtectCardSelectStatusAsync(uid, selectStatus);
            return (state with { ProtectCardSelectStatus = selectStatus }, true);
        });
        logger.LogInformation("Uid {Uid} set the rank point protection card to {SelectStatus}", uid, selectStatus == UseProtectCard ? "use" : "do not use");
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public RankedStartRefusal? RefuseStart(int mapId, int rankedTeamMemberNumType, LMNRoleInfo master, IReadOnlyList<LMNRoleInfo> members)
    {
        var others = members.Where(member => member.GameUlongUID != master.GameUlongUID).ToArray();
        if (!RankedMatch.IsRankedMatchUnlocked(gameData, notices, master))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchLevelLimit);
        }

        if (others.Any(member => !RankedMatch.IsRankedMatchUnlocked(gameData, notices, member)))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchTeamerLevelLimit);
        }

        var needPokemonNum = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalRankRaceOpenNeedPokemonNum);
        if (master.GetOwnedPokemonIds().Length < needPokemonNum)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchNotEnoughPokemon);
        }

        if (others.Any(member => member.GetOwnedPokemonIds().Length < needPokemonNum))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchTeamerNotEnoughPokemon);
        }

        if (rankedTeamMemberNumType is not (SingleRankedTeam or ThreeRankedTeam or FiveRankedTeam))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchInvalidTeamMemberNumType);
        }

        if (rankedTeamMemberNumType != FiveRankedTeam && members.Count > rankedTeamMemberNumType)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRoomRankPlayerMoreThanThree);
        }

        if (rankedTeamMemberNumType == FiveRankedTeam && members.Count < FiveRankedTeam)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRoomRankFiveNeedFivePeople);
        }

        if (NeedsSeasonRollover(master.GameUlongUID))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchNotInCurrentSeason);
        }

        if (others.Any(member => NeedsSeasonRollover(member.GameUlongUID)))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchSeasonNotEqual);
        }

        int[] bigs = [.. members.Select(member => BigOf(member.GameUlongUID))];
        if (bigs.Max() - bigs.Min() > PartyDivisionStep(rankedTeamMemberNumType))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRoomRankedDivisionDiffTooBig);
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var season = RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season;
        if (season.ForbidTime != 0
            && UTCTimeUtil.CfgTime2ServerUnixTime(season.ForbidTime) <= now
            && now <= UTCTimeUtil.CfgTime2ServerUnixTime(season.FinishTime))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchForbidTime);
        }

        if (members.FirstOrDefault(member => BanRankRaceEndTimeOf(member.GameUlongUID) > now) is { } banned)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchPlayerBan, new pbArgList
            {
                Args =
                {
                    new pbArgData { Value = new pbArgDataValue { StringValue = banned.InGameUsrName } },
                    new pbArgData
                    {
                        Value = new pbArgDataValue
                        {
                            TimestampValue = new pbArgTimestampValue { TimestampValue = BanRankRaceEndTimeOf(banned.GameUlongUID) },
                        },
                    },
                },
            });
        }

        var currentMapId = CurrentMapId(now);
        return mapId == currentMapId
            ? null
            : new(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchMapChange, new pbArgList
            {
                Args = { new pbArgData { Value = new pbArgDataValue { IntValue = currentMapId } } },
            });
    }

    public int PartyDivisionStep(int rankedTeamMemberNumType) => rankedTeamMemberNumType switch
    {
        FiveRankedTeam => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalFiveRankedDivisionNotDiffThan),
        ThreeRankedTeam => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalThreeRankedDivisionNotDiffThan),
        _ => int.MaxValue,
    };

    public int MatchDivisionBand(int rankedTeamMemberNumType) => gameData.GetGlobalIntData(rankedTeamMemberNumType == FiveRankedTeam
        ? PB_ENUM_RES_RESGLOBAL.PbResglobalRankMatachQuarantineDivisionFive
        : PB_ENUM_RES_RESGLOBAL.PbResglobalRankMatachQuarantineDivision);

    public int TeamMMRScore => Options.TeamMMRScore;

    public pbSCRoomMicTipsInfoNtf BuildMicTipsInfoNtf(ulong uid)
    {
        var session = sessions.GetValueOrDefault(uid, RankedSessionRecord.Empty);
        return new pbSCRoomMicTipsInfoNtf
        {
            BattleCnt = session.BattleCnt,
            ContinuousWin = session.ContinuousWin,
            ContinuousLose = session.ContinuousLose,
        };
    }

    public FakePlayerWinPointRange FakePlayerWinPoint => new(Options.FakePlayerWinPointMin, Options.FakePlayerWinPointMax);

    public int CurrentMapId(long now) =>
        gameData.RankedMatchMapOpenTime.Rows
            .Where(row => UTCTimeUtil.CfgTime2ServerUnixTime(row.OpenTime) <= now && now <= UTCTimeUtil.CfgTime2ServerUnixTime(row.CloseTime))
            .Select(row => row.MapID)
            .FirstOrDefault(gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalRankedMatchDefaultMap));

    public async Task ConsumeTicketsAsync(IEnumerable<ulong> uids)
    {
        foreach (var uid in uids)
        {
            await UpdateAsync(uid, async state =>
            {
                var season = state.Season;
                var position = season.Position;
                var ticket = season.UseFreeTicket ? NoTicketConsume : ladder.TicketConsumeOf(position.RankedDivision);
                var paid = ticket == NoTicketConsume ? position : ladder.Move(position, -ticket).Position;
                var saved = season with { Position = paid, Entry = new RankedEntry(position, ticket), UseFreeTicket = false };
                await repository.SaveSeasonAsync(uid, saved);
                logger.LogInformation(
                    "Uid {Uid} paid the ranked entry cost {TicketConsume} at division {RankedDivision}: win point {WinPoint} -> {PaidWinPoint}, master point {MasterPoint} -> {PaidMasterPoint}",
                    uid,
                    ticket,
                    position.RankedDivision,
                    position.WinPoint,
                    paid.WinPoint,
                    position.MasterPoint,
                    paid.MasterPoint);
                return (state with { Season = saved, SeasonStored = true }, true);
            });
        }
    }

    public async Task<FreeTicketCardUse> UseFreeTicketCardAsync(ulong uid, int resID, ulong gridUniqueID, int resCnt)
    {
        if (resCnt != OneFreeTicketCard)
        {
            return new FreeTicketCardUse(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum, [], null);
        }

        var (validation, config) = await bag.ValidateUseAsync(uid, resID, gridUniqueID, resCnt);
        if (config is null)
        {
            return new FreeTicketCardUse(validation, [], null);
        }

        if (config.ItemParam.Count <= ProtectCardSeasonParam || config.ItemParam[ProtectCardSeasonParam] != CurrentSeasonId)
        {
            return new FreeTicketCardUse(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemUnKnownUseType, [], null);
        }

        var result = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        IReadOnlyList<BagItem> changedItems = [];
        await UpdateAsync(uid, async state =>
        {
            var season = state.Season;
            if (season.UseFreeTicket)
            {
                result = PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemHasUsed;
                return (state, false);
            }

            if (!state.SeasonStored || ladder.TicketConsumeOf(season.Position.RankedDivision) == NoTicketConsume)
            {
                result = PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemFreeTicketZero;
                return (state, false);
            }

            var saved = season with { UseFreeTicket = true };
            var commit = await repository.CommitSeasonAsync(uid, saved, new BagCost(RoleItemType.OutSideItem, resID, OneFreeTicketCard), clock.GetUtcNow().ToUnixTimeSeconds());
            if (!commit.Committed)
            {
                result = PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemFreeTicketNotEnough;
                return (state, false);
            }

            changedItems = commit.ChangedItems;
            return (state with { Season = saved }, true);
        });
        return result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ? new FreeTicketCardUse(result, changedItems, new pbSCRankedMatchInfoNtf { Detail = await BuildDetailAsync(uid) })
            : new FreeTicketCardUse(result, [], null);
    }

    public async Task<RankedSettlement?> SettleAsync(ulong uid, RankedBattle battle)
    {
        RankedSettlement? settlement = null;
        await UpdateAsync(uid, async state =>
        {
            var season = state.Season;
            var old = season.Position;
            var ticket = season.Entry?.TicketConsume ?? NoTicketConsume;
            var big = ladder.BigOf((season.Entry?.Before ?? old).RankedDivision);
            var basePoint = battle.IsDraw ? 0
                : battle.Won ? Options.WinPointOf(big)
                : Options.LoseBasePointOf(big);
            var medals = ladder.RowOf((season.Entry?.Before ?? old).RankedDivision).MedalPoint > NoMedalPoint ? battle.Medals : [];
            var medalPoint = medals.Sum(medal => medal.Score);
            var battleAddSumPoint = basePoint + medalPoint;
            var move = ladder.Move(old, battleAddSumPoint);
            var entryFallProtect = season.Entry is { } entry && ladder.Move(entry.Before, -entry.TicketConsume).DivisionFallProtect;
            var record = Advance(season, move.Position, battle.Won);
            await repository.CommitSeasonAsync(uid, record, itemCost: null, clock.GetUtcNow().ToUnixTimeSeconds());

            var saved = state with { Season = record, SeasonStored = true };
            settlement = new RankedSettlement(
                new pbCOMDT_CATCH_SETTLE_RESULT
                {
                    OldRankedDivision = ToDivision(old, season.SeasonId),
                    NewRankedDivision = ToDivision(record.Position, season.SeasonId),
                    VirtualRegionRankInfo = new pbCOMDT_VIRTUAL_REGION_RANK_INFO(),
                    DivisionFallProtect = entryFallProtect ? FallProtected : 0,
                    BattlePointDetail = new pbCOMDT_SETTLE_POINT_DETAIL
                    {
                        TicketConsumePoint = ticket,
                        BattleAddSumPoint = battleAddSumPoint,
                        BasePoint = basePoint,
                        MedalPoint = medalPoint,
                        HangUpPercent = NotHungUpPercent,
                        MedalPointList = new pbCOMDT_SETTLE_MEDAlPOINT_LIST { MedalList = { medals.Select(medal => medal.Clone()) } },
                    },
                },
                new pbSCRankedMatchInfoNtf());
            logger.LogInformation(
                "Uid {Uid} ranked {Result}: division {OldDivision} ({OldWinPoint} wp, {OldMasterPoint} mp) -> {NewDivision} ({NewWinPoint} wp, {NewMasterPoint} mp); entry cost {TicketConsume}, base {BasePoint}, medals {MedalPoint}, fall protection {DivisionFallProtect}",
                uid,
                battle.IsDraw ? "draw" : battle.Won ? "win" : "loss",
                old.RankedDivision,
                old.WinPoint,
                old.MasterPoint,
                record.Position.RankedDivision,
                record.Position.WinPoint,
                record.Position.MasterPoint,
                ticket,
                basePoint,
                medalPoint,
                entryFallProtect);
            return (saved, true);
        });
        if (settlement is not null)
        {
            settlement.InfoNtf.Detail = await BuildDetailAsync(uid);
        }

        sessions.AddOrUpdate(uid, _ => RankedSessionRecord.Empty.After(battle.Won, battle.IsDraw), (_, session) => session.After(battle.Won, battle.IsDraw));

        return settlement;
    }

    public async Task<pbSCGetRankedMatchHistoryInfoRsp> BuildHistoryAsync(ulong uid)
    {
        var response = new pbSCGetRankedMatchHistoryInfoRsp { ResultInfo = new pbResultInfo() };
        var currentSeasonId = CurrentSeasonId;
        foreach (var season in await repository.ListSeasonsAsync(uid))
        {
            if (gameData.RankedMatchSeason.Find(season.SeasonId) is not { } row)
            {
                continue;
            }

            if (season.SeasonId == currentSeasonId)
            {
                response.RankedMatchHistoryInfoList.Add(new pbCOMDT_RANKED_MATCH_HISTORY_INFO { SeasonID = season.SeasonId });
                continue;
            }

            response.RankedMatchHistoryInfoList.Add(new pbCOMDT_RANKED_MATCH_HISTORY_INFO
            {
                SeasonID = season.SeasonId,
                RankedDivision = season.Position.RankedDivision,
                PlayerTotalBattleCnt = season.BattleCnt,
                PlayerTotalWinCnt = season.WinCnt,
                SeasonStartTime = UTCTimeUtil.CfgTime2ServerUnixTime(row.StartTime),
                SeasonEndTime = UTCTimeUtil.CfgTime2ServerUnixTime(row.FinishTime),
            });
        }

        return response;
    }

    public int ProtectCardItemID() => ProtectCardItemIDOf(RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season);

    private static int ProtectCardItemIDOf(pbRankedMatchSeason? season) =>
        season?.ProtectCardItemID is { } itemId and not NoProtectCardItemID ? itemId : DefaultProtectCardItemID;

    private long BanRankRaceEndTimeOf(ulong uid) =>
        states.TryGetValue(uid, out var state) ? state.BanRankRaceEndTime : NotBanned;

    private IEnumerable<pbRankedMatchSeasonTaskAward> SeasonTaskAwards(int seasonId) =>
        gameData.RankedMatchSeason.Find(seasonId) is { AwardGroupID: > NoAwardGroupID } season
            ? gameData.RankedMatchSeasonTaskAward.Rows.Where(row => row.AwardGroupId == season.AwardGroupID && rewards.HasDrops(row.SDropID))
            : [];

    private bool CanGainSeasonTaskAward(pbRankedMatchSeasonTaskAward row, RankedSeasonRecord season) =>
        row.BigRankedDivision >= ladder.MasterBig
            ? ladder.IndexOf(season.HighestRankedDivision) >= ladder.MasterIndex && season.HighestMasterPoint >= row.RankedDivision
            : !ladder.IsKnown(row.RankedDivision) || ladder.IndexOf(row.RankedDivision) <= ladder.IndexOf(season.HighestRankedDivision);

    private PB_ENUM_RES_ERROR_CODE? RefuseSeasonTaskAward(
        RankedState state,
        RankedSeasonRecord season,
        int rankedDivision,
        pbRankedMatchSeasonTaskAward[] requested)
    {
        if (state.PendingSeason is not null)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchNotInCurrentSeason;
        }

        if (requested.Length == 0)
        {
            return rankedDivision == RankedMatch.AllSeasonTaskAwards
                ? PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed
                : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        if (requested.Any(row => state.AwardedRankedDivisions.Contains(row.RankedDivision)))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked;
        }

        return requested.All(row => CanGainSeasonTaskAward(row, season)) ? null : PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed;
    }

    private async Task<IReadOnlyList<ExpiredProtectCard>> PlanExpiredProtectCardsAsync(ulong uid, int newSeasonId)
    {
        var currentCardItemID = ProtectCardItemIDOf(gameData.RankedMatchSeason.Find(newSeasonId));
        var expired = new List<ExpiredProtectCard>();
        foreach (var owned in await bag.GetOwnedOutSideItemsAsync(uid))
        {
            if (owned.ItemId == currentCardItemID
                || gameData.OutSideItemBase.Find(owned.ItemId) is not { } config
                || !SeasonBoundProtectCardTypes.Contains(config.OutSideItemType)
                || config.ItemParam.Count <= ProtectCardSeasonParam
                || config.ItemParam[ProtectCardSeasonParam] >= newSeasonId
                || !CoinID.WalletCoins.Contains(config.ExpireTransId)
                || config.ExpireTransNum <= 0)
            {
                continue;
            }

            var coinCnt = (long)owned.Cnt * config.ExpireTransNum / ExpireTransNumScale;
            if (coinCnt > 0)
            {
                expired.Add(new ExpiredProtectCard(owned.ItemId, owned.Cnt, config.ExpireTransId, coinCnt));
            }
        }

        return expired;
    }

    private async Task MailExpiredProtectCardsAsync(ulong uid, IReadOnlyList<ExpiredProtectCard> expiredCards)
    {
        foreach (var card in expiredCards)
        {
            await mail.SendSystemMailAsync(uid, new SystemMail(
                Subject: ItemExpiredMailSubject,
                Content: ItemExpiredMailContent,
                Attachments:
                [
                    new pbCOMDT_ONE_RESOURCE_AWARD_INFO
                    {
                        ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin,
                        ResID = card.CoinId,
                        ResCnt = (int)card.CoinCnt,
                    },
                ],
                MailID: ItemExpiredMailID,
                ContentParams:
                [
                    MailContentParam.Item(card.ItemId),
                    MailContentParam.Number(card.Cnt),
                    MailContentParam.Number(card.CoinCnt),
                    MailContentParam.Item(card.CoinId),
                ]));
            logger.LogInformation(
                "Uid {Uid} ranked season ended: protection card {ItemId}x{Cnt} expired and {CoinCnt} of coin {CoinId} mailed",
                uid,
                card.ItemId,
                card.Cnt,
                card.CoinCnt,
                card.CoinId);
        }
    }

    private static RankedGrant WithCosts(RankedGrant grant, IReadOnlyList<ExpiredProtectCard> expiredCards) => grant with
    {
        Change = grant.Change with
        {
            Costs = [.. grant.Change.Costs, .. expiredCards.Select(card => new BagCost(RoleItemType.OutSideItem, card.ItemId, card.Cnt))],
        },
    };

    private async Task MailUnclaimedSeasonTaskAwardsAsync(LMNRoleInfo role, RankedSeasonRecord old, long now)
    {
        var uid = role.GameUlongUID;
        if (gameData.RankedMatchSeason.Find(old.SeasonId) is not { } season)
        {
            return;
        }

        var awarded = (await repository.FindTaskAwardsAsync(uid, old.SeasonId)).ToHashSet();
        var unclaimed = SeasonTaskAwards(old.SeasonId)
            .Where(row => !awarded.Contains(row.RankedDivision) && CanGainSeasonTaskAward(row, old))
            .ToArray();
        var attachments = new List<pbCOMDT_ONE_RESOURCE_AWARD_INFO>();
        foreach (var row in unclaimed)
        {
            if (rewards.Plan(role, [row.SDropID], now) is not { } grant
                || grant.Awards.Any(award => !mail.CanAttach(award.FinallyAwardInfo)))
            {
                logger.LogWarning(
                    "Ranked season {SeasonId} task award {RankedDivision} (drop {SDropID}) for uid {Uid} cannot be mailed; not sent",
                    old.SeasonId,
                    row.RankedDivision,
                    row.SDropID,
                    uid);
                continue;
            }

            attachments.AddRange(grant.Awards.Select(award => award.FinallyAwardInfo.Clone()));
        }

        foreach (var chunk in attachments.Chunk(MailBox.MaxMailAccess))
        {
            await mail.SendSystemMailAsync(uid, new SystemMail(
                Subject: RankedUngetAwardMailSubject,
                Content: RankedUngetAwardMailContent,
                Attachments: chunk,
                MailID: RankedUngetAwardMailID,
                ContentParams: [MailContentParam.Text(season.ExplicitID)]));
        }

        if (attachments.Count > 0)
        {
            logger.LogInformation(
                "Uid {Uid} ranked season {SeasonId}: unclaimed task awards [{RankedDivisions}] mailed with {AttachmentCount} attachments",
                uid,
                old.SeasonId,
                string.Join(", ", unclaimed.Select(row => row.RankedDivision)),
                attachments.Count);
        }
    }

    private RankedGrant PlanSeasonAward(LMNRoleInfo role, RankedSeasonRecord old, long now)
    {
        if (gameData.RankedMatchSeason.Find(old.SeasonId) is not { SeasonAwardID: not NoSeasonAwardID } season
            || gameData.RankedMatchSeasonAward.Find(pbRankedMatchSeasonAward.Key(season.SeasonAwardID, ladder.BigOf(old.HighestRankedDivision))) is not { } award)
        {
            return RankedGrant.Empty;
        }

        if (rewards.Plan(role, [award.SDropID], now) is { } grant)
        {
            return grant;
        }

        logger.LogWarning(
            "Ranked season {SeasonId} award drop {SDropID} for uid {Uid} has a reward with no ownership path; nothing granted",
            old.SeasonId,
            award.SDropID,
            role.GameUlongUID);
        return RankedGrant.Empty;
    }

    private pbCSDT_RANKED_MATCH_DETAIL BuildDetail(
        RankedSeasonRecord season,
        PB_ENUM_COM_RANK_STATE_TYPE rankedMatchState,
        IReadOnlySet<int> awardedRankedDivisions,
        pbCOMDT_RANKED_DIVISION highestRankedDivisionInHistory) => new()
        {
            RankedMatchState = (int)rankedMatchState,
            CurrentRankedDivision = ToDivision(season.Position, season.SeasonId),
            HighestRankedDivisionInSeason = season.HighestRankedDivision,
            HighestMasterPointInSeason = season.HighestMasterPoint,
            AwardedInfo = new pbCOMDT_RANKED_MATCH_AWARDED_INFO { AwardedRankedDivisionList = { awardedRankedDivisions.Order() } },
            TicketConsume = ladder.TicketConsumeOf(season.Position.RankedDivision),
            IsUseFreeTicket = season.UseFreeTicket ? FreeTicketInUse : 0,
            HighestRankedDivisionInHistory = highestRankedDivisionInHistory,
        };

    private RankedSeasonRecord Advance(RankedSeasonRecord season, RankedPosition position, bool won)
    {
        var highest = season.HighestRankedDivision == RankedLadder.UnplacedRankedDivision
            || ladder.IndexOf(position.RankedDivision) > ladder.IndexOf(season.HighestRankedDivision)
            ? position.RankedDivision
            : season.HighestRankedDivision;
        return season with
        {
            Position = position,
            HighestRankedDivision = highest,
            HighestMasterPoint = Math.Max(season.HighestMasterPoint, position.MasterPoint),
            BattleCnt = season.BattleCnt + 1,
            WinCnt = season.WinCnt + (won ? 1 : 0),
            Entry = null,
        };
    }

    private RankedPosition InheritedPosition(RankedPosition old)
    {
        if (!Options.SeasonInheritRankedDivisionOrOfficial.TryGetValue(old.RankedDivision, out var inheritedDivision))
        {
            return old;
        }

        return ladder.IsMaster(inheritedDivision)
            ? ladder.StartOfBig(ladder.BigOf(inheritedDivision))
            : new RankedPosition(inheritedDivision, ladder.TicketConsumeOf(inheritedDivision), 0);
    }

    private RankedSeasonRecord StartingSeason(int seasonId) =>
        new(seasonId, RankedLadder.UnplacedPosition, RankedLadder.UnplacedRankedDivision, 0, 0, 0, Entry: null);

    public pbCOMDT_RANKED_DIVISION ToDivision(RankedPosition position, int seasonId)
    {
        var shown = ladder.ToClientPosition(position);
        return new pbCOMDT_RANKED_DIVISION
        {
            RankedDivision = shown.RankedDivision,
            WinPoint = shown.WinPoint,
            SeasonId = seasonId,
            MasterPoint = shown.MasterPoint,
        };
    }

    private async Task UpdateAsync(ulong uid, Func<RankedState, Task<(RankedState Saved, bool Changed)>> change)
    {
        var gate = gates.GetOrAdd(uid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var (saved, changed) = await change(await LoadAsync(uid));
            if (changed)
            {
                states[uid] = saved;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
