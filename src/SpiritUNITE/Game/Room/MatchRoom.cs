using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed class RoomMember(
    ulong uid,
    int campClient,
    int posOfCamp,
    pbCOMDT_ACNTMAP_INFO acntMapInfo,
    RoomMemberProfile profile)
{
    public ulong Uid { get; } = uid;
    public int CampClient { get; set; } = campClient;
    public int PosOfCamp { get; set; } = posOfCamp;
    public pbCOMDT_ACNTMAP_INFO AcntMapInfo { get; set; } = acntMapInfo;
    public RoomMemberProfile Profile { get; } = profile;

    public IReadOnlySet<int> UsableMaps =>
        AcntMapInfo.MapList.Where(map => map.CanUseType == RoomOptions.CanUseMap).Select(map => map.MapId).ToHashSet();
}

public sealed record RoomPosChange(int ChgSeq, ulong SenderUid, ulong ReceiverUid, long ExpiresAt);

public sealed class MatchRoom(int roomID, long roomSeq, int roomShowId, ulong masterUid)
{
    public const int RoomEntity = 1;
    public const int LeftCamp = (int)PB_ENUM_COM_CAMP_CLIENT.PbCampClientPlayer1;
    public const int RightCamp = (int)PB_ENUM_COM_CAMP_CLIENT.PbCampClientPlayer2;
    public const int JudgeCamp = (int)PB_ENUM_COM_CAMP_CLIENT.PbCampClientJudge1;
    public const int JudgeCampCapacity = (int)PB_MACRO_COM_MAX_JUDGE_NUM.PbComMaxJudgeNum;

    private readonly List<RoomMember> members = [];

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
    public PB_ENUM_COM_ROOMSVR_ROOM_STATE State { get; set; } = PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateWait;
    public long MatchStartedAt { get; set; }
    public RoomPosChange? PendingPosChange { get; set; }
    public int NextChgSeq { get; set; } = 1;
    public pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE? PveChallengeSelectNode { get; set; }
    public int MatchSeq { get; set; }
    public HashSet<ulong> ConfirmedUids { get; } = [];

    public IReadOnlyList<RoomMember> Members => members;

    public bool IsMatching => State == PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateReq;

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

    public int CountIn(int campClient) => members.Count(member => member.CampClient == campClient);

    public (int CampClient, int PosOfCamp)? FindFreeSeat() =>
        CampsInUse()
            .SelectMany(camp => Enumerable.Range(0, CapacityOf(camp)).Select(pos => (camp, pos)))
            .Where(seat => FindMemberAt(seat.camp, seat.pos) is null)
            .Select(seat => ((int, int)?)seat)
            .FirstOrDefault();

    public RoomMember Add(ulong uid, int campClient, int posOfCamp, pbCOMDT_ACNTMAP_INFO acntMapInfo, RoomMemberProfile profile)
    {
        var member = new RoomMember(uid, campClient, posOfCamp, acntMapInfo, profile);
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
}
