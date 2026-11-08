using SpiritUNITE.Databin;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Match;

public sealed record BotMatchSucc(pbSCMatchRsp MatchRsp, IReadOnlyDictionary<ulong, pbAiInfoConfig> NpcConfigs, int WarmBattleType = 0);

public static class PveMatch
{
    public const int MinAILevel = (int)PB_ENUM_RES_AI_DIFFICULTYLEVEL_TYPE.PbResAiDifficultylevelTypeWarmbattle;

    public const int ReadyConfirmUiType = 1;
    public const int GenericNpcType = (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeNormal;

    private const int TrainerConfirmUiType = 2;
    private const int CatchGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeCatch;
    private const int CatchAILevel = (int)PB_ENUM_RES_AI_DIFFICULTYLEVEL_TYPE.PbResAiDifficultylevelTypeEasy;
    private const int NpcAcntLevel = 1;
    private const int NpcMember = (int)PB_ENUM_COM_ROOM_MEMBEM_TYPE.PbComRoomMemberTnpc;
    private const int TitleNpcType = (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeAnonymousUser;
    private const int TitleTmpNameType = 2;
    private const int ValidTmpName = 1;
    private const int SubscriptionHeadType = (int)PB_ENUM_ENUM_SOCIAL_HEAD_TYPE.PbEnumSocialHeadTypeSubscription;

    private const int FirstMatchRoomID = 10000;
    private const int MatchRoomIDCount = 10000;
    private const int AceTrainerFemale = (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeAceTrainerFemale;
    private const int AceTrainerMale = (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeAceTrainerMale;
    private const int SlotsPerAceTrainerPair = 2;
    private const int NoPokemon = 0;

    private static readonly int[] TeammateNpcTypes =
    [
        (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeHeavenTrainer1,
        (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeHeavenTrainer2,
        (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeHeavenTrainer3,
        (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeHeavenTrainer4,
    ];

    private static readonly IReadOnlyDictionary<string, PB_ENUM_RES_NPC_TYPE> LegendTrainerByTrainerImgKOed =
        new Dictionary<string, PB_ENUM_RES_NPC_TYPE>
        {
            ["t_Battle_notify_ko_Dande"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeDande,
            ["t_Battle_notify_ko_Kibana"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeKibana,
            ["t_Battle_notify_ko_Korrina"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeCornichon,
            ["t_Battle_notify_ko_Shirona"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeShirona,
            ["t_Battle_notify_ko_Blue"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeBlue,
            ["t_Battle_notify_ko_Friede"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeFriede,
            ["t_Battle_notify_ko_Dot"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeDot,
            ["t_Battle_notify_ko_Liko"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeLiko,
            ["t_Battle_notify_ko_Roy"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeRoy,
            ["t_Battle_notify_ko_Uruto"] = PB_ENUM_RES_NPC_TYPE.PbResNpcTypeUlr,
        };

    public static BotMatchSucc MatchSucc(
        GameData gameData,
        MatchRoom room,
        IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> members,
        int masterTrainerLevel,
        long succTimeStamp,
        int confirmTimeoutMs)
    {
        var npcConfigs = new Dictionary<ulong, pbAiInfoConfig>();
        var npcUids = NpcUids.Besides(members);
        var isCatchMap = gameData.SceneMap.Find(room.MapId)?.GameType == CatchGameType;
        var aiLevel = isCatchMap ? CatchAILevel : masterTrainerLevel;
        var memInfo = isCatchMap
            ? BuildCatchMemInfo(gameData, room, members, aiLevel, npcConfigs, npcUids)
            : BuildHeavenTrainerMemInfo(gameData, room, members, aiLevel, npcConfigs, npcUids);

        var matchRsp = BuildMatchSuccRsp(room.MapId, aiLevel, memInfo, succTimeStamp, isCatchMap ? ReadyConfirmUiType : TrainerConfirmUiType, confirmTimeoutMs);
        return new BotMatchSucc(matchRsp, npcConfigs);
    }

    public static pbSCMatchRsp BuildMatchSuccRsp(
        int mapId,
        int aiLevel,
        pbCOMDT_MULTROOMMEMBER_INFO memInfo,
        long succTimeStamp,
        int confirmUiType,
        int confirmTimeoutMs) => new()
        {
            MatchResult = new pbCOMDT_MATCH_RESULT
            {
                Result = (int)PB_ENUM_COM_MATCH_RESULT.PbComMatchSucc,
                MatchResDetail = new pbCOMDT_MATCH_DETAIL
                {
                    MatchSucc = EmptyChildren.Fill(new pbCOMDT_MATCH_SUCC_DETAIL
                    {
                        RoomEntity = MatchRoom.MatchRoomEntity,
                        RoomID = FirstMatchRoomID + Random.Shared.Next(MatchRoomIDCount),
                        RoomSeq = succTimeStamp,
                        RoomInfo = new pbCOMDT_MATCHROOM_ATTR
                        {
                            MapId = mapId,
                            Feature = BattleVersion.Build(),
                            AILevel = aiLevel,
                        },
                        MemInfo = memInfo,
                        SuccTimeStamp = succTimeStamp,
                        ConfirmTimeoutMs = confirmTimeoutMs,
                        ConfirmUiType = confirmUiType,
                    }),
                },
            },
        };

    private static pbCOMDT_MULTROOMMEMBER_INFO BuildCatchMemInfo(
        GameData gameData,
        MatchRoom room,
        IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> members,
        int aiLevel,
        Dictionary<ulong, pbAiInfoConfig> npcConfigs,
        NpcUids npcUids)
    {
        int[] playerCamps = [MatchRoom.LeftCamp, MatchRoom.RightCamp];
        var humanCamp = playerCamps[Random.Shared.Next(playerCamps.Length)];
        var npcRows = gameData.AiInfoConfig.Rows
            .Where(row => row.NPCType == GenericNpcType)
            .OrderBy(row => row.ID)
            .Take(playerCamps.Length * room.CampPlayerNum - members.Count)
            .ToArray();
        var titles = BuildTitles(gameData);
        var seatedNpcCount = 0;
        var anonymousUserNameID = 0;
        var memInfo = new pbCOMDT_MULTROOMMEMBER_INFO();
        memInfo.CampMem.Add(new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.JudgeCamp });
        foreach (var campClient in playerCamps)
        {
            var camp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = campClient };
            var campHumans = campClient == humanCamp ? members : [];
            for (var posOfCamp = 0; posOfCamp < room.CampPlayerNum; posOfCamp++)
            {
                anonymousUserNameID++;
                if (posOfCamp < campHumans.Count)
                {
                    camp.MemInfo.Add(SeatHuman(gameData, campHumans[posOfCamp], posOfCamp, anonymousUserNameID));
                }
                else if (seatedNpcCount < npcRows.Length)
                {
                    var row = npcRows[seatedNpcCount++];
                    camp.MemInfo.Add(BuildNpc(gameData, row, posOfCamp, RememberNpc(npcConfigs, npcUids.NextInOrder(), row), anonymousUserNameID, titles, aiLevel));
                }
            }

            memInfo.CampMem.Add(camp);
        }

        return memInfo;
    }

    public static pbCOMDT_ROOMMEMBER_DT SeatHuman(GameData gameData, pbCOMDT_ROOMMEMBER_DT member, int posOfCamp, int anonymousUserNameID)
    {
        var seated = member.Clone();
        seated.PosOfCamp = posOfCamp;
        if (seated.MemberDetail?.MemberOfAcnt is { } acnt)
        {
            acnt.AnonymousUserNameID = anonymousUserNameID;
            acnt.TmpNameList = BuildTitleNameList(gameData, anonymousUserNameID);
        }

        return seated;
    }

    public static Dictionary<int, string> BuildTitles(GameData gameData) =>
        gameData.AiInfoConfig.Rows
            .Where(row => row.NPCType == TitleNpcType)
            .GroupBy(row => row.Num)
            .ToDictionary(group => group.Key, group => group.First().Name);

    private static pbCOMDT_MULTROOMMEMBER_INFO BuildHeavenTrainerMemInfo(
        GameData gameData,
        MatchRoom room,
        IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> members,
        int aiLevel,
        Dictionary<ulong, pbAiInfoConfig> npcConfigs,
        NpcUids npcUids)
    {
        var teammateCount = Math.Max(0, room.CampPlayerNum - members.Count);
        var teammates = gameData.AiInfoConfig.Rows
            .Where(row => TeammateNpcTypes.Contains(row.NPCType))
            .OrderBy(row => row.NPCType)
            .Take(teammateCount)
            .ToArray();
        var route = FindRoute(gameData, room.PveChallengeSelectNode);
        var enemies = Enumerable.Range(0, room.CampPlayerNum)
            .Select(slot => FindEnemyNpc(gameData, route, room.MapId, slot))
            .OfType<pbAiInfoConfig>()
            .ToArray();
        var titles = BuildTitles(gameData);
        var anonymousUserNameID = members.Count;

        var allyCamp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.LeftCamp };
        allyCamp.MemInfo.AddRange(members);
        allyCamp.MemInfo.AddRange(teammates.Select((row, index) =>
            BuildNpc(gameData, row, members.Count + index, RememberNpc(npcConfigs, npcUids.NextInOrder(), row), ++anonymousUserNameID, titles, aiLevel)));
        var enemyCamp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.RightCamp };
        enemyCamp.MemInfo.AddRange(enemies.Select((row, index) =>
            BuildNpc(gameData, row, index, RememberNpc(npcConfigs, npcUids.NextInOrder(), row), ++anonymousUserNameID, titles, aiLevel)));

        var memInfo = new pbCOMDT_MULTROOMMEMBER_INFO();
        memInfo.CampMem.Add(new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.JudgeCamp });
        memInfo.CampMem.Add(allyCamp);
        memInfo.CampMem.Add(enemyCamp);
        return memInfo;
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

    private static pbPveChallengeStageRoute? FindRoute(GameData gameData, pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE? selectNode) =>
        selectNode is not null
        && gameData.PveChallengeStageRoute.Find(pbPveChallengeStageRoute.Key(selectNode.StageID, selectNode.NodeID, selectNode.DifficultyLevel))
            is { } route
        && route.StageID == selectNode.StageID
        && route.NodeID == selectNode.NodeID
        && route.DifficultyLevel == selectNode.DifficultyLevel
            ? route
            : null;

    private static pbAiInfoConfig? FindEnemyNpc(GameData gameData, pbPveChallengeStageRoute? route, int mapId, int slot) =>
        FindLegendTrainerNpcType(gameData, route, mapId, slot) is { } legendTrainer
        && FindFirstNpcOfType(gameData, (int)legendTrainer) is { } legendTrainerNpc
            ? legendTrainerNpc
            : FindFirstNpcOfType(gameData, slot % SlotsPerAceTrainerPair == 0 ? AceTrainerFemale : AceTrainerMale);

    private static PB_ENUM_RES_NPC_TYPE? FindLegendTrainerNpcType(GameData gameData, pbPveChallengeStageRoute? route, int mapId, int slot) =>
        route?.EnemyldList.ElementAtOrDefault(slot) is > NoPokemon and var pokemonId
        && gameData.PokemonBase.Find(pokemonId) is { } pokemon
        && gameData.TrainerPokemon.Find(pokemon.MainUnitId) is { } trainerPokemon
        && !trainerPokemon.IgnoreSceneId.Contains(mapId)
        && LegendTrainerByTrainerImgKOed.TryGetValue(trainerPokemon.TrainerImgKOed, out var legendTrainer)
            ? legendTrainer
            : null;

    private static pbAiInfoConfig? FindFirstNpcOfType(GameData gameData, int npcType) =>
        gameData.AiInfoConfig.Rows
            .Where(row => row.NPCType == npcType)
            .OrderBy(row => row.Num)
            .FirstOrDefault();

    private static int PickNpcHeadId(GameData gameData) =>
        gameData.HeadIcon.Rows[Random.Shared.Next(gameData.HeadIcon.Rows.Count)].HeadIconID;

    public static ulong RememberNpc(Dictionary<ulong, pbAiInfoConfig> npcConfigs, ulong npcUid, pbAiInfoConfig row)
    {
        npcConfigs[npcUid] = row;
        return npcUid;
    }

    public static pbCOMDT_ROOMMEMBER_DT BuildNpc(
        GameData gameData,
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
                        HeadId = PickNpcHeadId(gameData),
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
                        DefaultRoleAvatarInfo = RoomMemberDetail.BuildDefaultRoleAvatarInfo(gameData, row.Gender),
                    },
                }),
            },
        };
    }
}
