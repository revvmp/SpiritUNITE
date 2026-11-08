using Google.Protobuf;
using SpiritUnite.Databin;
using SpiritUnite.Game.Room;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

public enum BanPickTickResult
{
    Running,
    Finished,
    NoHeroLeft,
}

public enum BanPickPhase
{
    Created,
    Round,
    Interval,
    Adjusting,
    Finished,
    Failed,
}

public sealed record BanPickTurn(BattleDesk Desk, DeskPlayer Player, BanPickDraft Draft, BanPickSeatActions Actions, long NowMs);

public sealed record BanPickSeatActions(
    Func<DeskPlayer, IReadOnlySet<int>, int> PickHeroFor,
    Action<DeskPlayer, int, RoomOutcome> Choose);

public sealed record BanPickTimers(
    int TipTimeMs,
    int AiChoiceHeroTimeMs,
    int SwapPokemonTimeoutMs,
    int PickToPickIntervalMs,
    int PickToAdjustIntervalMs,
    int BanToBanIntervalMs,
    int AiBanHeroTimeMs,
    int AiBanFirstHeroTimeMs,
    int AdjustTimeMs)
{
    private const int MillisecondsPerSecond = 1000;

    public static BanPickTimers From(GameData gameData, pbScene_Map scene) => new(
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalBanpickTipTime),
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalBanpickAiChoiceHeroTime) * MillisecondsPerSecond,
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalBanpickSwapPokemonTimeout) * MillisecondsPerSecond,
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalBanpickPickToPickIntervalTime),
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalBanpickPickToAdjustIntervalTime),
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalBanpickBanToBanIntervalTime),
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalBanpickAiBanHeroTime) * MillisecondsPerSecond,
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalBanpickAiBanFirstHeroTime) * MillisecondsPerSecond,
        scene.PickAdjustSec * MillisecondsPerSecond);

    public int SwapPokemonCfgTime => SwapPokemonTimeoutMs / MillisecondsPerSecond;

    public int AdjustLeftSec => AdjustTimeMs / MillisecondsPerSecond;
}

public sealed class BanPickDraft
{
    public const int PickHeroMode = (int)PB_ENUM_RES_PICK_HERO_MODE.PbResPickHeroModeBanpick;

    private const int BanType = (int)PB_ENUM_RES_BAN_PICK_TYPE.PbResBanPickTypeBan;
    private const int PickType = (int)PB_ENUM_RES_BAN_PICK_TYPE.PbResBanPickTypePick;
    private const int AdjustType = (int)PB_ENUM_RES_BAN_PICK_TYPE.PbResBanPickTypeAdjust;
    private const int AppointBanMode = (int)PB_ENUM_RES_BAN_PICK_BAN_TYPE.PbResBanPickBanTypeAppoint;
    private const int CampOne = MatchRoom.LeftCamp;
    private const int CampTwo = MatchRoom.RightCamp;
    private const int NoSeat = 0;
    private const int NoHeroID = 0;
    private const int BanStageResult = 1;
    private const int BanLocked = 1;
    private const int Confirmed = 1;

    private readonly pbBanPickRule rule;
    private readonly IReadOnlyList<pbRoundRule> rounds;
    private readonly BanPickTimers timers;
    private readonly int[] heroList;
    private readonly IReadOnlySet<int> globalBans;
    private readonly Dictionary<int, List<int>> bansByCamp = new() { [CampOne] = [], [CampTwo] = [] };
    private readonly Dictionary<int, List<pbCOMDT_VOTE_HERO_INFO>> votesByCamp = new() { [CampOne] = [], [CampTwo] = [] };
    private readonly Dictionary<ulong, long> npcActsAtMs = [];
    private int roundIndex;
    private long stageEndsAtMs;
    private PendingSwap? pendingSwap;

    private BanPickDraft(pbBanPickRule rule, IReadOnlyList<pbRoundRule> rounds, BanPickTimers timers, int[] heroList, IReadOnlySet<int> globalBans)
    {
        this.rule = rule;
        this.rounds = rounds;
        this.timers = timers;
        this.heroList = heroList;
        this.globalBans = globalBans;
    }

    public int PickRuleID => rule.BanPickRuleId;

    public BanPickPhase Phase { get; private set; } = BanPickPhase.Created;

