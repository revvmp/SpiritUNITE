using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public static class RoomNotices
{
    public const int NotReady = 0;

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
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgPlayeradd, new pbCOMDT_ROOMCHG_DT
        {
            PlayerAdd = new pbCOMDT_ROOMCHG_PLAYERADD
            {
                Camp = CampIndexOf(member.CampClient),
                CampClient = member.CampClient,
                MemInfo = memberDT,
            },
        });

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

    public static pbSCRoomChgNtf SelfBeKick(MatchRoom room) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgSelfbekick, null);

    public static pbSCRoomChgNtf StateChg(MatchRoom room, PB_ENUM_COM_ROOMSVR_ROOM_STATE oldState) =>
        ChgNtf(room, PB_ENUM_COM_ROOMCHGTYPE.PbComRoomchgStatechg, new pbCOMDT_ROOMCHG_DT
        {
            StateChg = new pbCOMDT_ROOMCHG_STATECHG { OldState = (int)oldState, NewState = (int)room.State },
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
        MemberGameEntity = MatchRoom.RoomEntity,
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
                RoomRule = new pbCOMDT_FRIENDLY_ROOM_RULE_INFO(),
                ManMachine = room.ManMachine,
            },
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

    public static pbSCMatchRsp MatchProcess(MatchRoom room) => new()
    {
        MatchResult = new pbCOMDT_MATCH_RESULT
        {
            Result = (int)PB_ENUM_COM_MATCH_RESULT.PbComMatchProcess,
            MatchResDetail = new pbCOMDT_MATCH_DETAIL
            {
                MatchProcess = new pbCOMDT_MATCH_PRCESS_DETAIL
                {
                    RoomEntity = MatchRoom.RoomEntity,
                    MapId = room.MapId,
                    PkAI = room.ManMachine,
                    ProcTimeStamp = room.MatchStartedAt,
                },
            },
        },
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
        MasterGameEntity = MatchRoom.RoomEntity,
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
