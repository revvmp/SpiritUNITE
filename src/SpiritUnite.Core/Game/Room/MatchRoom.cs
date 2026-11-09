using SpiritUnite.Game.Match;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Room;

public sealed class RoomMember(
    ulong uid,
    int campClient,
    int posOfCamp,
    int teamFlag,
    pbCOMDT_ACNTMAP_INFO acntMapInfo,
    RoomMemberProfile profile)
{
    public ulong Uid { get; } = uid;
    public int TeamFlag { get; } = teamFlag;
    public bool IsSettle { get; set; }
    public bool SettlesAlone { get; set; }
    public bool IsInGvoiceRoom { get; set; }
    public int CampClient { get; set; } = campClient;
    public int PosOfCamp { get; set; } = posOfCamp;
    public pbCOMDT_ACNTMAP_INFO AcntMapInfo { get; set; } = acntMapInfo;
    public RoomMemberProfile Profile { get; set; } = profile;

    public IReadOnlySet<int> UsableMaps =>
        AcntMapInfo.MapList.Where(map => map.CanUseType == RoomOptions.CanUseMap).Select(map => map.MapId).ToHashSet();
}

public sealed record RoomBot(ulong Uid, int CampClient, int PosOfCamp, pbAiInfoConfig Row);

public sealed record RoomPosChange(int ChgSeq, ulong SenderUid, ulong ReceiverUid, long ExpiresAt);

public enum RoomMatchPhase
{
    Waiting,
    Checking,
    Requesting,
    Searching,
    Confirming,
    Started,
}

public sealed class MatchRoom(int roomID, long roomSeq, int roomShowId, ulong masterUid)
{
    public const int RoomEntity = 193986824;
    public const int MatchRoomEntity = 199229704;
    public const int LeftCamp = (int)PB_ENUM_COM_CAMP_CLIENT.PbCampClientPlayer1;
    public const int RightCamp = (int)PB_ENUM_COM_CAMP_CLIENT.PbCampClientPlayer2;
    public const int JudgeCamp = (int)PB_ENUM_COM_CAMP_CLIENT.PbCampClientJudge1;
    public const int JudgeCampCapacity = (int)PB_MACRO_COM_MAX_JUDGE_NUM.PbComMaxJudgeNum;

    private const int FirstTeamFlag = 1;
    private const int NoReservedInOrderUids = 0;
    private const int GameEntityTypeBits = 12;
    private const int LobbyGameEntityType = 0x008;
    private const int FirstGameEntityInstance = 1;
    private const int GameEntityInstanceCount = 1 << (31 - GameEntityTypeBits);

    public static readonly int GameEntity = BuildLobbyGameEntity();

    private readonly List<RoomMember> members = [];
    private readonly List<RoomBot> bots = [];

    public int RoomID { get; } = roomID;
    public long RoomSeq { get; } = roomSeq;
    public int RoomShowId { get; } = roomShowId;
    public ulong MasterUid { get; set; } = masterUid;
    public PB_ENUM_COM_ROOM_TYPE RoomType { get; set; } = PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch;
    public int MapMode { get; set; }
    public int MapSubMode { get; set; }
    public int MapId { get; set; }
    public int CampPlayerNum { get; set; }
    public int ManMachine { get; set; }
    public int RankedTeamMemberNumType { get; set; }
    public int MatchTrainer { get; set; }
    public int TrainerDifficultyLevel { get; set; }
    public int HeavenDifficultyLevel { get; set; }
    public int SelectIdcTag { get; set; }
    public int LastRankedTeamMemberNumType { get; init; }
    public RoomMatchPhase MatchPhase { get; private set; } = RoomMatchPhase.Waiting;
    public PB_ENUM_COM_ROOMSVR_ROOM_STATE State => MatchPhase switch
    {
        RoomMatchPhase.Waiting => PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateWait,
        RoomMatchPhase.Checking => PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateChk,
        RoomMatchPhase.Requesting => PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateReq,
        RoomMatchPhase.Searching or RoomMatchPhase.Confirming or RoomMatchPhase.Started => PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateProc,
        _ => throw new InvalidOperationException($"Unknown room matchmaking phase {MatchPhase}"),
    };
    public long MatchStartedAt { get; private set; }
    public RoomPosChange? PendingPosChange { get; set; }
    public int NextChgSeq { get; set; } = 1;
    public pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE? PveChallengeSelectNode { get; set; }

    public pbCOMDT_TEN_GAMES_TEAM_BRIEF_INFO? TenGamesTeamInfo { get; set; }
    public PendingMatch? Match { get; private set; }
    public FriendlyRoomRules FriendRoomRule { get; set; } = FriendlyRoomRules.None;
    public IReadOnlyList<int> GlobalBanPokemonList { get; set; } = [];
    public DateTimeOffset GlobalBanPokemonListChangedAt { get; set; } = DateTimeOffset.MinValue;

    public IReadOnlyList<RoomMember> Members => members;

    public IReadOnlyList<RoomBot> Bots => bots;

    public bool IsCustomRoom => RoomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeNormal;

    public bool IsMatching => MatchPhase is RoomMatchPhase.Requesting or RoomMatchPhase.Searching or RoomMatchPhase.Confirming;

    public bool IsSearching => MatchPhase == RoomMatchPhase.Searching;

    public bool IsConfirming => MatchPhase == RoomMatchPhase.Confirming;

    public void BeginMatchCheck() => TransitionMatch(RoomMatchPhase.Waiting, RoomMatchPhase.Checking);