    public int FirstPickCamp => rounds.FirstOrDefault(round => round.BanOrPick == PickType) is { } firstPick && !SeatsOf(firstPick, CampOne).Any()
        ? CampTwo
        : CampOne;

    public static BanPickDraft? Create(GameData gameData, int pickRuleID, BanPickTimers timers, IEnumerable<int> heroList, IEnumerable<int> globalBanHeroIDs)
    {
        if (gameData.BanPickRule.Find(pickRuleID) is not { } rule)
        {
            return null;
        }

        pbRoundRule[] rounds = [.. rule.Round.Where(roundID => roundID != 0).Select(roundID => gameData.RoundRule.Find(roundID)).OfType<pbRoundRule>()];
        var globalBans = globalBanHeroIDs.ToHashSet();
        return rounds.Length == 0
            ? null
            : new BanPickDraft(rule, rounds, timers, [.. heroList.Where(heroID => !globalBans.Contains(heroID)).Distinct().Order()], globalBans);
    }

    public void Begin(BattleDesk desk, long nowMs)
    {
        if (Phase != BanPickPhase.Created)
        {
            throw new InvalidOperationException("The draft has already begun");
        }

        roundIndex = 0;
        StartRound(desk, nowMs);
    }

    public pbCOMDT_BAN_PICK_INFO BuildBanPickInfo() => new()
    {
        BanPickHeroInfo = new pbCOMDT_HERO_INFO { HeroList = { heroList } },
        BanHeroInfo = BuildBanHeroInfo(),
        CurState = BuildState(CurrentRound),
        NextState = BuildState(NextRound),
        SwapPokemonCfgTime = timers.SwapPokemonCfgTime,
        PickRuleID = PickRuleID,
        FirstPickCamp = FirstPickCamp,
    };

    public bool IsBanned(int heroID) => globalBans.Contains(heroID) || bansByCamp.Values.Any(bans => bans.Contains(heroID));

    public bool IsInTurn(DeskPlayer player) => Phase == BanPickPhase.Round && CurrentRound is { } round && IsSeatOf(round, player);

    public bool IsPickTurn(DeskPlayer player) => IsInTurn(player) && CurrentRound!.BanOrPick == PickType;

    public PB_ENUM_RES_ERROR_CODE? RefuseChoose(BattleDesk desk, DeskPlayer player, int heroID)
    {
        if (!IsPickTurn(player))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrDeskStateInvalid;
        }

        if (IsBanned(heroID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrBanPickHeroAlreadyBan;
        }

        return desk.Players.Any(other => other != player && other.IsHeroLocked && other.ChosenHeroID == heroID)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCannotChooseHero
            : null;
    }

    public RoomOutcome VoteBan(BattleDesk desk, DeskPlayer player, int heroID, bool lockBan)
    {
        var refusal = !IsInTurn(player) || CurrentRound!.BanOrPick != BanType ? PB_ENUM_RES_ERROR_CODE.PbErrDeskStateInvalid
            : player.ChoiceHero.IsBanHeroLocked == BanLocked ? PB_ENUM_RES_ERROR_CODE.PbErrBanPickHeroAlreadyBan
            : !heroList.Contains(heroID) ? PB_ENUM_RES_ERROR_CODE.PbErrCannotChooseHero
            : IsBanned(heroID) ? PB_ENUM_RES_ERROR_CODE.PbErrBanPickHeroAlreadyBan
            : (PB_ENUM_RES_ERROR_CODE?)null;
        var rspID = lockBan ? PB_ENUM_CS_MSGID_DEF.PbScidLockBanHeroRsp : PB_ENUM_CS_MSGID_DEF.PbScidVoteBanHeroRsp;
        IMessage Rsp(PB_ENUM_RES_ERROR_CODE result) => lockBan
            ? new pbSCLockBanHeroRsp { ResultInfo = new pbResultInfo { ResultId = (int)result }, HeroId = heroID }
            : new pbSCVoteBanHeroRsp { ResultInfo = new pbResultInfo { ResultId = (int)result }, HeroId = heroID };
        if (refusal is { } code)
        {
            return new RoomOutcome().Push(player.Uid, rspID, Rsp(code));
        }

        var outcome = new RoomOutcome();
        CastVote(desk, player, heroID, lockBan, outcome);
        return outcome.Push(player.Uid, rspID, Rsp(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc));
    }

