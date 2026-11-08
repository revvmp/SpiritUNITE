using Microsoft.Extensions.Options;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Match;
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
    pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE? PveChallengeSelectNode = null,
    bool IsSubModeCompetition = false,
    pbCOMDT_TEN_GAMES_TEAM_BRIEF_INFO? TenGamesTeamInfo = null,
    int TenGamesMapID = 0);

public readonly record struct SearchWaitSamples(long TotalSeconds, int Count)
{
    public int MeanSeconds => Count == 0 ? 0 : (int)Math.Round((double)TotalSeconds / Count, MidpointRounding.AwayFromZero);

    public SearchWaitSamples Add(long waitedSeconds) => new(TotalSeconds + waitedSeconds, Count + 1);
}

public sealed class RoomManager(
    RoleInfoStore roles,
    GameData gameData,
    PveChallengeProgress pveChallenge,
    RankedManager ranked,
    WarmBattleManager warmBattles,
    LobbyNotices notices,
    LobbyRoles lobbyRoles,
    RoomMicTips micTips,
    ActivitySchedule activities,
    IOptions<RoomOptions> roomOptions,
    IOptions<MatchOptions> matchOptions,
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
    private const int HeavenTrainerGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeHeavenTrainer;
    private const int MatchTrainerOff = 0;
    private const int MatchTrainerRandom = 1;
    private const int PartyTeamFlag = 1;
    private const int SoloParty = 1;
    private const int RightCampTeamFlag = PartyTeamFlag + 1;
    private const int NoTeamFlag = 0;
    private const int CompetitionSubMode = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeCompetition;
    private const int OnlineCompetitionSubMode = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeOnlineCompetition;
    private const PB_ENUM_RES_ERROR_CODE TenGamesRoomChangeRefused = PB_ENUM_RES_ERROR_CODE.PbErrRoomCompetitionToOtherSubMode;

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
    private readonly Dictionary<int, PendingMatch> matchesById = [];
    private readonly Matchmaker matchmaker = new();
    private readonly RoomOptions options = roomOptions.Value;
    private readonly MatchOptions matching = matchOptions.Value;
    private readonly GVoiceOptions gVoice = gVoiceOptions.Value;
    private int nextRoomID = 1;
    private int nextMatchID = 1;

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

            var acntMapInfo = RoomMemberDetail.BuildAcntMapInfo(gameData, options, request.UsableMaps, clock.GetUtcNow());
            var usableMaps = UsableMapsOf(acntMapInfo);
            pbScene_Map? scene;
            if (request.TenGamesTeamInfo is not null)
            {
                scene = gameData.SceneMap.Find(request.TenGamesMapID);
                if (scene is null || (request.MapID != 0 && request.MapID != scene.SceneId))
                {
                    return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalid);
                }
            }
            else if (request.MapID != 0)
            {
                scene = gameData.SceneMap.Find(request.MapID);
                if (scene is null || !usableMaps.Contains(scene.SceneId))
                {
                    return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalid);
                }
            }
            else
            {
                var subMode = request.IsSubModeCompetition ? CompetitionSubMode
                    : request.SubMapMode != 0 ? request.SubMapMode
                    : (int)RoomMaps.DefaultSubModeOf(request.MapMode);
                if (subMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeInvalid)
                {
                    return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapModeInvalid);
                }

                scene = FindDefaultMap(subMode, usableMaps);
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

            if (scene.MapSubMode == OnlineCompetitionSubMode && request.TenGamesTeamInfo is null)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTenGamesNotTeam);
            }

            var room = new MatchRoom(nextRoomID++, clock.GetUtcNow().ToUnixTimeSeconds(), AllocateShowId(), uid);
            ApplyMap(room, scene);
            room.RoomType = RoomTypeOf(scene.MapSubMode);
            room.PveChallengeSelectNode = pveChallengeSelectNode;
            room.TenGamesTeamInfo = scene.MapSubMode == OnlineCompetitionSubMode ? request.TenGamesTeamInfo!.Clone() : null;
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
                return Join(
                    uid,
                    settleRoom,
                    finishedMember.AcntMapInfo,
                    finishedMember.Profile,
                    JoinRoomTypeSettle,
                    (finishedMember.CampClient, finishedMember.PosOfCamp));
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
                LastRankedTeamMemberNumType = IsRanked(finished) ? finished.RankedTeamMemberNumType : 0,
                FriendRoomRule = finished.FriendRoomRule,
                GlobalBanPokemonList = finished.GlobalBanPokemonList,
                TenGamesTeamInfo = finished.TenGamesTeamInfo,
            };
            foreach (var bot in finished.Bots)
            {
                room.AddBot(bot.CampClient, bot.PosOfCamp, bot.Row);
            }

            var (campClient, posOfCamp) = finished.IsCustomRoom
                ? (finishedMember.CampClient, finishedMember.PosOfCamp)
                : (MatchRoom.LeftCamp, 0);
            var settlingMember = room.Add(uid, campClient, posOfCamp, finishedMember.AcntMapInfo, finishedMember.Profile);
            settlingMember.IsSettle = true;
            settlingMember.SettlesAlone = finished.Members.Count == SoloParty;
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

            if (room.FindBotAt(campClient, posOfCamp) is { } bot)
            {
                room.RemoveBot(bot);
                return new RoomOutcome().PushAll(
                    MemberUids(room),
                    PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf,
                    RoomNotices.PlayerLeave(room, bot.CampClient, bot.PosOfCamp));
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

            if (room.IsCustomRoom)
            {
                return StartCustomGame(room);
            }

            if (room.RoomType != PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrSystemNotOpen);
            }

            if (IsRanked(room) && RefuseRankedStart(room) is { } rankedRefusal)
            {
                return RoomOutcome.Refused(rankedRefusal.Result, rankedRefusal.Args);
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

    public RoomOutcome FormMatches()
    {
        lock (gate)
        {
            var outcome = new RoomOutcome();
            foreach (var group in matchmaker.FormMatches(clock.GetUtcNow(), matching.BotFill))
            {
                OpenMatch(group, outcome);
            }

            return outcome;
        }
    }

    public RoomOutcome ConfirmNpc(MatchTicket ticket, ulong npcUid)
    {
        lock (gate)
        {
            if (!matchesById.TryGetValue(ticket.MatchID, out var match) || !match.UnconfirmedNpcUids.Remove(npcUid))
            {
                return new RoomOutcome();
            }

            var outcome = new RoomOutcome().PushAll(match.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidRoomConfirmRsp, RoomNotices.NpcConfirmed(npcUid));
            return CompleteMatchIfConfirmed(match, outcome);
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

            if (!room.IsConfirming || room.Match is not { } match || !match.ConfirmedUids.Add(uid))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomStateInvalid);
            }

            var outcome = new RoomOutcome().PushAll(match.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidRoomConfirmRsp, RoomNotices.HumanConfirmed(uid));
            return CompleteMatchIfConfirmed(match, outcome);
        }
    }

    public RoomOutcome ExpireMatchConfirm(MatchTicket ticket, out ulong[] removedUids)
    {
        lock (gate)
        {
            removedUids = [];
            if (!matchesById.Remove(ticket.MatchID, out var match))
            {
                return new RoomOutcome();
            }

            var outcome = new RoomOutcome();
            var removed = new List<ulong>();
            foreach (var room in match.Rooms)
            {
                var unconfirmed = room.Members
                    .Where(member => member.CampClient != MatchRoom.JudgeCamp && !match.ConfirmedUids.Contains(member.Uid))
                    .ToArray();
                ulong[] unconfirmedUids = [.. unconfirmed.Select(member => member.Uid)];
                removed.AddRange(unconfirmedUids);
                foreach (var member in unconfirmed)
                {
                    outcome
                        .Push(member.Uid, PB_ENUM_CS_MSGID_DEF.PbScidMultGameAbortNtf, RoomNotices.MatchAbort(PB_ENUM_RES_ERROR_CODE.PbErrRoomUnconfirm))
                        .Push(member.Uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.SelfBeKick(room));
                }

                if (unconfirmed.Length == room.Members.Count)
                {
                    Dissolve(room);
                    continue;
                }

                if (unconfirmed.Length == 0)
                {
                    Requeue(match, room, outcome);
                    continue;
                }

                var before = BuildCanUseMap(room);
                AbortConfirm(room, PB_ENUM_RES_ERROR_CODE.PbErrRoomTeamerUnconfirm, outcome, unconfirmedUids);
                foreach (var member in unconfirmed)
                {
                    room.Remove(member);
                    roomsByUid.Remove(member.Uid);
                    PushDeparture(room, member, outcome);
                }

                PushCanUseMapChange(room, before, outcome, MemberUids(room));
            }

            removedUids = [.. removed];
            return outcome;
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

            return StopMatching(room, uid, PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON.PbEnmMatchteamDestoryReasonAcntcancel, new RoomOutcome());
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

            if (room.TenGamesTeamInfo is not null)
            {
                return RoomOutcome.Refused(TenGamesRoomChangeRefused);
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

            if (room.TenGamesTeamInfo is not null)
            {
                return RoomOutcome.Refused(TenGamesRoomChangeRefused);
            }

            if (subMode == room.MapSubMode)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomSameSubMode);
            }

            var scene = FindDefaultMap(subMode, room.CanUseMap());
            if (scene is null)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomInvalidSubMode);
            }

            var roomType = RoomTypeOf(scene.MapSubMode);
            var leavesCustomRoom = room.IsCustomRoom && roomType != PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeNormal;
            if (leavesCustomRoom && room.CountIn(MatchRoom.JudgeCamp) > 0)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomFriendModeJudgeMent);
            }

            if (leavesCustomRoom
                ? room.Members.Count > scene.CampPlayerNum
                : room.CampsInUse().Any(camp => room.CountIn(camp) > CapacityAfter(room, scene, camp)))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalidToMorePlayer);
            }

            var before = BuildCanUseMap(room);
            ApplyMap(room, scene);
            var outcome = new RoomOutcome()
                .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.SubModeChg(room))
                .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.MapChg(room));
            if (roomType != room.RoomType)
            {
                ChangeRoomTypeTo(room, roomType, outcome);
            }

            return PushCanUseMapChange(room, before, outcome, MemberUids(room));
        }
    }

    public RoomOutcome ChangeHeavenTrainerLevel(ulong uid, int mapId, int level)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (mapId != room.MapId || gameData.SceneMap.Find(mapId)?.GameType != HeavenTrainerGameType)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapInvalid);
            }

            if (!IsMatchTrainerLevel(level))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
            }

            if (roles.Find(uid) is not { } role || !HasUnlockedTrainerLevel(role.GetHeavenTrainerRecords(), mapId, level))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapLevelLimit);
            }

            room.HeavenDifficultyLevel = level;
            return new RoomOutcome();
        }
    }

    public RoomOutcome ChangeMatchTrainer(ulong uid, int matchTrainer, DateTimeOffset now)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (activities.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeMatchTrainer, now) is null)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrMatchTrainerActivityNotExit);
            }

            if (matchTrainer is not (MatchTrainerOff or MatchTrainerRandom))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
            }

            room.MatchTrainer = matchTrainer;
            return new RoomOutcome();
        }
    }

    public RoomOutcome ChangeTrainerLevel(ulong uid, int level, DateTimeOffset now)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (activities.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeMatchTrainer, now) is null)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrMatchTrainerActivityNotExit);
            }

            if (!IsMatchTrainerLevel(level) || gameData.SceneMap.Find(room.MapId) is not { ChallengeTrainerMode: not (int)PB_ENUM_RES_MATCH_TRAINER_MODE.PbResMatchTrainerNone })
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
            }

            if (!HasUnlockedTrainerLevel([], room.MapId, level))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMapLevelLimit);
            }

            room.TrainerDifficultyLevel = level;
            return new RoomOutcome()
                .PushAll(MemberUids(room).Where(member => member != uid), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.TrainerLevelChg(room));
        }
    }

    public int HeavenDifficultyLevelOf(ulong uid) => FindRoomOf(uid)?.HeavenDifficultyLevel ?? 0;

    public int TrainerDifficultyLevelOf(ulong uid) => FindRoomOf(uid)?.TrainerDifficultyLevel ?? 0;

    public int MatchTrainerOf(ulong uid) => FindRoomOf(uid)?.MatchTrainer ?? 0;

    private static bool IsMatchTrainerLevel(int level) =>
        level is >= (int)PB_ENUM_RES_MATCH_TRAINER_LEVEL.PbMatchTrainerLevelLow and < (int)PB_ENUM_RES_MATCH_TRAINER_LEVEL.PbMatchTrainerLevelMax;

    private bool HasUnlockedTrainerLevel(IEnumerable<ChallengeTrainerRecord> records, int mapId, int level)
    {
        var needCnt = gameData.MatchTrainer.Find(pbMatchTrainer.Key(mapId, level))?.NeedCnt ?? 0;
        var unlockingLevel = Math.Max((int)PB_ENUM_RES_MATCH_TRAINER_LEVEL.PbMatchTrainerLevelLow, level - 1);
        var challengeCnt = records.FirstOrDefault(record => record.MapId == mapId && record.LevelType == unlockingLevel)?.ChallengeCnt ?? 0;
        return needCnt <= challengeCnt;
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

            if (room.TenGamesTeamInfo is not null)
            {
                return RoomOutcome.Refused(TenGamesRoomChangeRefused);
            }

            if (roomType is not ((int)PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch or (int)PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeNormal)
                || manMachine is not (0 or 1))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomTypeInvalid);
            }

            var newType = (PB_ENUM_COM_ROOM_TYPE)roomType;
            if (newType != RoomTypeOf(room.MapSubMode) || (room.IsCustomRoom && manMachine == CpuMatch.ManMachine))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomTypeInvalid);
            }

            var outcome = new RoomOutcome()
                .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.LegendPokemonChg(room));
            room.ManMachine = manMachine;
            room.PendingPosChange = null;
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

            if (room.FindBotAt(campClient, posOfCamp) is not null)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomChgposFail);
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

    public RoomOutcome AddRoomAI(ulong uid, int campClient)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (!room.IsCustomRoom)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomTypeInvalid);
            }

            if (campClient is not (MatchRoom.LeftCamp or MatchRoom.RightCamp))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomCampInvalid);
            }

            pbAiInfoConfig[] freeRows =
            [
                .. gameData.AiInfoConfig.Rows
                    .Where(row => row.NPCType == PveMatch.GenericNpcType && room.Bots.All(bot => bot.Row.ID != row.ID)),
            ];
            if (room.FindFreePosIn(campClient) is not { } posOfCamp || freeRows.Length == 0)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMemberFull);
            }

            var bot = room.AddBot(campClient, posOfCamp, freeRows[Random.Shared.Next(freeRows.Length)]);
            return new RoomOutcome().PushAll(
                MemberUids(room),
                PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf,
                RoomNotices.PlayerAdd(room, campClient, BuildRoomBotDT(bot)));
        }
    }

    public RoomOutcome UpdateFriendlyRoomRule(ulong uid, IEnumerable<int> ruleList)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (!room.IsCustomRoom || gameData.SceneMap.Find(room.MapId) is not { } scene)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomTypeInvalid);
            }

            if (FriendlyRoomRules.Link(gameData, scene, ruleList) is not { } rules)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
            }

            room.FriendRoomRule = rules;
            return new RoomOutcome().PushAll(
                MemberUids(room),
                PB_ENUM_CS_MSGID_DEF.PbScidFriendlyRoomRuleInfoNtf,
                RoomNotices.FriendlyRoomRuleInfo(room));
        }
    }

    public IReadOnlyList<int>? FindRoomGlobalBanList(ulong uid)
    {
        lock (gate)
        {
            return roomsByUid.TryGetValue(uid, out var room) && room.IsCustomRoom ? room.GlobalBanPokemonList : null;
        }
    }

    public RoomOutcome UpdateRoomGlobalBanList(ulong uid, IEnumerable<int> banPokemonList)
    {
        lock (gate)
        {
            if (FindIdleMasterRoom(uid, out var room) is { } refusal)
            {
                return refusal;
            }

            if (!room.IsCustomRoom)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomTypeInvalid);
            }

            int[] requested = [.. banPokemonList.Distinct()];
            if (requested.Any(pokemonId => gameData.PokemonBase.Find(pokemonId) is not { MainUnitId: > 0 }))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
            }

            if (requested.Order().SequenceEqual(room.GlobalBanPokemonList.Order()))
            {
                return new RoomOutcome();
            }

            var now = clock.GetUtcNow();
            var unlocksAt = room.GlobalBanPokemonListChangedAt + TimeSpan.FromSeconds(RoomBanPokemonListChgCd);
            if (now < unlocksAt)
            {
                var waitSeconds = (int)Math.Ceiling((unlocksAt - now).TotalSeconds);
                return RoomOutcome.Refused(
                    PB_ENUM_RES_ERROR_CODE.PbErrRoomGlobalBanChgCd,
                    new pbArgList { Args = { new pbArgData { Value = new pbArgDataValue { IntValue = waitSeconds } } } });
            }

            room.GlobalBanPokemonList = requested;
            room.GlobalBanPokemonListChangedAt = now;
            return new RoomOutcome().PushAll(
                MemberUids(room).Where(memberUid => memberUid != uid),
                PB_ENUM_CS_MSGID_DEF.PbScidUpdateRoomGlobalBanListNtf,
                new pbSCUpdateRoomGlobalBanListNtf
                {
                    ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrRoomGlobalBanPokemonChg },
                });
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
            room.FindMember(uid)!.AcntMapInfo = RoomMemberDetail.BuildAcntMapInfo(gameData, options, usableMaps, clock.GetUtcNow());
            return PushCanUseMapChange(room, before, new RoomOutcome(), MemberUids(room));
        }
    }

    public RoomOutcome ChangeTmpNameList(ulong uid, pbCOMDT_TMP_NAME_LIST tmpNameList)
    {
        lock (gate)
        {
            if (!roomsByUid.TryGetValue(uid, out var room) || room.FindMember(uid) is not { } member)
            {
                return new RoomOutcome();
            }

            member.Profile = member.Profile with { TmpNameList = tmpNameList };
            return BuildRoomMemberDT(member) is { } memberDT
                ? new RoomOutcome().PushAll(
                    MemberUids(room),
                    PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf,
                    RoomNotices.MemberInfoChg(room, memberDT.MemberDetail.MemberOfAcnt))
                : new RoomOutcome();
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
        Join(uid, room, RoomMemberDetail.BuildAcntMapInfo(gameData, options, usableMaps, clock.GetUtcNow()), profile, JoinRoomTypeNormal);

    private RoomOutcome Join(
        ulong uid,
        MatchRoom room,
        pbCOMDT_ACNTMAP_INFO acntMapInfo,
        RoomMemberProfile profile,
        int joinRoomType,
        (int CampClient, int PosOfCamp)? preferredSeat = null)
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

        if (room.TenGamesTeamInfo is { } tenGamesTeamInfo && tenGamesTeamInfo.TeamID != profile.TenGamesTeamID)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTenGamesJoinRoomNotTeamMember);
        }

        if (IsRanked(room) && room.Members.Count >= room.RankedTeamMemberNumType)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMemberFull);
        }

        var seat = preferredSeat is { } preferred
            && room.CampsInUse().Contains(preferred.CampClient)
            && preferred.PosOfCamp < room.CapacityOf(preferred.CampClient)
            && room.IsSeatFree(preferred.CampClient, preferred.PosOfCamp)
                ? preferred
                : room.FindFreeSeat();
        if (seat is null)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMemberFull);
        }

        var (campClient, posOfCamp) = seat.Value;

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
            .PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidChatMsgNtf, RoomNotices.RoomChat(BuildRoomChatSender(role, member), RoomChatEnter))
            .PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.PlayerAdd(room, member, BuildRoomMemberDT(member)!))
            .PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.LegendPokemonChg(room));
        foreach (var other in others)
        {
            outcome.Push(other, PB_ENUM_CS_MSGID_DEF.PbScidRoomRttNtf, RoomNotices.RoomRtt(gVoice, lobbyRoles.Rtt(other)));
        }

        if (joinRoomType != JoinRoomTypeSettle)
        {
            foreach (var other in others)
            {
                outcome.Push(other, PB_ENUM_CS_MSGID_DEF.PbScidRoomMicTipsInfoNtf, micTips.BuildInfoNtf(other));
            }

            outcome
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomRttNtf, RoomNotices.RoomRtt(gVoice, lobbyRoles.Rtt(uid)))
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomMicTipsInfoNtf, micTips.BuildInfoNtf(uid));
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
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidRoomMicTipsInfoNtf, micTips.BuildInfoNtf(uid))
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
        var confirmingMatch = room.IsConfirming ? room.Match : null;
        room.Remove(member);
        roomsByUid.Remove(member.Uid);
        var outcome = new RoomOutcome();
        if (wasSearching)
        {
            matchmaker.Remove(room);
        }

        if (confirmingMatch is not null)
        {
            RequeueOthers(confirmingMatch, room, outcome);
        }

        if (room.Members.Count == 0)
        {
            roomsById.Remove(room.RoomID);
            roomsByShowId.Remove(room.RoomShowId);
            room.ClearMatch();
            return outcome;
        }

        if (wasSearching)
        {
            StopMatching(room, member.Uid, reason, outcome);
        }

        if (confirmingMatch is not null)
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

    private RoomOutcome StopMatching(MatchRoom room, ulong byUid, PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON reason, RoomOutcome outcome)
    {
        matchmaker.Remove(room);
        room.ClearMatch();
        var name = roles.Find(byUid)?.InGameUsrName ?? string.Empty;
        var others = MemberUids(room).Where(uid => uid != byUid).ToArray();
        outcome.PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidCancelMatchNtf, RoomNotices.CancelMatch(reason, name));
        ChangeState(room, PB_ENUM_COM_ROOMSVR_ROOM_STATE.PbComRoomsvrRoomStateWait, MemberUids(room), outcome);
        return outcome;
    }

    private void RequeueOthers(PendingMatch match, MatchRoom leavingRoom, RoomOutcome outcome)
    {
        matchesById.Remove(match.Ticket.MatchID);
        foreach (var room in match.Rooms.Where(room => room != leavingRoom))
        {
            Requeue(match, room, outcome);
        }
    }

    private void Requeue(PendingMatch match, MatchRoom room, RoomOutcome outcome)
    {
        var entry = match.QueueEntryOf(room)!;
        room.Match = null;
        matchmaker.Enqueue(entry);
        outcome
            .PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidMatchRsp, RoomNotices.MatchProcess(room, EstimateSearchWait(room), ConfirmTimeoutOf(room)))
            .SearchFor(new MatchSearch(entry.FillAt));
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
        var warmBattleType = WarmBattleTypeOf(room);
        var isWarmBattle = warmBattleType != MatchGroup.NoWarmBattle;
        var now = clock.GetUtcNow();
        var entry = new MatchQueueEntry(room, now, now + SearchDelayOf(room, isWarmBattle), warmBattleType, RankedBandOf(room));
        matchmaker.Enqueue(entry);
        var waitTime = isWarmBattle ? matching.DrawWarmBattleWaitTime(room.MapSubMode) ?? warmBattles.WaitTime : EstimateSearchWait(room);
        outcome.PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidMatchRsp, RoomNotices.MatchProcess(room, waitTime, ConfirmTimeoutOf(room)));
        return outcome.SearchFor(new MatchSearch(entry.FillAt));
    }

    private TimeSpan SearchDelayOf(MatchRoom room, bool isWarmBattle) =>
        isWarmBattle ? warmBattles.PickSuccDelay(room.Members.Count)
        : TimeSpan.FromSeconds(CpuMatch.IsCpuMatch(room) ? options.ManMachineSearchSec : matching.BotFillSearchSec);

    private int ConfirmTimeoutOf(MatchRoom room) => MatchConfirmTimeout.Of(gameData, room.MapSubMode);

    private int EstimateSearchWait(MatchRoom room) =>
        searchWaits.TryGetValue((room.MapId, room.Members.Count), out var samples) ? samples.MeanSeconds : NoEstimatedWaitTime;

    private void RecordSearchWait(MatchRoom room, long waitedSeconds)
    {
        var key = (room.MapId, room.Members.Count);
        searchWaits[key] = searchWaits.GetValueOrDefault(key).Add(waitedSeconds);
    }

    private int WarmBattleTypeOf(MatchRoom room) =>
        matching.BotFill
        && room.RoomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch
        && room.ManMachine != CpuMatch.ManMachine
        && gameData.SceneMap.Find(room.MapId)?.GameType == (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeCatch
            ? warmBattles.WarmBattleTypeOf(room.MapSubMode, [.. MemberUids(room)])
            : MatchGroup.NoWarmBattle;

    private RoomOutcome StartCustomGame(MatchRoom room)
    {
        int[] playerCamps = [MatchRoom.LeftCamp, MatchRoom.RightCamp];
        if (playerCamps.Any(camp => room.CountIn(camp) == 0))
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomCampMustFull);
        }

        if (playerCamps.Any(camp => room.CountIn(camp) > room.CampPlayerNum))
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomPlayerNumMoreThenMap);
        }

        if (RequiresFullCamps(room) && playerCamps.Any(camp => room.CountIn(camp) < room.CampPlayerNum))
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomCampMustFull);
        }

        var outcome = BeginMatch(room);
        var succTimeStamp = NextSecond();
        room.MatchStartedAt = succTimeStamp;
        var aiLevel = RoomAiDifficuty;
        var npcConfigs = new Dictionary<ulong, pbAiInfoConfig>();
        var memInfo = new pbCOMDT_MULTROOMMEMBER_INFO();
        var judgeCamp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.JudgeCamp };
        foreach (var judge in room.Members.Where(member => member.CampClient == MatchRoom.JudgeCamp).OrderBy(member => member.PosOfCamp))
        {
            if (BuildMatchMemberDT(judge, NoTeamFlag, RoomMemberDetail.NoAnonymousUserNameID) is { } judgeDT)
            {
                judgeCamp.MemInfo.Add(judgeDT);
            }
        }

        memInfo.CampMem.Add(judgeCamp);
        foreach (var campClient in playerCamps)
        {
            var teamFlag = campClient == MatchRoom.LeftCamp ? PartyTeamFlag : RightCampTeamFlag;
            var camp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = campClient };
            var seats = room.Members
                .Where(member => member.CampClient == campClient)
                .Select(member => (member.PosOfCamp, Member: (RoomMember?)member, Bot: (RoomBot?)null))
                .Concat(room.Bots
                    .Where(bot => bot.CampClient == campClient)
                    .Select(bot => (bot.PosOfCamp, Member: (RoomMember?)null, Bot: (RoomBot?)bot)))
                .OrderBy(seat => seat.PosOfCamp);
            foreach (var (_, member, bot) in seats)
            {
                if (bot is not null)
                {
                    npcConfigs[bot.Uid] = bot.Row;
                    camp.MemInfo.Add(RoomMemberDetail.BuildRoomBot(gameData, bot, camp.MemInfo.Count, aiLevel));
                }
                else if (member is not null && BuildMatchMemberDT(member, teamFlag, RoomMemberDetail.NoAnonymousUserNameID) is { } memberDT)
                {
                    memberDT.PosOfCamp = camp.MemInfo.Count;
                    camp.MemInfo.Add(memberDT);
                }
            }

            memInfo.CampMem.Add(camp);
        }

        var matchSucc = new BotMatchSucc(
            PveMatch.BuildMatchSuccRsp(room.MapId, aiLevel, memInfo, succTimeStamp, PveMatch.ReadyConfirmUiType, ConfirmTimeoutOf(room)),
            npcConfigs);
        return OpenConfirmWindow([room], [], matchSucc, npcsConfirm: false, outcome);
    }

    private bool RequiresFullCamps(MatchRoom room) =>
        room.MapSubMode == CompetitionSubMode
        || gameData.SceneMap.Find(room.MapId)?.PickHeroMode == (int)PB_ENUM_RES_PICK_HERO_MODE.PbResPickHeroModeBanpick;

    private int RoomAiDifficuty => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalRoomAiDifficuty);

    private int RoomBanPokemonListChgCd => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalRoomBanPokemonListChgCd);

    private void ChangeRoomTypeTo(MatchRoom room, PB_ENUM_COM_ROOM_TYPE roomType, RoomOutcome outcome)
    {
        room.RoomType = roomType;
        room.ManMachine = 0;
        room.PendingPosChange = null;
        if (roomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch)
        {
            room.FriendRoomRule = FriendlyRoomRules.None;
            foreach (var bot in room.Bots.ToArray())
            {
                room.RemoveBot(bot);
                outcome.PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.PlayerLeave(room, bot.CampClient, bot.PosOfCamp));
            }

            foreach (var member in room.Members.Where(member => member.CampClient != MatchRoom.LeftCamp).ToArray())
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

        outcome.PushAll(MemberUids(room), PB_ENUM_CS_MSGID_DEF.PbScidRoomChgNtf, RoomNotices.RoomTypeChg(room));
    }

    private static PB_ENUM_COM_ROOM_TYPE RoomTypeOf(int mapSubMode) =>
        mapSubMode is (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeFriendly or CompetitionSubMode
            ? PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeNormal
            : PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch;

    private RoomOutcome StartPveChallengeMatch(MatchRoom room)
    {
        var outcome = BeginMatch(room);
        var succTimeStamp = NextSecond();
        room.MatchStartedAt = succTimeStamp;
        var masterTrainerLevel = roles.Find(room.MasterUid) is { } master ? RoleProfile.GetLevel(gameData, master) : PveMatch.MinAILevel;
        var matchSucc = PveMatch.MatchSucc(
            gameData,
            room,
            BuildMatchMembers(room, PartyTeamFlag),
            masterTrainerLevel,
            succTimeStamp,
            ConfirmTimeoutOf(room));
        return OpenConfirmWindow([room], [], matchSucc, npcsConfirm: false, outcome);
    }

    private void OpenMatch(MatchGroup group, RoomOutcome outcome)
    {
        var lead = group.LeadRoom;
        var succTimeStamp = NextSecond();
        MatchQueueEntry[] entries = [.. group.Entries];
        var teamFlags = entries.Select((entry, index) => (entry.Room, TeamFlag: PartyTeamFlag + index)).ToDictionary();
        pbCOMDT_ROOMMEMBER_DT[] Humans(IEnumerable<MatchQueueEntry> camp) =>
            [.. camp.SelectMany(entry => BuildMatchMembers(entry.Room, teamFlags[entry.Room]))];

        var request = new MatchSuccRequest(
            lead.MapId,
            lead.CampPlayerNum,
            ConfirmTimeoutOf(lead),
            succTimeStamp,
            IsRanked(lead) ? ranked.TeamMMRScore : options.TeamMMRScore,
            new CpuMatchFillerRules(
                RankedMatch.GetRankedMatchUnlockLevel(gameData, notices),
                RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId,
                IsRanked(lead) ? ranked.FakePlayerWinPoint : null,
                IsRanked(lead) ? PartyRankedDivisions(entries) : []));
        var matchSucc = group.IsWarmBattle
            ? CpuMatch.WarmMatchSucc(gameData, request, Humans(group.LeftCamp), group.WarmBattleType)
            : CpuMatch.MatchSucc(gameData, request, new MatchCamps(
                Humans(group.LeftCamp),
                Humans(group.RightCamp),
                AgainstBots: CpuMatch.IsCpuMatch(lead),
                FillWithFakePlayers: matching.BotFill));
        if (!group.IsWarmBattle)
        {
            foreach (var entry in entries)
            {
                RecordSearchWait(entry.Room, succTimeStamp - entry.Room.MatchStartedAt);
            }
        }

        OpenConfirmWindow([.. entries.Select(entry => entry.Room)], entries, matchSucc, npcsConfirm: true, outcome);
    }

    private RoomOutcome OpenConfirmWindow(
        IReadOnlyList<MatchRoom> rooms,
        IReadOnlyList<MatchQueueEntry> queueEntries,
        BotMatchSucc matchSucc,
        bool npcsConfirm,
        RoomOutcome outcome)
    {
        var match = new PendingMatch(nextMatchID++, rooms, queueEntries, matchSucc, npcsConfirm);
        matchesById[match.Ticket.MatchID] = match;
        foreach (var room in rooms)
        {
            room.Match = match;
        }

        return outcome
            .PushAll(match.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidMatchRsp, match.MatchRsp)
            .AwaitConfirm(match.ConfirmWindow);
    }

    private RoomOutcome CompleteMatchIfConfirmed(PendingMatch match, RoomOutcome outcome)
    {
        if (!match.IsConfirmed)
        {
            return outcome;
        }

        matchesById.Remove(match.Ticket.MatchID);
        foreach (var room in match.Rooms)
        {
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
        }

        return outcome.StartGame(match);
    }

    private pbCOMDT_ROOMMEMBER_DT[] BuildMatchMembers(MatchRoom room, int teamFlag) =>
    [
        .. room.Members
            .OrderBy(member => member.PosOfCamp)
            .Select((member, index) => BuildMatchMemberDT(member, teamFlag, RoomMemberDetail.FirstAnonymousUserNameID + index))
            .OfType<pbCOMDT_ROOMMEMBER_DT>()
            .Select(memberDT => IsRanked(room) ? WithRankedDivision(memberDT) : memberDT),
    ];

    private pbCOMDT_ROOMMEMBER_DT WithRankedDivision(pbCOMDT_ROOMMEMBER_DT memberDT)
    {
        memberDT.MemberDetail.MemberOfAcnt.RankedDivision = ranked.BuildRankedDivision(memberDT.MemberDetail.MemberOfAcnt.Uid);
        return memberDT;
    }

    private RankedStartRefusal? RefuseRankedStart(MatchRoom room)
    {
        LMNRoleInfo[] members = [.. room.Members.Select(member => roles.Find(member.Uid)).OfType<LMNRoleInfo>()];
        return members.Length != room.Members.Count || roles.Find(room.MasterUid) is not { } master
            ? new RankedStartRefusal(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntInvalid)
            : ranked.RefuseStart(room.MapId, room.RankedTeamMemberNumType, master, members);
    }

    private int[] PartyRankedDivisions(IEnumerable<MatchQueueEntry> entries)
    {
        int[] placedBigs =
        [
            .. entries.SelectMany(entry => entry.Room.Members)
                .Where(member => ranked.PositionOf(member.Uid).RankedDivision != RankedLadder.UnplacedRankedDivision)
                .Select(member => ranked.BigOf(member.Uid)),
        ];
        return placedBigs.Length == 0
            ? []
            : [.. Enumerable.Range(placedBigs.Min(), placedBigs.Max() - placedBigs.Min() + 1).SelectMany(ranked.Ladder.DivisionsOfBig)];
    }

    private RankedBand? RankedBandOf(MatchRoom room)
    {
        if (!IsRanked(room))
        {
            return null;
        }

        int[] bigs = [.. room.Members.Select(member => ranked.BigOf(member.Uid))];
        var masterPoints = room.Members.All(member => ranked.MatchesByMasterPoint(member.Uid))
            ? room.Members.Select(member => ranked.PositionOf(member.Uid).MasterPoint).ToArray()
            : null;
        return new RankedBand(
            bigs.Min(),
            bigs.Max(),
            ranked.MatchDivisionBand(room.RankedTeamMemberNumType),
            masterPoints is null ? null : new MasterPointBand(masterPoints.Min(), masterPoints.Max(), ranked.MasterPointMatchBand));
    }

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

    private static pbCOMDT_CHAT_PLAYER_INFO BuildRoomChatSender(LMNRoleInfo role, RoomMember member) => new()
    {
        Uid = role.GameUlongUID,
        Name = role.InGameUsrName,
        TmpNameList = member.Profile.TmpNameList.Clone(),
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
                .Select(BuildRoomMemberDT)
                .OfType<pbCOMDT_ROOMMEMBER_DT>()
                .Concat(room.Bots.Where(bot => bot.CampClient == camp).Select(BuildRoomBotDT))
                .OrderBy(memberDT => memberDT.PosOfCamp));
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
                    LastRankedTeamMemberNumType = room.LastRankedTeamMemberNumType,
                    MatchTrainer = room.MatchTrainer,
                    TrainerDifficultyLevel = room.TrainerDifficultyLevel,
                    HeavenDifficultyLevel = room.HeavenDifficultyLevel,
                    FriendRoomRule = room.FriendRoomRule.Build(),
                    LegendPokemonInfo = new pbCOMDT_LEGEND_POKEMON_INFO(),
                    PveChallengeSelectNode = room.PveChallengeSelectNode?.Clone() ?? new pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE(),
                    TenGamesTeamInfo = room.TenGamesTeamInfo?.Clone(),
                },
                AllMemInfo = allMemInfo,
                CanUseMap = BuildCanUseMap(room),
                GVoiceInfo = gVoice.BuildGVoiceInfo(),
            },
            AcntLocalSvrInfo = new pbCOMDT_LOCAL_SVR_ACNT_INFO(),
        };
    }

    private pbCOMDT_ROOMMEMBER_DT BuildRoomBotDT(RoomBot bot) =>
        RoomMemberDetail.BuildRoomBot(gameData, bot, bot.PosOfCamp, RoomAiDifficuty);

    private pbCOMDT_ROOMMEMBER_DT? BuildRoomMemberDT(RoomMember member) =>
        roles.Find(member.Uid) is { } role
            ? RoomMemberDetail.BuildRoomMember(gameData, role, member, BuildRankedDivision(role))
            : null;

    private pbCOMDT_ROOMMEMBER_DT? BuildMatchMemberDT(RoomMember member, int teamFlag, int anonymousUserNameID) =>
        roles.Find(member.Uid) is { } role
            ? RoomMemberDetail.BuildMatchMember(
                gameData,
                role,
                member,
                teamFlag,
                anonymousUserNameID,
                BuildMatchTmpNameList(member, anonymousUserNameID))
            : null;

    private pbCOMDT_TMP_NAME_LIST BuildMatchTmpNameList(RoomMember member, int anonymousUserNameID)
    {
        var tmpNameList = PveMatch.BuildTitleNameList(gameData, anonymousUserNameID);
        tmpNameList.TmpNameInfo.Add(member.Profile.TmpNameList.TmpNameInfo.Select(tmpName => tmpName.Clone()));
        return tmpNameList;
    }

    private pbCOMDT_RANKED_DIVISION BuildRankedDivision(LMNRoleInfo role) =>
        RankedMatch.IsRankedMatchUnlocked(gameData, notices, role)
            ? ranked.BuildRankedDivision(role.GameUlongUID)
            : new pbCOMDT_RANKED_DIVISION();

    private pbCOMDT_ROOMMAP_INFO BuildCanUseMap(MatchRoom room) =>
        room.TenGamesTeamInfo is null
            ? RoomMaps.BuildCanUseMap(gameData, room.MapSubMode, room.CanUseMap())
            : new pbCOMDT_ROOMMAP_INFO { MapList = { room.MapId } };

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

    private pbScene_Map? FindDefaultMap(int mapSubMode, IReadOnlySet<int> usableMaps) =>
        mapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank
        && gameData.SceneMap.Find(ranked.CurrentMapId(clock.GetUtcNow().ToUnixTimeSeconds())) is { } openRankedMap
        && usableMaps.Contains(openRankedMap.SceneId)
            ? openRankedMap
            : RoomMaps.FindDefaultMap(gameData, mapSubMode, usableMaps);

    private static HashSet<int> UsableMapsOf(pbCOMDT_ACNTMAP_INFO acntMapInfo) =>
        [.. acntMapInfo.MapList.Where(map => map.CanUseType == RoomOptions.CanUseMap).Select(map => map.MapId)];

    private void ApplyMap(MatchRoom room, pbScene_Map scene)
    {
        room.MapId = scene.SceneId;
        room.MapMode = scene.MapMode;
        room.MapSubMode = scene.MapSubMode;
        room.CampPlayerNum = scene.CampPlayerNum;
        room.FriendRoomRule = room.FriendRoomRule.OfferedOn(scene);
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
