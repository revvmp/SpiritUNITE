using SpiritUNITE.Databin;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public static class PveMatch
{
    public const int MinAILevel = 1;
    public const int ConfirmTimeoutMs = 15000;

    private const int TrainerConfirmUiType = 2;
    private const int NpcAcntLevel = 1;
    private const int NpcMember = (int)PB_ENUM_COM_ROOM_MEMBEM_TYPE.PbComRoomMemberTnpc;
    private const int PlayerNameNpcType = (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeNormal;
    private const int TitleNpcType = (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeAnonymousUser;
    private const int TitleTmpNameType = 2;
    private const int ValidTmpName = 1;
    private const int SubscriptionHeadType = (int)PB_ENUM_ENUM_SOCIAL_HEAD_TYPE.PbEnumSocialHeadTypeSubscription;

    private static readonly int[] TeammateNpcTypes =
    [
        (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeHeavenTrainer1,
        (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeHeavenTrainer2,
        (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeHeavenTrainer3,
        (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeHeavenTrainer4,
    ];

    public static pbSCMatchRsp MatchSucc(
        GameData gameData,
        MatchRoom room,
        IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> members,
        int aiLevel,
        long succTimeStamp)
    {
        var teammateCount = Math.Max(0, room.CampPlayerNum - members.Count);
        var teammates = gameData.AiInfoConfig.Rows
            .Where(row => TeammateNpcTypes.Contains(row.NPCType))
            .OrderBy(row => row.NPCType)
            .Take(teammateCount)
            .ToArray();
        var enemies = gameData.AiInfoConfig.Rows
            .Where(row => row.NPCType is not PlayerNameNpcType and not TitleNpcType && !TeammateNpcTypes.Contains(row.NPCType))
            .OrderBy(row => row.Num)
            .Take(room.CampPlayerNum)
            .ToArray();
        var titles = gameData.AiInfoConfig.Rows
            .Where(row => row.NPCType == TitleNpcType)
            .GroupBy(row => row.Num)
            .ToDictionary(group => group.Key, group => group.First().Name);
        var npcUid = 0UL;
        var anonymousUserNameID = members.Count;

        var allyCamp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.LeftCamp };
        allyCamp.MemInfo.AddRange(members);
        allyCamp.MemInfo.AddRange(teammates.Select((row, index) =>
            BuildNpc(row, members.Count + index, ++npcUid, ++anonymousUserNameID, titles, aiLevel)));
        var enemyCamp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.RightCamp };
        enemyCamp.MemInfo.AddRange(enemies.Select((row, index) =>
            BuildNpc(row, index, ++npcUid, ++anonymousUserNameID, titles, aiLevel)));

        var memInfo = new pbCOMDT_MULTROOMMEMBER_INFO();
        memInfo.CampMem.Add(new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.JudgeCamp });
        memInfo.CampMem.Add(allyCamp);
        memInfo.CampMem.Add(enemyCamp);

        return new pbSCMatchRsp
        {
            MatchResult = new pbCOMDT_MATCH_RESULT
            {
                Result = (int)PB_ENUM_COM_MATCH_RESULT.PbComMatchSucc,
                MatchResDetail = new pbCOMDT_MATCH_DETAIL
                {
                    MatchSucc = EmptyChildren.Fill(new pbCOMDT_MATCH_SUCC_DETAIL
                    {
                        RoomEntity = MatchRoom.RoomEntity,
                        RoomID = room.RoomID,
                        RoomSeq = room.RoomSeq,
                        RoomInfo = new pbCOMDT_MATCHROOM_ATTR
                        {
                            MapId = room.MapId,
                            Feature = BattleVersion.Build(),
                            AILevel = aiLevel,
                        },
                        MemInfo = memInfo,
                        SuccTimeStamp = succTimeStamp,
                        ConfirmTimeoutMs = ConfirmTimeoutMs,
                        ConfirmUiType = TrainerConfirmUiType,
                    }),
                },
            },
        };
    }

    public static pbCOMDT_TMP_NAME_LIST BuildTitleNameList(GameData gameData, int anonymousUserNameID)
    {
        var tmpNameList = new pbCOMDT_TMP_NAME_LIST();
        var title = gameData.AiInfoConfig.Rows.FirstOrDefault(row => row.NPCType == TitleNpcType && row.Num == anonymousUserNameID);
        if (title is not null)
        {
            tmpNameList.TmpNameInfo.Add(new pbCOMDT_TMP_NAME_INFO
            {
                TmpNameType = TitleTmpNameType,
                TmpName = title.Name,
                IsValid = ValidTmpName,
            });
        }

        return tmpNameList;
    }

    private static pbCOMDT_ROOMMEMBER_DT BuildNpc(
        pbAiInfoConfig row,
        int posOfCamp,
        ulong npcUid,
        int anonymousUserNameID,
        IReadOnlyDictionary<int, string> titles,
        int aiLevel)
    {
        var tmpNameList = new pbCOMDT_TMP_NAME_LIST();
        if (titles.TryGetValue(anonymousUserNameID, out var title))
        {
            tmpNameList.TmpNameInfo.Add(new pbCOMDT_TMP_NAME_INFO
            {
                TmpNameType = TitleTmpNameType,
                TmpName = title,
                IsValid = ValidTmpName,
            });
        }

        var avatar = new pbCOMDT_ROLE_AVATAR_INFO
        {
            AvatarIDList = { row.AvatarItemID },
            RoleBaseModelID = row.BaseModelID,
            SkinColorID = row.SkinColorIdx,
        };
        return new pbCOMDT_ROOMMEMBER_DT
        {
            PosOfCamp = posOfCamp,
            RoomMemberType = NpcMember,
            MemberDetail = new pbCOMDT_ROOM_MEMBER_DETAIL
            {
                MemberOfNpc = EmptyChildren.Fill(new pbCOMDT_ROOMMEMBER_OF_NPC
                {
                    Level = aiLevel,
                    Detail = new pbCOMDT_FAKEACNT_DETAIL
                    {
                        Uid = npcUid,
                        AcntLevel = NpcAcntLevel,
                        UserName = row.Name,
                        Gender = row.Gender,
                        HeadId = row.HeadID,
                        AvatarInfo = avatar,
                        FriendShipLevel = row.FriendShipLevel,
                        ContestantAction = row.ContestantAction,
                        ContestantEmoji = row.ContestantEmoji,
                        ContestantBackgroud = row.ContestantBackgroud,
                        ContestantFrame = row.ContestantFrame,
                        NPCType = row.NPCType,
                        AnonymousUserNameID = anonymousUserNameID,
                        TmpNameList = tmpNameList,
                        UserPlantInfo = new pbCOMDT_ROLE_HEAD_URL_INFO { HeadType = SubscriptionHeadType },
                        DefaultRoleAvatarInfo = RoomMemberDetail.BuildDefaultRoleAvatarInfo(row.Gender),
                    },
                }),
            },
        };
    }
}