    public RoomOutcome ExpectBan(BattleDesk desk, DeskPlayer player, int heroID, long nowMs)
    {
        var inBanStage = Phase == BanPickPhase.Round && CurrentRound?.BanOrPick == BanType && desk.CampOf(player.CampClient).Any(IsInTurn);
        if (!inBanStage || (heroID != NoHeroID && !heroList.Contains(heroID)))
        {
            return new RoomOutcome().Push(player.Uid, PB_ENUM_CS_MSGID_DEF.PbScidExpectBanHeroRsp, new pbSCExpectBanHeroRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrBanPickSelfStageNotExpectBan },
                HeroId = heroID,
            });
        }

        player.ChoiceHero.BanPickExpectBanHeroId = heroID;
        player.ChoiceHero.BanPickExpectCDTime = heroID == NoHeroID || rule.ExceptBanCD == 0 ? 0 : nowMs + rule.ExceptBanCD;
        return new RoomOutcome()
            .PushAll(desk.HumanUidsOf(player.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, ChgNtf(player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroExpectBanChg))
            .Push(player.Uid, PB_ENUM_CS_MSGID_DEF.PbScidExpectBanHeroRsp, new pbSCExpectBanHeroRsp { ResultInfo = new pbResultInfo(), HeroId = heroID });
    }

    public RoomOutcome PreChoose(BattleDesk desk, DeskPlayer player, int heroID)
    {
        var refusal = Phase != BanPickPhase.Round || CurrentRound?.BanOrPick != PickType || player.IsHeroLocked ? PB_ENUM_RES_ERROR_CODE.PbErrDeskStateInvalid
            : heroID != NoHeroID && !player.UsableHeroes.ContainsKey(heroID) ? PB_ENUM_RES_ERROR_CODE.PbErrCannotChooseHero
            : heroID != NoHeroID && IsBanned(heroID) ? PB_ENUM_RES_ERROR_CODE.PbErrBanPickHeroAlreadyBan
            : (PB_ENUM_RES_ERROR_CODE?)null;
        if (refusal is { } code)
        {
            return new RoomOutcome().Push(player.Uid, PB_ENUM_CS_MSGID_DEF.PbScidPreChooseHeroRsp, new pbSCPreChooseHeroRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)code },
                HeroId = heroID,
            });
        }

        player.ChoiceHero.BanPickPreHeroId = heroID;
        return new RoomOutcome()
            .PushAll(desk.HumanUidsOf(player.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, ChgNtf(player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroPreChooseChg))
            .Push(player.Uid, PB_ENUM_CS_MSGID_DEF.PbScidPreChooseHeroRsp, new pbSCPreChooseHeroRsp { ResultInfo = new pbResultInfo(), HeroId = heroID });
    }

    public RoomOutcome HelpChoose(BattleDesk desk, DeskPlayer player, int heroID)
    {
        var refusal = Phase != BanPickPhase.Round || CurrentRound?.BanOrPick != PickType || IsInTurn(player) || player.IsHeroLocked
                || !desk.CampOf(player.CampClient).Any(IsInTurn)
            ? PB_ENUM_RES_ERROR_CODE.PbErrBanPickSelfStageNotHelpChoose
            : heroID != NoHeroID && (!player.UsableHeroes.ContainsKey(heroID) || IsBanned(heroID)) ? PB_ENUM_RES_ERROR_CODE.PbErrCannotChooseHero
            : (PB_ENUM_RES_ERROR_CODE?)null;
        if (refusal is { } code)
        {
            return new RoomOutcome().Push(player.Uid, PB_ENUM_CS_MSGID_DEF.PbScidHelpChooseHeroRsp, new pbSCHelpChooseHeroRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)code },
                HeroId = heroID,
            });
        }

        player.ChoiceHero.BanPickHelpChooseHeroId = heroID;
        return new RoomOutcome()
            .PushAll(desk.HumanUidsOf(player.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, ChgNtf(player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroHelpChooseChg))
            .Push(player.Uid, PB_ENUM_CS_MSGID_DEF.PbScidHelpChooseHeroRsp, new pbSCHelpChooseHeroRsp { ResultInfo = new pbResultInfo(), HeroId = heroID });
    }

    public RoomOutcome ConfirmHelpChoose(BattleDesk desk, DeskPlayer helper, ulong needHelpUid, BanPickSeatActions actions)
    {
        var needHelp = desk.FindHuman(needHelpUid);
        var helpHeroID = needHelp?.ChoiceHero.BanPickHelpChooseHeroId ?? NoHeroID;
        var valid = needHelp is not null
            && needHelp != helper
            && needHelp.CampClient == helper.CampClient
            && IsPickTurn(helper)
            && !helper.IsHeroLocked
            && !needHelp.IsHeroLocked
            && helpHeroID != NoHeroID
            && RefuseChoose(desk, helper, helpHeroID) is null;
        if (!valid)
        {
            return new RoomOutcome().Push(helper.Uid, PB_ENUM_CS_MSGID_DEF.PbScidConfirmHelpChooseHeroRsp, new pbSCConfirmHelpChooseHeroRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrBanPickSelfStageNotHelpChoose },
            });
        }

        var outcome = new RoomOutcome();
        (helper.PlayerInfo.PosOfCamp, needHelp!.PlayerInfo.PosOfCamp) = (needHelp.PosOfCamp, helper.PosOfCamp);
        outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidSwapPosOfCampNtf, new pbSCSwapPosOfCampNtf { HelpUid = helper.Uid, NeedHelpUid = needHelp.Uid });
        helper.ChoiceHero = new pbCOMDT_CHOICEHERO();
        outcome.PushAll(desk.HumanUidsOf(helper.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, ChgNtf(helper, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroConfirmHelpChooseChg));
        actions.Choose(needHelp, helpHeroID, outcome);
        needHelp.ChoiceHero.BanPickHelpChooseHeroId = NoHeroID;
        outcome.PushAll(desk.HumanUidsOf(needHelp.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, ChgNtf(needHelp, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroConfirmHelpChooseChg));
        return outcome.Push(helper.Uid, PB_ENUM_CS_MSGID_DEF.PbScidConfirmHelpChooseHeroRsp, new pbSCConfirmHelpChooseHeroRsp { ResultInfo = new pbResultInfo() });
    }

    public RoomOutcome RequestSwap(BattleDesk desk, DeskPlayer source, ulong dstUid, long nowMs)
    {
        var target = desk.FindHuman(dstUid);
        var refusal = Phase != BanPickPhase.Adjusting ? PB_ENUM_RES_ERROR_CODE.PbErrBanPickPokemonSwapInvalid
            : target is null || target == source || target.CampClient != source.CampClient ? PB_ENUM_RES_ERROR_CODE.PbErrBanPickPokemonSwapNoObject
            : pendingSwap is not null ? PB_ENUM_RES_ERROR_CODE.PbErrBanPickPokemonSwapAlready
            : !CanSwap(source, target) ? PB_ENUM_RES_ERROR_CODE.PbErrBanPickPokemonSwapErr
            : (PB_ENUM_RES_ERROR_CODE?)null;
        var outcome = new RoomOutcome();
        if (refusal is null)
        {
            pendingSwap = new PendingSwap(source.Uid, target!.Uid, nowMs + timers.SwapPokemonTimeoutMs);
            outcome.Push(target.Uid, PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonNtf, new pbSCBanPickSwapPokemonNtf { SrcUid = source.Uid });
        }

        return outcome.Push(source.Uid, PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonRsp, new pbSCBanPickSwapPokemonRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)(refusal ?? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc) },
        });
    }

    public RoomOutcome ConfirmSwap(BattleDesk desk, DeskPlayer target, bool isConfirm)
    {
        var outcome = new RoomOutcome();
        if (Phase != BanPickPhase.Adjusting || pendingSwap is not { } swap || swap.DstUid != target.Uid || desk.FindHuman(swap.SrcUid) is not { } source)
        {
            return outcome.Push(target.Uid, PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonConfirmRsp, new pbSCBanPickSwapPokemonConfirmRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrBanPickPokemonSwapTimeout },
                IsConfirm = isConfirm ? Confirmed : 0,
            });
        }

        pendingSwap = null;
        if (isConfirm && CanSwap(source, target))
        {
            var sourceHeroID = source.ChosenHeroID;
            source.ChoiceHero = Locked(source.UsableHeroes[target.ChosenHeroID]);
            target.ChoiceHero = Locked(target.UsableHeroes[sourceHeroID]);
            foreach (var swapped in new[] { source, target })
            {
                outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, ChgNtf(swapped, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroSwapChg));
            }
        }

        return outcome
            .Push(source.Uid, PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonConfirmNtf, new pbSCBanPickSwapPokemonConfirmNtf { DstUid = target.Uid, IsConfirm = isConfirm ? Confirmed : 0 })
            .Push(target.Uid, PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonConfirmRsp, new pbSCBanPickSwapPokemonConfirmRsp { ResultInfo = new pbResultInfo(), IsConfirm = isConfirm ? Confirmed : 0 });
    }

    public RoomOutcome CancelSwap(DeskPlayer source)
    {
        var outcome = new RoomOutcome();
        if (pendingSwap is { } swap && swap.SrcUid == source.Uid)
        {
            pendingSwap = null;
            outcome.Push(swap.DstUid, PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonCancelNtf, new pbSCBanPickSwapPokemonCancelNtf());
        }

        return outcome.Push(source.Uid, PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonCancelRsp, new pbSCBanPickSwapPokemonCancelRsp { ResultInfo = new pbResultInfo() });
    }

    public BanPickTickResult Tick(BattleDesk desk, long nowMs, BanPickSeatActions actions, RoomOutcome outcome)
    {
        if (Phase == BanPickPhase.Created)
        {
            throw new InvalidOperationException("The draft has not begun");
        }

        if (Phase == BanPickPhase.Finished)
        {
            return BanPickTickResult.Finished;
        }

        if (Phase == BanPickPhase.Failed)
        {
            return BanPickTickResult.NoHeroLeft;
        }

        if (Phase == BanPickPhase.Adjusting)
        {
            if (pendingSwap is { } swap && (swap.ExpiresAtMs <= nowMs || nowMs >= stageEndsAtMs))
            {
                pendingSwap = null;
                outcome.Push(swap.DstUid, PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonCancelNtf, new pbSCBanPickSwapPokemonCancelNtf());
            }

            if (nowMs < stageEndsAtMs)
            {
                return BanPickTickResult.Running;
            }

            Phase = BanPickPhase.Finished;
            return BanPickTickResult.Finished;
        }

        if (Phase == BanPickPhase.Interval)
        {
            if (nowMs < stageEndsAtMs)
            {
                return BanPickTickResult.Running;
            }

            if (CurrentRound is null)
            {
                BeginAdjust(desk, nowMs, outcome);
                return BanPickTickResult.Running;
            }

            StartRound(desk, nowMs);
            outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidCurBanPickInfoNtf, new pbSCCurBanPickInfoNtf
            {
                CurState = BuildState(CurrentRound),
                NextState = BuildState(NextRound),
            });
        }

        var round = CurrentRound!;
        if (ActNpcs(desk, round, nowMs, actions, outcome) is BanPickTickResult.NoHeroLeft)
        {
            Phase = BanPickPhase.Failed;
            return BanPickTickResult.NoHeroLeft;
        }

        DeskPlayer[] seats = [.. desk.Players.Where(player => IsSeatOf(round, player))];
        var done = round.BanOrPick == BanType
            ? seats.All(seat => seat.ChoiceHero.IsBanHeroLocked == BanLocked)
            : seats.All(seat => seat.IsHeroLocked);
        if (!done && nowMs < stageEndsAtMs)
        {
            return BanPickTickResult.Running;
        }

        var result = round.BanOrPick == BanType ? EndBanRound(desk, seats, nowMs, outcome) : EndPickRound(desk, seats, nowMs, actions, outcome);
        if (result == BanPickTickResult.NoHeroLeft)
        {
            Phase = BanPickPhase.Failed;
        }

        return result;
    }

    private pbRoundRule? CurrentRound => rounds.ElementAtOrDefault(roundIndex);

    private pbRoundRule? NextRound => rounds.ElementAtOrDefault(roundIndex + 1);

    private void StartRound(BattleDesk desk, long nowMs)
    {
        var round = CurrentRound!;
        Phase = BanPickPhase.Round;
        stageEndsAtMs = nowMs + round.TimeOut;
        npcActsAtMs.Clear();
        foreach (var campSeats in desk.Players.Where(player => player.IsNpc && IsSeatOf(round, player)).GroupBy(player => player.CampClient))
        {
            var order = 0;
            foreach (var npc in campSeats.OrderBy(player => player.PosOfCamp))
            {
                npcActsAtMs[npc.Uid] = round.BanOrPick == BanType
                    ? nowMs + timers.AiBanFirstHeroTimeMs + (order++ * timers.AiBanHeroTimeMs)
                    : nowMs + timers.AiChoiceHeroTimeMs;
            }
        }
    }

    private BanPickTickResult ActNpcs(BattleDesk desk, pbRoundRule round, long nowMs, BanPickSeatActions actions, RoomOutcome outcome)
    {
        foreach (var (npcUid, actsAtMs) in npcActsAtMs.Where(entry => entry.Value <= nowMs).ToArray())
        {
            npcActsAtMs.Remove(npcUid);
            var npc = desk.Players.First(player => player.IsNpc && player.Uid == npcUid);
            if (round.BanOrPick == BanType)
            {
                var candidates = heroList.Where(heroID => !IsBanned(heroID)).ToArray();
                if (candidates.Length > 0)
                {
                    CastVote(desk, npc, candidates[Random.Shared.Next(candidates.Length)], lockBan: true, outcome);
                }
            }
            else if (!PickAndLock(desk, npc, actions, outcome))
            {
                return BanPickTickResult.NoHeroLeft;
            }
        }

        return BanPickTickResult.Running;
    }

    private void CastVote(BattleDesk desk, DeskPlayer player, int heroID, bool lockBan, RoomOutcome outcome)
    {
        player.ChoiceHero.BanPickVoteBanHeroId = heroID;
        if (lockBan)
        {
            player.ChoiceHero.IsBanHeroLocked = BanLocked;
        }

        outcome.PushAll(
            desk.HumanUidsOf(player.CampClient),
            PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf,
            ChgNtf(player, lockBan ? PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroLockBanHero : PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroVoteBanChg));
    }

    private BanPickTickResult EndBanRound(BattleDesk desk, DeskPlayer[] seats, long nowMs, RoomOutcome outcome)
    {
        foreach (var camp in new[] { CampOne, CampTwo })
        {
            var votes = seats
                .Where(seat => seat.CampClient == camp && seat.ChoiceHero.BanPickVoteBanHeroId != NoHeroID)
                .GroupBy(seat => seat.ChoiceHero.BanPickVoteBanHeroId)
                .Select(group => new pbCOMDT_VOTE_HERO_INFO { HeroID = group.Key, VoteCnt = group.Count() })
                .OrderByDescending(vote => vote.VoteCnt)
                .ThenBy(_ => Random.Shared.Next())
                .ToArray();
            votesByCamp[camp].AddRange(votes);
            var banNum = rule.BanMode == AppointBanMode ? votes.Length : rule.VoteCampBanNum;
            bansByCamp[camp].AddRange(votes.Take(banNum).Select(vote => vote.HeroID).Where(heroID => !bansByCamp[camp].Contains(heroID)));
        }

        foreach (var player in desk.Players)
        {
            player.ChoiceHero.BanPickVoteBanHeroId = NoHeroID;
            player.ChoiceHero.IsBanHeroLocked = 0;
            player.ChoiceHero.BanPickExpectBanHeroId = NoHeroID;
            player.ChoiceHero.BanPickExpectCDTime = 0;
        }

        outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidBanHeroInfoNtf, new pbSCBanHeroInfoNtf
        {
            BanHeroInfo = BuildBanHeroInfo(),
            BanStage = BanStageResult,
        });
        var nextIsBan = NextRound?.BanOrPick == BanType;
        return EndRound(nowMs, nextIsBan ? timers.BanToBanIntervalMs : timers.TipTimeMs);
    }

    private BanPickTickResult EndPickRound(BattleDesk desk, DeskPlayer[] seats, long nowMs, BanPickSeatActions actions, RoomOutcome outcome)
    {
        foreach (var seat in seats.Where(seat => !seat.IsHeroLocked))
        {
            if (!PickAndLock(desk, seat, actions, outcome))
            {
                return BanPickTickResult.NoHeroLeft;
            }
        }

        return EndRound(nowMs, NextRound is null ? timers.PickToAdjustIntervalMs : timers.PickToPickIntervalMs);
    }

    private BanPickTickResult EndRound(long nowMs, int intervalMs)
    {
        Phase = BanPickPhase.Interval;
        roundIndex++;
        stageEndsAtMs = nowMs + intervalMs;
        return BanPickTickResult.Running;
    }

    private void BeginAdjust(BattleDesk desk, long nowMs, RoomOutcome outcome)
    {
        Phase = BanPickPhase.Adjusting;
        stageEndsAtMs = nowMs + timers.AdjustTimeMs;
        outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidMultGameBeginAdjustNtf, new pbSCMultGameBeginAdjustNtf { LeftSec = timers.AdjustLeftSec });
    }

    private bool PickAndLock(BattleDesk desk, DeskPlayer player, BanPickSeatActions actions, RoomOutcome outcome)
    {
        var excluded = desk.Players
            .Where(other => other != player && (other.IsHeroLocked || other.CampClient == player.CampClient) && other.ChosenHeroID != NoHeroID)
            .Select(other => other.ChosenHeroID)
            .Concat(bansByCamp.Values.SelectMany(bans => bans))
            .Concat(globalBans)
            .ToHashSet();
        if (player.ChosenHeroID == NoHeroID || excluded.Contains(player.ChosenHeroID))
        {
            var heroID = actions.PickHeroFor(player, excluded);
            if (heroID == NoHeroID)
            {
                return false;
            }

            actions.Choose(player, heroID, outcome);
        }

        player.ChoiceHero.IsHeroLocked = DeskPlayer.HeroLocked;
        outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, ChgNtf(player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroLockHero));
        return true;
    }

    private static bool CanSwap(DeskPlayer source, DeskPlayer target) =>
        source.IsHeroLocked
        && target.IsHeroLocked
        && source.UsableHeroes.ContainsKey(target.ChosenHeroID)
        && target.UsableHeroes.ContainsKey(source.ChosenHeroID);

    private static pbCOMDT_CHOICEHERO Locked(pbCOMDT_CHOICEHERO template)
    {
        var choice = template.Clone();
        choice.IsHeroLocked = DeskPlayer.HeroLocked;
        return choice;
    }

    private pbCOMDT_BAN_HERO_INFO BuildBanHeroInfo() => new()
    {
        BanCampInfo =
        {
            new[] { CampOne, CampTwo }.Select(camp => new pbCOMDT_BAN_CAMP_INFO
            {
                Camp = camp,
                BanHeroList = { bansByCamp[camp] },
                VoteHeroList = { votesByCamp[camp].Select(vote => vote.Clone()) },
            }),
        },
    };

    private pbCOMDT_BAN_PICK_STATE_INFO BuildState(pbRoundRule? round) => round is null
        ? new pbCOMDT_BAN_PICK_STATE_INFO { BanPickType = AdjustType, TimeOut = timers.AdjustTimeMs }
        : new pbCOMDT_BAN_PICK_STATE_INFO
        {
            BanPickType = round.BanOrPick,
            TimeOut = round.TimeOut,
            BanPickCampInfo =
            {
                new[] { CampOne, CampTwo }.Select(camp => new pbCOMDT_BAN_PICK_STATE_CAMP_INFO
                {
                    Camp = camp,
                    PosList = { SeatsOf(round, camp) },
                }),
            },
        };

    private static IEnumerable<int> SeatsOf(pbRoundRule round, int camp) =>
        (camp == CampOne ? round.CampOnePos : round.CampTwoPos).Where(seat => seat != NoSeat).Select(seat => seat - 1);

    private static bool IsSeatOf(pbRoundRule round, DeskPlayer player) =>
        player.CampClient is CampOne or CampTwo && SeatsOf(round, player.CampClient).Contains(player.PosOfCamp);

    private static pbSCChoiceHeroChgNtf ChgNtf(DeskPlayer player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON chgReason) =>
        DeskManager.BuildChoiceHeroChgNtf(player, chgReason);

    private sealed record PendingSwap(ulong SrcUid, ulong DstUid, long ExpiresAtMs);
}