    public void RequestMatch() => TransitionMatch(RoomMatchPhase.Checking, RoomMatchPhase.Requesting);

    public void BeginSearch(long startedAt)
    {
        TransitionMatch(RoomMatchPhase.Requesting, RoomMatchPhase.Searching);
        MatchStartedAt = startedAt;
    }

    public void OpenConfirmation(PendingMatch match)
    {
        if (!match.Rooms.Contains(this))
        {
            throw new InvalidOperationException("The match does not contain this room");
        }

        TransitionMatch(RoomMatchPhase.Searching, RoomMatchPhase.Confirming);
        Match = match;
    }

    public void ResumeSearch(PendingMatch match)
    {
        RequireCurrentMatch(match);
        TransitionMatch(RoomMatchPhase.Confirming, RoomMatchPhase.Searching);
        Match = null;
    }

    public void StartConfirmedMatch(PendingMatch match)
    {
        RequireCurrentMatch(match);
        if (!match.IsConfirmed)
        {
            throw new InvalidOperationException("The match has not been confirmed by every participant");
        }

        TransitionMatch(RoomMatchPhase.Confirming, RoomMatchPhase.Started);
    }

    public void ResetMatch()
    {
        if (MatchPhase == RoomMatchPhase.Started)
        {
            throw new InvalidOperationException("A room whose match has started cannot return to matchmaking");
        }

        Match = null;
        MatchStartedAt = 0;
        MatchPhase = RoomMatchPhase.Waiting;
    }

    private void RequireCurrentMatch(PendingMatch match)
    {
        if (Match != match)
        {
            throw new InvalidOperationException("The match is no longer current for this room");
        }
    }

    private void TransitionMatch(RoomMatchPhase expected, RoomMatchPhase next)
    {
        if (MatchPhase != expected)
        {
            throw new InvalidOperationException($"Cannot change room matchmaking from {MatchPhase} to {next}");
        }

        MatchPhase = next;
    }

    public RoomMember? FindMember(ulong uid) => members.Find(member => member.Uid == uid);

    public RoomMember? FindMemberAt(int campClient, int posOfCamp) =>
        members.Find(member => member.CampClient == campClient && member.PosOfCamp == posOfCamp);

    public int[] ListedCamps() => RoomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch
        ? [JudgeCamp, LeftCamp]
        : [JudgeCamp, LeftCamp, RightCamp];

    public int[] CampsInUse() => RoomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch
        ? [LeftCamp]
        : [LeftCamp, RightCamp, JudgeCamp];

    public int CapacityOf(int campClient) => campClient switch
    {
        LeftCamp => CampPlayerNum,
        RightCamp when RoomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeNormal => CampPlayerNum,
        JudgeCamp when RoomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeNormal => JudgeCampCapacity,
        _ => 0,
    };

    public RoomBot? FindBotAt(int campClient, int posOfCamp) =>
        bots.Find(bot => bot.CampClient == campClient && bot.PosOfCamp == posOfCamp);

    public bool IsSeatFree(int campClient, int posOfCamp) =>
        FindMemberAt(campClient, posOfCamp) is null && FindBotAt(campClient, posOfCamp) is null;

    public int CountIn(int campClient) =>
        members.Count(member => member.CampClient == campClient) + bots.Count(bot => bot.CampClient == campClient);

    public (int CampClient, int PosOfCamp)? FindFreeSeat() =>
        CampsInUse()
            .SelectMany(camp => Enumerable.Range(0, CapacityOf(camp)).Select(pos => (camp, pos)))
            .Where(seat => IsSeatFree(seat.camp, seat.pos))
            .Select(seat => ((int, int)?)seat)
            .FirstOrDefault();

    public int? FindFreePosIn(int campClient) =>
        Enumerable.Range(0, CapacityOf(campClient))
            .Where(pos => IsSeatFree(campClient, pos))
            .Select(pos => (int?)pos)
            .FirstOrDefault();

    public RoomBot AddBot(int campClient, int posOfCamp, pbAiInfoConfig row)
    {
        var uid = new NpcUids(members.Select(member => member.Uid).Concat(bots.Select(bot => bot.Uid))).NextRandom(NoReservedInOrderUids);
        var bot = new RoomBot(uid, campClient, posOfCamp, row);
        bots.Add(bot);
        return bot;
    }

    public void RemoveBot(RoomBot bot) => bots.Remove(bot);

    public RoomMember Add(ulong uid, int campClient, int posOfCamp, pbCOMDT_ACNTMAP_INFO acntMapInfo, RoomMemberProfile profile)
    {
        var teamFlag = members.Count == 0 ? FirstTeamFlag : members.Max(member => member.TeamFlag) + 1;
        var member = new RoomMember(uid, campClient, posOfCamp, teamFlag, acntMapInfo, profile);
        members.Add(member);
        return member;
    }

    public void Remove(RoomMember member) => members.Remove(member);

    public HashSet<int> CanUseMap()
    {
        var common = new HashSet<int>(members.Count == 0 ? [] : members[0].UsableMaps);
        foreach (var member in members.Skip(1))
        {
            common.IntersectWith(member.UsableMaps);
        }

        return common;
    }

    private static int BuildLobbyGameEntity() =>
        (Random.Shared.Next(FirstGameEntityInstance, GameEntityInstanceCount) << GameEntityTypeBits) | LobbyGameEntityType;
}

public sealed class SettleRoomPlan(MatchRoom finishedRoom)
{
    public MatchRoom FinishedRoom { get; } = finishedRoom;
    public MatchRoom? SettleRoom { get; set; }
}
