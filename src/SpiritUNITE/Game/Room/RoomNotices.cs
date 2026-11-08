using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public static class RoomNotices
{
    public const int NotReady = 0;

    private const int ClientDefaultRoomDelayMs = 80;
    private const int MinRoomDelayMs = 1;
    private const int JoinRoomTypeNormal = (int)PB_ENUM_ENM_JOIN_ROOM_TYPE.PbEnmJoinRoomTypeNormal;

    public static pbSCJoinRoomNtf JoinRefused(PB_ENUM_RES_ERROR_CODE result) => new()
    {
        ResultInfo = new pbResultInfo { ResultId = (int)result },
        JoinRoomType = JoinRoomTypeNormal,
        AcntLocalSvrInfo = new pbCOMDT_LOCAL_SVR_ACNT_INFO(),
    };

    public static pbSCJoinRoomNtf JoinBanned(long banEndTime) => new()
    {
        ResultInfo = new pbResultInfo
        {
            ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrInBannedForRejectMatchConfirm,
            Args = new pbArgList { Args = { new pbArgData { Value = new pbArgDataValue { IntValue = (int)banEndTime } } } },
        },
        AcntLocalSvrInfo = new pbCOMDT_LOCAL_SVR_ACNT_INFO(),
    };

    public static pbSCRoomRttNtf RoomRtt(GVoiceOptions gVoice, TimeSpan? lobbyRtt) => new()
    {
        Rtt = lobbyRtt is { } rtt ? Math.Max(MinRoomDelayMs, (int)Math.Ceiling(rtt.TotalMilliseconds)) : ClientDefaultRoomDelayMs,
        IdcTag = gVoice.IdcTag,
    };

    public static pbSCRoomDailyActivityRewardNtf DailyActivityReward(pbCSDT_ROOM_DAILY_ACTIVITY_INFO dailyActivityInfo) => new()
    {
        DailyActivityInfo = dailyActivityInfo,
    };

    public static pbSCRoomChgNtf ChgNtf(MatchRoom room, PB_ENUM_COM_ROOMCHGTYPE chgType, pbCOMDT_ROOMCHG_DT? chgInfo) => new()
    {
        RoomID = room.RoomID,
        RoomChgInfo = new pbCOMDT_ROOMCHGNTF_INFO { ChgType = (int)chgType, ChgInfo = chgInfo },
    };

    public static pbSCRoomChgNtf ShowIdChg(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgGenshowid, new pbCOMDT_ROOMCHG_DT
        {
            RoomShowId = new pbCOMDT_ROOMCHG_SHOWID { RoomShowId = room.RoomShowId },
        });

    public static pbSCRoomChgNtf LegendPokemonChg(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgLegendPokemonChg, new pbCOMDT_ROOMCHG_DT
        {
            LegendPokemonChg = new pbCOMDT_ROOMCHG_LEGEND_POKEMON_CHG { LegendPokemonInfo = new pbCOMDT_LEGEND_POKEMON_INFO() },
        });

    public static pbSCRoomChgNtf PlayerAdd(MatchRoom room, RoomMember member, pbCOMDT_ROOMMEMBER_DT memberDT) =>
        PlayerAdd(room, member.CampClient, memberDT);

    public static pbSCRoomChgNtf PlayerAdd(MatchRoom room, int campClient, pbCOMDT_ROOMMEMBER_DT memberDT) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgPlayeradd, new pbCOMDT_ROOMCHG_DT
        {
            PlayerAdd = new pbCOMDT_ROOMCHG_PLAYERADD
            {
                Camp = CampIndexOf(campClient),
                CampClient = campClient,
                MemInfo = memberDT,
            },
        });

    public static pbSCFriendlyRoomRuleInfoNtf FriendlyRoomRuleInfo(MatchRoom room) => new()
    {
        RoomRuleInfo = room.FriendRoomRule.Build(),
    };

    public static pbSCChatMsgNtf RoomChat(pbCOMDT_CHAT_PLAYER_INFO from, int roomChatType) => new()
    {
        ChatMsg =
        {
            new pbCOMDT_CHAT_MSG
            {
                Type = (int)PB_ENUM_RES_CHAT_MSG_TYPE.PbResChatMsgTypeRoom,
                ChatInfo = new pbCOMDT_CHAT_MSG_UNION
                {
                    Room = new pbCOMDT_CHAT_MSG_ROOM { From = from, Type = roomChatType },
                },
            },
        },
    };

    public static pbSCRoomChgNtf PlayerLeave(MatchRoom room, int campClient, int posOfCamp) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgPlayerleave, new pbCOMDT_ROOMCHG_DT
        {
            PlayerLeave = new pbCOMDT_ROOMCHG_PLAYERLEAVE
            {
                Camp = CampIndexOf(campClient),
                Pos = posOfCamp,
                CampClient = campClient,
            },
        });

    public static pbSCRoomChgNtf Dismiss(MatchRoom room, PB_ENUM_ENUM_ROOM_DISMISS_REASON dismissReason) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgDismiss, new pbCOMDT_ROOMCHG_DT
        {
            DismissChg = new pbCOMDT_ROOMCHG_DISMISS { DismissReason = (int)dismissReason },
        });

    public static pbSCRoomChgNtf SelfBeKick(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgSelfbekick, null);

    public static pbSCRoomChgNtf StateChg(MatchRoom room, PB_ENUM_COM_ROOMSVR_ROOM_STATE oldState) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgStatechg, new pbCOMDT_ROOMCHG_DT
        {
            StateChg = new pbCOMDT_ROOMCHG_STATECHG { OldState = (int)oldState, NewState = (int)room.State },
        });

    public static pbSCRoomChgNtf MemberInfoChg(MatchRoom room, pbCOMDT_ROOMMEMBER_OF_ACNT memberOfAcnt) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgMemberinfochg, new pbCOMDT_ROOMCHG_DT
        {
            MemberInfoChg = new pbCOMDT_ROOMCHG_MEMBERINFOCHG
            {
                MemberUid = memberOfAcnt.Uid,
                RoleAvatarInfo = memberOfAcnt.RoleAvatarInfo,
                RankedDivision = memberOfAcnt.RankedDivision,
                RoleName = memberOfAcnt.MemberName,
                TmpNameList = memberOfAcnt.TmpNameList,
                BattleMemberConfigList = memberOfAcnt.BattleMemberConfigList,
            },
        });

    public static pbSCRoomChgNtf MasterChg(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgMasterchg, new pbCOMDT_ROOMCHG_DT
        {
            MasterChg = new pbCOMDT_ROOMCHG_MASTERCHG
            {
                NewMaster = BuildMaster(room),
                ReadyStatus = (int)room.State,
            },
        });

    public static pbSCRoomChgNtf MemberPos(
        pbCOMDT_CHGMEMBERPOS_DATA memberSrc,
        pbCOMDT_CHGMEMBERPOS_DATA memberDst,
        PB_ENUM_COM_CHGROOMPOS_TYPE chgType,
        MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgChgmemberpos, new pbCOMDT_ROOMCHG_DT
        {
            ChgMemberPos = new pbCOMDT_ROOMCHG_CHGMEMBERPOS
            {
                MemberSrc = memberSrc,
                MemberDst = memberDst,
                ChgType = (int)chgType,
            },
        });

    public static pbCOMDT_CHGMEMBERPOS_DATA PosData(RoomMember member, int oldCampClient, int oldPosOfCamp) => new()
    {
        MemberUid = member.Uid,
        MemberGameEntity = MatchRoom.GameEntity,
        OldCamp = CampIndexOf(oldCampClient),
        OldPosOfCamp = oldPosOfCamp,
        OldCampClient = oldCampClient,
        NewCamp = CampIndexOf(member.CampClient),
        NewPosOfCamp = member.PosOfCamp,
        NewCampClient = member.CampClient,
        ReadyStatus = NotReady,
    };

    public static pbSCRoomChgNtf MapChg(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgMapchg, new pbCOMDT_ROOMCHG_DT
        {
            MapChg = new pbCOMDT_ROOMCHG_MAPCHG
            {
                MapId = room.MapId,
                RoomRule = room.FriendRoomRule.Build(),
                ManMachine = room.ManMachine,
            },
        });

    public static pbSCRoomChgNtf TrainerLevelChg(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgTrainerlevelChg, new pbCOMDT_ROOMCHG_DT
        {
            TrainerLevelChg = new pbCOMDT_ROOMCHG_TRAINER_LEVEL { TrainerLevel = room.TrainerDifficultyLevel },
        });

    public static pbSCRoomChgNtf RoomTypeChg(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgRoomtypechg, new pbCOMDT_ROOMCHG_DT
        {
            RoomTypeChg = new pbCOMDT_ROOMCHG_ROOMTYPECHG { RoomType = (int)room.RoomType, ManMachine = room.ManMachine },
        });

    public static pbSCRoomChgNtf CanUseMapChg(MatchRoom room, pbCOMDT_ROOMMAP_INFO canUseMap) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgCanusemapchg, new pbCOMDT_ROOMCHG_DT
        {
            CanUseMapChg = new pbCOMDT_ROOMCHG_CANUSEMAPCHG { CanUseMap = canUseMap },
        });

    public static pbSCRoomChgNtf SubModeChg(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgSubmodechg, new pbCOMDT_ROOMCHG_DT
        {
            SubModeChg = new pbCOMDT_ROOMCHG_SUBMODECHG { SubMode = room.MapSubMode },
        });

    public static pbSCRoomChgNtf RankedTeamMemberNumTypeChg(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgRankedteammembernumtypechg, new pbCOMDT_ROOMCHG_DT
        {
            RankedTeamMemberNumTypeChg = new pbCOMDT_ROOMCHG_RANKEDTEAMMEMBERNUMTYPECHG
            {
                RankedTeamMemberNumType = room.RankedTeamMemberNumType,
            },
        });

    public static pbSCMatchRsp MatchProcess(MatchRoom room, int waitTime, int confirmTimeoutMs) => new()
    {
        MatchResult = new pbCOMDT_MATCH_RESULT
        {
            Result = (int)PB_ENUM_COM_MATCH_RESULT.PbComMatchProcess,
            MatchResDetail = new pbCOMDT_MATCH_DETAIL
            {
                MatchSucc = new pbCOMDT_MATCH_SUCC_DETAIL { ConfirmTimeoutMs = confirmTimeoutMs },
                MatchProcess = new pbCOMDT_MATCH_PRCESS_DETAIL
                {
                    RoomEntity = MatchRoom.MatchRoomEntity,
                    MapId = room.MapId,
                    WaitTime = waitTime,
                    ProcTimeStamp = room.MatchStartedAt,
                },
            },
        },
    };

    public static pbSCRoomConfirmRsp HumanConfirmed(ulong uid) => new()
    {
        Uid = uid,
        FromGameEntity = MatchRoom.GameEntity,
        ResultInfo = new pbResultInfo(),
    };

    public static pbSCRoomConfirmRsp NpcConfirmed(ulong npcUid) => new()
    {
        Uid = npcUid,
        ResultInfo = new pbResultInfo(),
    };

    public static pbSCMultGameAbortNtf MatchAbort(PB_ENUM_RES_ERROR_CODE result) => new()
    {
        ResultInfo = new pbResultInfo { ResultId = (int)result },
    };

    public static pbSCCancelMatchNtf CancelMatch(PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON reason, string acntName) =>
        EmptyChildren.Fill(new pbSCCancelMatchNtf
        {
            Detail = new pbCOMDT_MATCHTEAM_DESTROY
            {
                Reason = (int)reason,
                Detail = reason == PB_ENUM_ENM_MATCHTEAM_DESTORY_REASON.PbEnmMatchteamDestoryReasonAcntleave
                    ? new pbCOMDT_MATCHTEAM_DESTROY_REASON
                    {
                        AcntLeave = new pbCOMDT_MATCHTEAM_DESTORY_ACNTLEAVE { LeaveAcntName = acntName },
                    }
                    : new pbCOMDT_MATCHTEAM_DESTROY_REASON
                    {
                        AcntCancel = new pbCOMDT_MATCHTEAM_DESTORY_ACNTCANCEL { CancelAcntName = acntName },
                    },
            },
        });

    public static pbSCRoomChgPosNtf ChgPosRequest(MatchRoom room, RoomMember sender, RoomMember receiver, int timeOutSec) =>
        new()
        {
            ResultInfo = new pbResultInfo(),
            ChgPosData = new pbCOMDT_ROOMCHGPOS_DATA
            {
                Sender = Brief(sender),
                Receiver = Brief(receiver),
                ChgSeq = room.PendingPosChange?.ChgSeq ?? 0,
                TimeOutSec = timeOutSec,
            },
        };

    public static pbSCRoomChgPosNtf ChgPosRefused(PB_ENUM_RES_ERROR_CODE result) => EmptyChildren.Fill(new pbSCRoomChgPosNtf
    {
        ResultInfo = new pbResultInfo { ResultId = (int)result },
    });

    public static pbCOMDT_ROOM_MASTER BuildMaster(MatchRoom room) => new()
    {
        MasterUid = room.MasterUid,
        MasterGameEntity = MatchRoom.GameEntity,
    };

    public static int CampIndexOf(int campClient) => campClient switch
    {
        MatchRoom.LeftCamp => (int)PB_ENUM_COM_PLAYERCAMP_INDEX.PbComPlayercampIdx1,
        MatchRoom.RightCamp => (int)PB_ENUM_COM_PLAYERCAMP_INDEX.PbComPlayercampIdx2,
        _ => (int)PB_ENUM_COM_PLAYERCAMP_INDEX.PbComPlayercampIdxMid,
    };

    private static pbCOMDT_ROOMMEMBER_BRIF Brief(RoomMember member) => new()
    {
        Camp = CampIndexOf(member.CampClient),
        PosOfCamp = member.PosOfCamp,
        CampClient = member.CampClient,
    };
}
