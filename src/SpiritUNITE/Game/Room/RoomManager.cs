using Microsoft.Extensions.Options;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.PveChallenge;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed record RoomCreateRequest(
    int MapMode,
    int MapID,
    int SubMapMode,
    int FromSystemType,
    IReadOnlyCollection<int> UsableMaps,
    pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE? PveChallengeSelectNode = null);

public readonly record struct SearchWaitSamples(long TotalSeconds, int Count)
{
    public int MeanSeconds => Count == 0 ? 0 : (int)Math.Round((double)TotalSeconds / Count, MidpointRounding.AwayFromZero);

    public SearchWaitSamples Add(long waitedSeconds) => new(TotalSeconds + waitedSeconds, Count + 1);
}

public sealed class RoomManager(
    RoleInfoStore roles,
    GameData gameData,
    PveChallengeProgress pveChallenge,
    LobbyNotices notices,
    LobbyRoles lobbyRoles,
    IOptions<RoomOptions> roomOptions,
    IOptions<GVoiceOptions> gVoiceOptions,
    TimeProvider clock)
{
    public const int ChgPosTimeOutSec = 10;

    private const int JoinRoomTypeNormal = (int)PB_ENUM_ENM_JOIN_ROOM_TYPE.PbEnmJoinRoomTypeNormal;
    private const int JoinRoomTypeSettle = (int)PB_ENUM_ENM_JOIN_ROOM_TYPE.PbEnmJoinRoomTypeSettle;
    private const int MinRoomShowId = 10000000;
    private const int MaxRoomShowId = 100000000;
    private const int SingleRankedTeam = (int)PB_ENUM_ENM_RANKED_TEAM_MEMBER_NUM_TYPE.PbEnmRankedTeamMemberNumTypeSingle;
    private const int ThreeRankedTeam = (int)PB_ENUM_ENM_RANKED_TEAM_MEMBER_NUM_TYPE.PbEnmRankedTeamMemberNumTypeThree;
    private const int FiveRankedTeam = (int)PB_ENUM_ENM_RANKED_TEAM_MEMBER_NUM_TYPE.PbEnmRankedTeamMemberNumTypeFive;
    private const int DefaultRankedTeamMemberNumType = ThreeRankedTeam;
    private const int NoEstimatedWaitTime = 0;
    private const int RoomChatEnter = (int)PB_ENUM_ENM_CHAT_ROOM_CHAT_TYPE.PbEnmChatRoomChatTypeEnter;
    private const long MillisecondsPerSecond = 1000;

    private static readonly PB_ENUM_COM_ROOMSVR_ROOM_STATE[] MatchRequestStates =
    [
        PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateChk,
        PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateReq,
    ];

    private readonly Lock gate = new();
    private readonly Dictionary<int, MatchRoom> roomsById = [];
    private readonly Dictionary<int, MatchRoom> roomsByShowId = [];
    private readonly Dictionary<ulong, MatchRoom> roomsByUid = [];
    private readonly Dictionary<ulong, SettleRoomPlan> settleRoomsByUid = [];
    private readonly Dictionary<(int MapId, int PartySize), SearchWaitSamples> searchWaits = [];
    private readonly RoomOptions options = roomOptions.Value;
    private readonly GVoiceOptions gVoice = gVoiceOptions.Value;
    private int nextRoomID = 1;

    public MatchRoom? FindRoomOf(ulong uid)
    {
        lock (gate)
        {
            return roomsByUid.GetValueOrDefault(uid);
        }
    }

    public RoomOutcome Create(ulong uid, RoomCreateRequest request, RoomMemberProfile profile)
    {
        lock (gate)
        {
            if (roomsByUid.ContainsKey(uid))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrStillInOtherRoom);
            }

            var acntMapInfo = RoomMemberDetail.BuildAcntMapInfo(gameData, options, request.UsableMaps);
            var usableMaps = UsableMapsOf(acntMapInfo);
            pbScene_Map? scene;
            if (request.MapID != 0)
            {
                scene = gameData.SceneMap.Find(request.MapID);
                if (scene is null || !usableMaps.Contains(scene.SceneId))
                {
                    return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalid);
                }
            }
            else
            {
                var subMode = request.SubMapMode != 0 ? request.SubMapMode : (int)RoomMaps.DefaultSubModeOf(request.MapMode);
                if (subMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeInvalid)
                {
                    return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapModeInvalid);
                }

                scene = RoomMaps.FindDefaultMap(gameData, subMode, usableMaps);
                if (scene is null)
                {
                    return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomNoMapCanUse);
                }
            }

            if (roles.Find(uid) is not { } role)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntInvalid);
            }

            if (scene.MapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank
                && !RankedMatch.IsRankedMatchUnlocked(gameData, notices, role))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchLevelLimit);
            }

            pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE? pveChallengeSelectNode = null;
            if (scene.MapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModePveChallenge)
            {
                if (request.PveChallengeSelectNode is not { } selectNode || !IsSelectableNode(role, scene.SceneId, selectNode))
                {
                    return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalid);
                }

                pveChallengeSelectNode = selectNode.Clone();
            }

            var room = new MatchRoom(nextRoomID++, clock.GetUtcNow().ToUnixTimeSeconds(), AllocateShowId(), uid);
            ApplyMap(room, scene);
            room.PveChallengeSelectNode = pveChallengeSelectNode;
            room.RankedTeamMemberNumType = DefaultRankedTeamMemberNumType;
            room.Add(uid, MatchRoom.LeftCamp, 0, acntMapInfo, profile);
            roomsById[room.RoomID] = room;
            roomsByShowId[room.RoomShowId] = room;
            roomsByUid[uid] = room;
            return new RoomOutcome()
                .Reply(PB_ENUM_CS_MSGID_DEF.PbScidJoinRoomNtf, BuildJoinRoomNtf(room, uid, request.FromSystemType, JoinRoomTypeNormal))
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.ShowIdChg(room));
        }
    }

    public RoomOutcome JoinByShowId(ulong uid, int roomShowId, IReadOnlyCollection<int> usableMaps, RoomMemberProfile profile)
    {
        lock (gate)
        {
            return roomsByShowId.TryGetValue(roomShowId, out var room)
                ? Join(uid, room, usableMaps, profile)
                : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomNotFound);
        }
    }

    public RoomOutcome JoinById(
        ulong uid,
        int roomID,
        long roomSeq,
        IReadOnlyCollection<int> usableMaps,
        RoomMemberProfile profile)
    {
        lock (gate)
        {
            return roomsById.TryGetValue(roomID, out var room) && room.RoomSeq == roomSeq
                ? Join(uid, room, usableMaps, profile)
                : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomNotFound);
        }
    }

    public RoomOutcome EnterSettleRoom(ulong uid)
    {
        lock (gate)
        {
            if (!settleRoomsByUid.Remove(uid, out var plan) || plan.FinishedRoom.FindMember(uid) is not { } finishedMember)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomNotFound);
            }

            if (roomsByUid.ContainsKey(uid))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrStillInOtherRoom);
            }

            if (plan.SettleRoom is { } settleRoom && roomsById.GetValueOrDefault(settleRoom.RoomID) == settleRoom)
            {
                return Join(uid, settleRoom, finishedMember.AcntMapInfo, finishedMember.Profile, JoinRoomTypeSettle);
            }

            if (roles.Find(uid) is null)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntInvalid);
            }

            var finished = plan.FinishedRoom;
            var room = new MatchRoom(nextRoomID++, clock.GetUtcNow().ToUnixTimeSeconds(), AllocateShowId(), uid)
            {
                RoomType = finished.RoomType,
                ManMachine = finished.ManMachine,
                MapMode = finished.MapMode,
                MapSubMode = finished.MapSubMode,
                MapId = finished.MapId,
                CampPlayerNum = finished.CampPlayerNum,
                RankedTeamMemberNumType = finished.RankedTeamMemberNumType,
            };
            room.Add(uid, MatchRoom.LeftCamp, 0, finishedMember.AcntMapInfo, finishedMember.Profile).IsSettle = true;
            roomsById[room.RoomID] = room;
            roomsByShowId[room.RoomShowId] = room;
            roomsByUid[uid] = room;
            plan.SettleRoom = room;
            return new RoomOutcome()
                .Reply(PB_ENUM_CS_MSGID_DEF.PbScidJoinRoomNtf, BuildJoinRoomNtf(room, uid, 0, JoinRoomTypeSettle))
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.ShowIdChg(room));
        }
    }

    public RoomOutcome Quit(ulong uid)
    {
        lock (gate)
        {
            return roomsByUid.TryGetValue(uid, out var room)
                ? Leave(room, room.FindMember(uid)!, PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON.PbEnmMatchteamDestoryReasonAcntleave)
                : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom);
        }
    }

    public RoomOutcome Kick(ulong uid, int campClient, int posOfCamp)
    {
        lock (gate)
        {
            if (FindMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (room.IsMatching)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMatching);
            }

            var target = room.FindMemberAt(campClient, posOfCamp);
            if (target is null || target.Uid == uid)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomCampPosInvalid);
            }

            var outcome = Leave(room, target, PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON.PbEnmMatchteamDestoryReasonAcntleave);
            return outcome.Push(target.Uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.SelfBeKick(room));
        }
    }

    public RoomOutcome StartGame(ulong uid)
    {
        lock (gate)
        {
            if (FindMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (room.State != PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateWait)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomStateInvalid);
            }

            if (room.RoomType != PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrSystemNotOpen);
            }

            if (IsRanked(room) && room.Members.Count > room.RankedTeamMemberNumType)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomInvalidPlayerNum);
            }

            if (room.PveChallengeSelectNode is { } selectNode)
            {
                return room.Members.All(member => roles.Find(member.Uid) is { } role && IsSelectableNode(role, room.MapId, selectNode))
                    ? StartPveChallengeMatch(room)
                    : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomSomeoneHasNoMap);
            }

            if (room.MapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModePveChallenge)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalid);
            }

            return StartSearch(room);
        }
    }

    public RoomOutcome CompleteSearch(MatchTicket ticket)
    {
        lock (gate)
        {
            var isCpuMatch = false;
            if (!roomsById.TryGetValue(ticket.RoomID, out var room)
                || !room.Holds(ticket)
                || !room.IsSearching
                || !((isCpuMatch = CpuMatch.IsCpuMatch(room)) || IsWarmBattleRoom(room)))
            {
                return new RoomOutcome();
            }

            var fillerRules = new CpuMatchFillerRules(
                RankedMatch.GetRankedMatchUnlockLevel(gameData, notices),
                RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId);
            var succTimeStamp = NextSecond();
            var matchSucc = isCpuMatch
                ? CpuMatch.MatchSucc(gameData, room, BuildMatchMembers(room), options.TeamMMRScore, fillerRules, succTimeStamp)
                : CpuMatch.WarmMatchSucc(gameData, room, BuildMatchMembers(room), options.TeamMMRScore, fillerRules, succTimeStamp);
            if (isCpuMatch)
            {
                RecordSearchWait(room, succTimeStamp - room.MatchStartedAt);
            }

            return OpenConfirmWindow(room, matchSucc, npcsConfirm: true, new RoomOutcome());
        }
    }

    public RoomOutcome ConfirmNpc(MatchTicket ticket, ulong npcUid)
    {
        lock (gate)
        {
            if (!roomsById.TryGetValue(ticket.RoomID, out var room)
                || !room.Holds(ticket)
                || !room.IsConfirming
                || !room.UnconfirmedNpcUids.Remove(npcUid))
            {
                return new RoomOutcome();
            }

            var outcome = new RoomOutcome().PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomConfirmRsp, RoomNotices.NpcConfirmed(npcUid));
            return CompleteMatchIfConfirmed(room, outcome);
        }
    }

    public RoomOutcome ConfirmMatch(ulong uid)
    {
        lock (gate)
        {
            if (!roomsByUid.TryGetValue(uid, out var room))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom);
            }

            if (!room.IsConfirming || !room.ConfirmedUids.Add(uid))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomStateInvalid);
            }

            var outcome = new RoomOutcome().PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomConfirmRsp, RoomNotices.HumanConfirmed(uid));
            return CompleteMatchIfConfirmed(room, outcome);
        }
    }

    public RoomOutcome ExpireMatchConfirm(MatchTicket ticket, out ulong[] removedUids)
    {
        lock (gate)
        {
            removedUids = [];
            if (!roomsById.TryGetValue(ticket.RoomID, out var room) || !room.Holds(ticket) || !room.IsConfirming)
            {
                return new RoomOutcome();
            }

            var unconfirmed = room.Members.Where(member => !room.ConfirmedUids.Contains(member.Uid)).ToArray();
            removedUids = [.. unconfirmed.Select(member => member.Uid)];
            var outcome = new RoomOutcome();
            foreach (var member in unconfirmed)
            {
                outcome
                    .Push(member.Uid, PB_ENUM_CS_MSGID_DEF.PbScidMultGameAbortNtf, RoomNotices.MatchAbort(PB_ENUM_RES_ERROR_CODE.PbErrRoomUnconfirm))
                    .Push(member.Uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.SelfBeKick(room));
            }

            if (unconfirmed.Length == room.Members.Count)
            {
                Dissolve(room);
                return outcome;
            }

            var before = BuildCanUseMap(room);
            AbortConfirm(room, PB_ENUM_RES_ERROR_CODE.PbErrRoomTeamerUnconfirm, outcome, removedUids);
            foreach (var member in unconfirmed)
            {
                room.Remove(member);
                roomsByUid.Remove(member.Uid);
                PushDeparture(room, member, outcome);
            }

            return PushCanUseMapChange(room, before, outcome, MemberUids(room));
        }
    }

    public RoomOutcome CancelMatch(ulong uid)
    {
        lock (gate)
        {
            if (!roomsByUid.TryGetValue(uid, out var room))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom);
            }

            if (!room.IsSearching)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrMatchCancelFail);
            }

            return StopMatching(room, uid, PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON.PbEnmMatchteamDestoryReasonAcntcancel);
        }
    }

    public RoomOutcome ChangeMap(ulong uid, int mapId)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            var scene = gameData.SceneMap.Find(mapId);
            if (scene is null || scene.MapSubMode != room.MapSubMode)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalid);
            }

            if (!room.CanUseMap().Contains(mapId))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalidNoMap);
            }

            if (room.CampsInUse().Any(camp => room.CountIn(camp) > CapacityAfter(room, scene, camp)))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalidToMorePlayer);
            }

            if (scene.SceneId == room.MapId)
            {
                return new RoomOutcome();
            }

            ApplyMap(room, scene);
            return new RoomOutcome()
                .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.LegendPokemonChg(room))
                .RespondHere(uid)
                .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.MapChg(room));
        }
    }

    public RoomOutcome ChangeSubMode(ulong uid, int subMode)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (subMode == room.MapSubMode)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomSameSubMode);
            }

            var scene = RoomMaps.FindDefaultMap(gameData, subMode, room.CanUseMap());
            if (scene is null)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomInvalidSubMode);
            }

            if (room.CampsInUse().Any(camp => room.CountIn(camp) > CapacityAfter(room, scene, camp)))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalidToMorePlayer);
            }

            var before = BuildCanUseMap(room);
            ApplyMap(room, scene);
            var outcome = new RoomOutcome()
                .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.SubModeChg(room))
                .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.MapChg(room));
            return PushCanUseMapChange(room, before, outcome, MemberUids(room));
        }
    }

    public RoomOutcome ChangeRankedTeamMemberNumType(ulong uid, int rankedTeamMemberNumType)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (!IsRanked(room) || rankedTeamMemberNumType is not (SingleRankedTeam or ThreeRankedTeam or FiveRankedTeam))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomTypeInvalid);
            }

            if (room.Members.Count > rankedTeamMemberNumType)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomInvalidPlayerNum);
            }

            room.RankedTeamMemberNumType = rankedTeamMemberNumType;
            return new RoomOutcome().PushAll(
                MemberUids(room),
                PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf,
                RoomNotices.RankedTeamMemberNumTypeChg(room));
        }
    }

    public RoomOutcome ChangeRoomType(ulong uid, int roomType, int manMachine)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (roomType is not ((int)PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch or (int)PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeNormal)
                || manMachine is not (0 or 1))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomTypeInvalid);
            }

            var newType = (PB_ENUM_COM_ROOM_TYPE)roomType;
            if (newType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch && room.Members.Count > room.CampPlayerNum)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomInvalidPlayerNum);
            }

            var outcome = new RoomOutcome()
                .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.LegendPokemonChg(room));
            room.RoomType = newType;
            room.ManMachine = manMachine;
            room.PendingPosChange = null;
            if (newType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch)
            {
                foreach (var member in room.Members.Where(member => member.CampClient != MatchRoom.LeftCamp).ToList())
                {
                    var (oldCamp, oldPos) = (member.CampClient, member.PosOfCamp);
                    var (newCamp, newPos) = room.FindFreeSeat()!.Value;
                    (member.CampClient, member.PosOfCamp) = (newCamp, newPos);
                    outcome.PushAll(
                        MemberUids(room),
                        PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf,
                        RoomNotices.MemberPos(
                            RoomNotices.PosData(member, oldCamp, oldPos),
                            new pbCOMDT_CHGMEMBERPOS_DATA(),
                            PB_ENUM_COM_CHGROOMPOS_TYPE.PbComChgroomposEmpty,
                            room));
                }
            }

            return outcome.PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.RoomTypeChg(room));
        }
    }

    public RoomOutcome ChangeMaster(ulong uid, ulong newMasterUid)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (newMasterUid == uid || room.FindMember(newMasterUid) is null)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMasterInvalid);
            }

            room.MasterUid = newMasterUid;
            return new RoomOutcome().PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.MasterChg(room));
        }
    }

    public RoomOutcome ChangePos(ulong uid, int campClient, int posOfCamp)
    {
        lock (gate)
        {
            if (!roomsByUid.TryGetValue(uid, out var room))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom);
            }

            if (room.IsMatching)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMatching);
            }

            if (!room.CampsInUse().Contains(campClient))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomCampInvalid);
            }

            if (posOfCamp < 0 || posOfCamp >= room.CapacityOf(campClient))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomCampPosInvalid);
            }

            var sender = room.FindMember(uid)!;
            var receiver = room.FindMemberAt(campClient, posOfCamp);
            if (receiver == sender)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomChgposFail);
            }

            if (receiver is null)
            {
                var (oldCamp, oldPos) = (sender.CampClient, sender.PosOfCamp);
                (sender.CampClient, sender.PosOfCamp) = (campClient, posOfCamp);
                return new RoomOutcome().PushAll(
                    MemberUids(room),
                    PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf,
                    RoomNotices.MemberPos(
                        RoomNotices.PosData(sender, oldCamp, oldPos),
                        new pbCOMDT_CHGMEMBERPOS_DATA(),
                        PB_ENUM_COM_CHGROOMPOS_TYPE.PbComChgroomposEmpty,
                        room));
            }

            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            if (room.PendingPosChange is { } pending && pending.ExpiresAt > now)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomChgposBusy);
            }

            room.PendingPosChange = new RoomPosChange(room.NextChgSeq++, sender.Uid, receiver.Uid, now + ChgPosTimeOutSec);
            return new RoomOutcome().Push(
                receiver.Uid,
                PB_ENUM_CS_MSGID_DEF.PbScidRoomChgPosNtf,
                RoomNotices.ChgPosRequest(room, sender, receiver, ChgPosTimeOutSec));
        }
    }

    public RoomOutcome ConfirmPosChange(ulong uid, bool accept, int chgSeq)
    {
        lock (gate)
        {
            if (!roomsByUid.TryGetValue(uid, out var room))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom);
            }

            if (room.PendingPosChange is not { } pending || pending.ChgSeq != chgSeq || pending.ReceiverUid != uid)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomChgposFail);
            }

            room.PendingPosChange = null;
            var sender = room.FindMember(pending.SenderUid);
            var receiver = room.FindMember(pending.ReceiverUid)!;
            if (sender is null || room.IsMatching || pending.ExpiresAt <= clock.GetUtcNow().ToUnixTimeSeconds())
            {
                return sender is null
                    ? RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomChgposCancel)
                    : new RoomOutcome().Push(
                        sender.Uid,
                        PB_ENUM_CS_MSGID_DEF.PbScidRoomChgPosNtf,
                        RoomNotices.ChgPosRefused(PB_ENUM_RES_ERROR_CODE.PbErrRoomChgposCancel));
            }

            if (!accept)
            {
                return new RoomOutcome().Push(
                    sender.Uid,
                    PB_ENUM_CS_MSGID_DEF.PbScidRoomChgPosNtf,
                    RoomNotices.ChgPosRefused(PB_ENUM_RES_ERROR_CODE.PbErrRoomChgposRefuse));
            }

            var (senderCamp, senderPos) = (sender.CampClient, sender.PosOfCamp);
            var (receiverCamp, receiverPos) = (receiver.CampClient, receiver.PosOfCamp);
            (sender.CampClient, sender.PosOfCamp) = (receiverCamp, receiverPos);
            (receiver.CampClient, receiver.PosOfCamp) = (senderCamp, senderPos);
            return new RoomOutcome().PushAll(
                MemberUids(room),
                PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf,
                RoomNotices.MemberPos(
                    RoomNotices.PosData(sender, senderCamp, senderPos),
                    RoomNotices.PosData(receiver, receiverCamp, receiverPos),
                    PB_ENUM_COM_CHGROOMPOS_TYPE.PbComChgroomposPlayer,
                    room));
        }
    }

    public RoomOutcome UpdateUsableMaps(ulong uid, IReadOnlyCollection<int> usableMaps)
    {
        lock (gate)
        {
            if (!roomsByUid.TryGetValue(uid, out var room))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom);
            }

            var before = BuildCanUseMap(room);
            room.FindMember(uid)!.AcntMapInfo = RoomMemberDetail.BuildAcntMapInfo(gameData, options, usableMaps);
            return PushCanUseMapChange(room, before, new RoomOutcome(), MemberUids(room));
        }
    }

    public RoomOutcome LeaveOnDisconnect(ulong uid)
    {
        lock (gate)
        {
            settleRoomsByUid.Remove(uid);
            return roomsByUid.TryGetValue(uid, out var room)
                ? Leave(room, room.FindMember(uid)!, PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON.PbEnmMatchteamDestoryReasonAcntleave)
                : new RoomOutcome();
        }
    }

    private RoomOutcome Join(ulong uid, MatchRoom room, IReadOnlyCollection<int> usableMaps, RoomMemberProfile profile) =>
        Join(uid, room, RoomMemberDetail.BuildAcntMapInfo(gameData, options, usableMaps), profile, JoinRoomTypeNormal);

    private RoomOutcome Join(ulong uid, MatchRoom room, pbCOMDT_ACNTMAP_INFO acntMapInfo, RoomMemberProfile profile, int joinRoomType)
    {
        if (roomsByUid.TryGetValue(uid, out var current))
        {
            return RoomOutcome.Refused(current == room
                ? PB_ENUM_RES_ERROR_CODE.PbErrRepeatIntoTheRoom
                : PB_ENUM_RES_ERROR_CODE.PbErrRoomAlreadyInRoom);
        }

        if (room.IsMatching)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMatching);
        }

        if (IsRanked(room) && room.Members.Count >= room.RankedTeamMemberNumType)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMemberFull);
        }

        if (room.FindFreeSeat() is not { } seat)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMemberFull);
        }

        var (campClient, posOfCamp) = seat;

        if (roles.Find(uid) is not { } role)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntInvalid);
        }

        if (room.PveChallengeSelectNode is { } selectNode && !IsSelectableNode(role, room.MapId, selectNode))
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalid);
        }

        var before = BuildCanUseMap(room);
        var others = MemberUids(room).ToArray();
        var member = room.Add(uid, campClient, posOfCamp, acntMapInfo, profile);
        member.IsSettle = joinRoomType == JoinRoomTypeSettle;
        roomsByUid[uid] = room;
        var joinRoomNtf = BuildJoinRoomNtf(room, uid, 0, joinRoomType);
        joinRoomNtf.RoomInfo.RoomAttr.RoomShowId = room.RoomShowId;
        var outcome = new RoomOutcome()
            .Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.LegendPokemonChg(room))
            .Reply(PB_ENUM_CS_MSGID_DEF.PbScidJoinRoomNtf, joinRoomNtf)
            .PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidChatMsgNtf, RoomNotices.RoomChat(BuildRoomChatSender(role), RoomChatEnter))
            .PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.PlayerAdd(room, member, BuildRoomMemberDT(member)!))
            .PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.LegendPokemonChg(room));
        foreach (var other in others)
        {
            outcome.Push(other, PB_ENUM_CS_MSGID_DEF.PbScidRoomRttNtf, RoomNotices.RoomRtt(gVoice, lobbyRoles.Rtt(other)));
        }

        if (joinRoomType != JoinRoomTypeSettle)
        {
            outcome
                .PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidRoomMicTipsInfoNtf, new pbSCRoomMicTipsInfoNtf())
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomRttNtf, RoomNotices.RoomRtt(gVoice, lobbyRoles.Rtt(uid)))
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomMicTipsInfoNtf, new pbSCRoomMicTipsInfoNtf());
        }

        return PushCanUseMapChange(room, before, outcome, others);
    }

    public RoomOutcome ReturnFromSettle(ulong uid)
    {
        lock (gate)
        {
            if (!roomsByUid.TryGetValue(uid, out var room) || room.FindMember(uid) is not { } member)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom);
            }

            if (!member.IsSettle)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomStateInvalid);
            }

            member.IsSettle = false;
            return new RoomOutcome()
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomMicTipsInfoNtf, new pbSCRoomMicTipsInfoNtf())
                .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidSettleAcntReturnRoomNtf, new pbSCSettleAcntReturnRoomNtf
                {
                    ResultInfo = new pbResultInfo(),
                    Uid = uid,
                });
        }
    }

    public MatchRoom? FindSettleRoomOf(ulong uid)
    {
        lock (gate)
        {
            return roomsByUid.GetValueOrDefault(uid) is { } room && room.FindMember(uid) is { IsSettle: true } ? room : null;
        }
    }

    private RoomOutcome Leave(MatchRoom room, RoomMember member, PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON reason)
    {
        if (room.PendingPosChange is { } pending && (pending.SenderUid == member.Uid || pending.ReceiverUid == member.Uid))
        {
            room.PendingPosChange = null;
        }

        var before = BuildCanUseMap(room);
        var wasSearching = room.IsSearching;
        var wasConfirming = room.IsConfirming;
        room.Remove(member);
        roomsByUid.Remove(member.Uid);
        if (room.Members.Count == 0)
        {
            roomsById.Remove(room.RoomID);
            roomsByShowId.Remove(room.RoomShowId);
            return new RoomOutcome();
        }

        var outcome = wasSearching ? StopMatching(room, member.Uid, reason) : new RoomOutcome();
        if (wasConfirming)
        {
            AbortConfirm(room, PB_ENUM_RES_ERROR_CODE.PbErrRoomTeamerLeave, outcome, [member.Uid]);
        }

        PushDeparture(room, member, outcome);
        return PushCanUseMapChange(room, before, outcome, MemberUids(room));
    }

    private void PushDeparture(MatchRoom room, RoomMember member, RoomOutcome outcome)
    {
        outcome.PushAll(
            MemberUids(room),
            PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf,
            RoomNotices.PlayerLeave(room, member.CampClient, member.PosOfCamp));
        if (room.MasterUid == member.Uid)
        {
            room.MasterUid = room.Members[0].Uid;
            outcome.PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.MasterChg(room));
        }
    }

    private RoomOutcome StopMatching(MatchRoom room, ulong byUid, PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON reason)
    {
        room.ClearMatch();
        var name = roles.Find(byUid)?.InGameUsrName ?? string.Empty;
        var others = MemberUids(room).Where(uid => uid != byUid).ToArray();
        var outcome = new RoomOutcome().PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidCancelMatchNtf, RoomNotices.CancelMatch(reason, name));
        ChangeState(room, PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateWait, MemberUids(room), outcome);
        return outcome;
    }

    private void AbortConfirm(MatchRoom room, PB_ENUM_RES_ERROR_CODE result, RoomOutcome outcome, IReadOnlyCollection<ulong> departingUids)
    {
        ulong[] staying = [.. MemberUids(room).Where(uid => !departingUids.Contains(uid))];
        outcome.PushAll(staying, PB_ENUM_CS_MSGID_DEF.PbScidMultGameAbortNtf, RoomNotices.MatchAbort(result));
        room.ClearMatch();
        ChangeState(room, PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateWait, staying, outcome);
    }

    private RoomOutcome BeginMatch(MatchRoom room)
    {
        room.ClearMatch();
        room.MatchSeq++;
        room.PendingPosChange = null;
        var uids = MemberUids(room);
        var outcome = new RoomOutcome();
        foreach (var state in MatchRequestStates)
        {
            ChangeState(room, state, uids, outcome);
        }

        outcome.Push(room.MasterUid, PB_ENUM_CS_MSGID_DEF.PbScidRoomStartGameRsp, new pbSCRoomStartGameRsp
        {
            ResultInfo = new pbResultInfo(),
        });
        ChangeState(room, PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateProc, uids, outcome);
        return outcome;
    }

    private RoomOutcome StartSearch(MatchRoom room)
    {
        var outcome = BeginMatch(room);
        room.MatchStartedAt = NextSecond();
        var isCpuMatch = CpuMatch.IsCpuMatch(room);
        var isWarmBattle = !isCpuMatch && IsWarmBattleRoom(room);
        var waitTime = isCpuMatch ? EstimateSearchWait(room) : isWarmBattle ? options.WarmBattleWaitTime : NoEstimatedWaitTime;
        outcome.PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidMatchRsp, RoomNotices.MatchProcess(room, waitTime));
        return isCpuMatch || isWarmBattle
            ? outcome.SearchFor(new MatchSearch(room.Ticket, TimeSpan.FromSeconds(isCpuMatch ? options.ManMachineSearchSec : options.WarmBattleSearchSec)))
            : outcome;
    }

    private int EstimateSearchWait(MatchRoom room) =>
        searchWaits.TryGetValue((room.MapId, room.Members.Count), out var samples) ? samples.MeanSeconds : NoEstimatedWaitTime;

    private void RecordSearchWait(MatchRoom room, long waitedSeconds)
    {
        var key = (room.MapId, room.Members.Count);
        searchWaits[key] = searchWaits.GetValueOrDefault(key).Add(waitedSeconds);
    }

    private bool IsWarmBattleRoom(MatchRoom room) =>
        room.RoomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch
        && room.ManMachine != CpuMatch.ManMachine
        && room.MapSubMode is (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceNormal or (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRelaxNormal
        && gameData.SceneMap.Find(room.MapId)?.GameType == (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeCatch
        && room.Members.All(member => roles.Find(member.Uid) is { } role && RoleProfile.GetLevel(gameData, role) <= options.WarmBattleMaxTrainerLevel);

    private RoomOutcome StartPveChallengeMatch(MatchRoom room)
    {
        var outcome = BeginMatch(room);
        var succTimeStamp = NextSecond();
        room.MatchStartedAt = succTimeStamp;
        var masterTrainerLevel = roles.Find(room.MasterUid) is { } master ? RoleProfile.GetLevel(gameData, master) : PveMatch.MinAILevel;
        var matchSucc = PveMatch.MatchSucc(gameData, room, BuildMatchMembers(room), masterTrainerLevel, succTimeStamp);
        return OpenConfirmWindow(room, matchSucc, npcsConfirm: false, outcome);
    }

    private RoomOutcome OpenConfirmWindow(MatchRoom room, BotMatchSucc matchSucc, bool npcsConfirm, RoomOutcome outcome)
    {
        var detail = matchSucc.MatchRsp.MatchResult.MatchResDetail.MatchSucc;
        room.MatchSucc = detail;
        room.MatchNpcConfigs = matchSucc.NpcConfigs;
        if (npcsConfirm)
        {
            room.UnconfirmedNpcUids.UnionWith(matchSucc.NpcConfigs.Keys);
        }

        var deadline = DateTimeOffset.FromUnixTimeSeconds(detail.SuccTimeStamp).AddMilliseconds(detail.ConfirmTimeoutMs);
        return outcome
            .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidMatchRsp, matchSucc.MatchRsp)
            .AwaitConfirm(new MatchConfirmWindow(room.Ticket, room.MapId, deadline, [.. room.UnconfirmedNpcUids]));
    }

    private RoomOutcome CompleteMatchIfConfirmed(MatchRoom room, RoomOutcome outcome)
    {
        if (!room.IsMatchConfirmed)
        {
            return outcome;
        }

        outcome.PushAll(
            MemberUids(room),
            PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf,
            RoomNotices.Dismiss(room, PB_ENUM_ENUM_ROOM_DISMISS_REASON.PbEnumRoomDismissReasonGamestart));
        Dissolve(room);
        if (room.PveChallengeSelectNode is null)
        {
            var plan = new SettleRoomPlan(room);
            foreach (var member in room.Members)
            {
                settleRoomsByUid[member.Uid] = plan;
            }
        }

        return outcome.StartGame(room);
    }

    private pbCOMDT_ROOMMEMBER_DT[] BuildMatchMembers(MatchRoom room) =>
    [
        .. room.Members
            .OrderBy(member => member.PosOfCamp)
            .Select((member, index) => BuildMatchMemberDT(member, RoomMemberDetail.FirstAnonymousUserNameID + index))
            .OfType<pbCOMDT_ROOMMEMBER_DT>(),
    ];

    private static void ChangeState(
        MatchRoom room,
        PB_ENUM_COM_ROOMSVR_ROOM_STATE state,
        IEnumerable<ulong> uids,
        RoomOutcome outcome)
    {
        var oldState = room.State;
        room.State = state;
        outcome.PushAll(uids, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.StateChg(room, oldState));
    }

    private long NextSecond() =>
        (clock.GetUtcNow().ToUnixTimeMilliseconds() + MillisecondsPerSecond - 1) / MillisecondsPerSecond;

    private void Dissolve(MatchRoom room)
    {
        foreach (var member in room.Members)
        {
            roomsByUid.Remove(member.Uid);
        }

        roomsById.Remove(room.RoomID);
        roomsByShowId.Remove(room.RoomShowId);
    }

    private static pbCOMDT_CHAT_PLAYER_INFO BuildRoomChatSender(LMNRoleInfo role) => new()
    {
        Uid = role.GameUlongUID,
        Name = role.InGameUsrName,
        TmpNameList = new pbCOMDT_TMP_NAME_LIST(),
    };

    private bool IsSelectableNode(LMNRoleInfo role, int mapId, pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE selectNode) =>
        IsNodeOnMap(mapId, selectNode)
        && pveChallenge.FindRoute(selectNode.StageID, selectNode.NodeID, selectNode.DifficultyLevel) is { } route
        && pveChallenge.IsUnlocked(role, route);

    private bool IsNodeOnMap(int mapId, pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE selectNode) =>
        pveChallenge.FindRoute(selectNode.StageID, selectNode.NodeID, selectNode.DifficultyLevel)?.SceneId == mapId;

    private RoomOutcome? FindMasterRoom(ulong uid, out MatchRoom room)
    {
        if (!roomsByUid.TryGetValue(uid, out room!))
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom);
        }

        return room.MasterUid == uid ? null : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMasterOperInvalid);
    }

    private RoomOutcome? FindIdleMasterRoom(ulong uid, out MatchRoom room) =>
        FindMasterRoom(uid, out room)
        ?? (room.IsMatching ? RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMatching) : null);

    private pbSCJoinRoomNtf BuildJoinRoomNtf(MatchRoom room, ulong selfUid, int fromSystemType, int joinRoomType)
    {
        var allMemInfo = new pbCOMDT_ROOMMEMBER_INFO();
        foreach (var camp in room.ListedCamps())
        {
            var campMembers = new pbCOMDT_ROOMMEMBER_CAMP { CampClient = camp };
            campMembers.MemList.AddRange(room.Members
                .Where(member => member.CampClient == camp)
                .OrderBy(member => member.PosOfCamp)
                .Select(BuildRoomMemberDT)
                .OfType<pbCOMDT_ROOMMEMBER_DT>());
            allMemInfo.CampMemList.Add(campMembers);
        }

        return new pbSCJoinRoomNtf
        {
            ResultInfo = new pbResultInfo(),
            JoinRoomType = joinRoomType,
            FromSystemType = fromSystemType,
            RoomInfo = new pbCOMDT_ROOM_INFO
            {
                RoomEntity = MatchRoom.RoomEntity,
                RoomID = room.RoomID,
                RoomSeq = room.RoomSeq,
                SelfUid = selfUid,
                SelfGameEntity = MatchRoom.GameEntity,
                RoomMaster = RoomNotices.BuildMaster(room),
                RoomAttr = new pbCOMDT_ROOM_ATTR
                {
                    RoomType = (int)room.RoomType,
                    MapMode = room.MapMode,
                    MapSubMode = room.MapSubMode,
                    MapId = room.MapId,
                    Feature = BattleVersion.Build(),
                    ManMachine = room.ManMachine,
                    RankedTeamMemberNumType = room.RankedTeamMemberNumType,
                    FriendRoomRule = new pbCOMDT_FRIENDLY_ROOM_RULE_INFO(),
                    LegendPokemonInfo = new pbCOMDT_LEGEND_POKEMON_INFO(),
                    PveChallengeSelectNode = room.PveChallengeSelectNode?.Clone() ?? new pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE(),
                },
                AllMemInfo = allMemInfo,
                CanUseMap = BuildCanUseMap(room),
                GVoiceInfo = gVoice.BuildGVoiceInfo(),
            },
            AcntLocalSvrInfo = new pbCOMDT_LOCAL_SVR_ACNT_INFO(),
        };
    }

    private pbCOMDT_ROOMMEMBER_DT? BuildRoomMemberDT(RoomMember member) =>
        roles.Find(member.Uid) is { } role
            ? RoomMemberDetail.BuildRoomMember(gameData, role, member, BuildRankedDivision(role))
            : null;

    private pbCOMDT_ROOMMEMBER_DT? BuildMatchMemberDT(RoomMember member, int anonymousUserNameID) =>
        roles.Find(member.Uid) is { } role
            ? RoomMemberDetail.BuildMatchMember(
                gameData,
                role,
                member,
                anonymousUserNameID,
                PveMatch.BuildTitleNameList(gameData, anonymousUserNameID))
            : null;

    private pbCOMDT_RANKED_DIVISION BuildRankedDivision(LMNRoleInfo role) =>
        RankedMatch.IsRankedMatchUnlocked(gameData, notices, role)
            ? new pbCOMDT_RANKED_DIVISION
            {
                RankedDivision = (int)RankedMatch.StartingRankedDivision,
                SeasonId = RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId,
            }
            : new pbCOMDT_RANKED_DIVISION();

    private pbCOMDT_ROOMMAP_INFO BuildCanUseMap(MatchRoom room) =>
        RoomMaps.BuildCanUseMap(gameData, room.MapSubMode, room.CanUseMap());

    private RoomOutcome PushCanUseMapChange(
        MatchRoom room,
        pbCOMDT_ROOMMAP_INFO before,
        RoomOutcome outcome,
        IEnumerable<ulong> uids)
    {
        var after = BuildCanUseMap(room);
        return before.MapList.SequenceEqual(after.MapList)
            ? outcome
            : outcome.PushAll(uids, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.CanUseMapChg(room, after));
    }

    private static HashSet<int> UsableMapsOf(pbCOMDT_ACNTMAP_INFO acntMapInfo) =>
        [.. acntMapInfo.MapList.Where(map => map.CanUseType == RoomOptions.CanUseMap).Select(map => map.MapId)];

    private void ApplyMap(MatchRoom room, pbScene_Map scene)
    {
        room.MapId = scene.SceneId;
        room.MapMode = scene.MapMode;
        room.MapSubMode = scene.MapSubMode;
        room.CampPlayerNum = scene.CampPlayerNum;
        if (room.PveChallengeSelectNode is { } selectNode && !IsNodeOnMap(scene.SceneId, selectNode))
        {
            room.PveChallengeSelectNode = null;
        }
    }

    private static int CapacityAfter(MatchRoom room, pbScene_Map scene, int campClient) =>
        campClient == MatchRoom.JudgeCamp ? room.CapacityOf(campClient) : scene.CampPlayerNum;

    private static bool IsRanked(MatchRoom room) =>
        room.MapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank;

    private static IEnumerable<ulong> MemberUids(MatchRoom room) => room.Members.Select(member => member.Uid).ToArray();

    private int AllocateShowId()
    {
        int showId;
        do
        {
            showId = Random.Shared.Next(MinRoomShowId, MaxRoomShowId);
        }
        while (roomsByShowId.ContainsKey(showId));

        return showId;
    }
}
