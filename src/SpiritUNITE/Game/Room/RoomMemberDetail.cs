using System.Globalization;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Match;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed record RoomMemberProfile(pbCOMDT_ROLE_HEAD_URL_INFO UserPlantInfo, int ABModeType, bool IsGuestAccount);

public static class RoomMemberDetail
{
    public const int FirstAnonymousUserNameID = 1;
    public const int NoAnonymousUserNameID = 0;

    private const int AcntMember = (int)PB_ENUM_COM_ROOM_MEMBEM_TYPE.PbComRoomMemberTacnt;
    private const int SettlingMember = 1;
    private const int ReturnedMember = 0;
    private const int GuestAccount = 1;
    private const int RegisteredAccount = 0;
    private const int HasMapResource = 1;
    private const int BossRushGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeBossRush;
    private const int FirstBossRushGameLevel = 1;
    private const int NewUnLock = 1;
    private const int UnsetBattleSetting = -1;
    private const int ValidConfig = 1;
    private const int MaleGender = (int)PB_ENUM_RES_GENDER_TYPE.PbResGenderTypeMale;
    private const int FemaleGender = (int)PB_ENUM_RES_GENDER_TYPE.PbResGenderTypeFemale;
    private const int AnonymousUserNpcType = (int)PB_ENUM_RES_NPC_TYPE.PbResNpcTypeAnonymousUser;

    private static readonly IReadOnlyDictionary<int, string> NoTitles = new Dictionary<int, string>();

    private static readonly PB_ENUM_ENUM_SYNC_SETTINGS[] BattleMemberSettings =
    [
        PB_ENUM_ENUM_SYNC_SETTINGS.PbEnmBattleDefaultAvatar,
        PB_ENUM_ENUM_SYNC_SETTINGS.PbEnmBattleDefaultRoleName,
    ];

    public static pbCOMDT_ACNTMAP_INFO BuildAcntMapInfo(GameData gameData, RoomOptions options, IEnumerable<int> downloadedMapIds)
    {
        var acntMapInfo = new pbCOMDT_ACNTMAP_INFO();
        acntMapInfo.MapList.AddRange(downloadedMapIds
            .Distinct()
            .Where(mapId => RoomMaps.IsListed(gameData, mapId) && !options.IsHidden(mapId))
            .Select(mapId => new pbCOMDT_ACNTMAP_DETAIL
            {
                MapId = mapId,
                CanUseType = options.CanUseTypeOf(mapId),
                HasResource = HasMapResource,
            }));
        return acntMapInfo;
    }

    public static pbCOMDT_ROOMMEMBER_DT BuildRoomMember(
        GameData gameData,
        LMNRoleInfo role,
        RoomMember member,
        pbCOMDT_RANKED_DIVISION rankedDivision) => new()
        {
            PosOfCamp = member.PosOfCamp,
            RoomMemberType = AcntMember,
            MemberDetail = new pbCOMDT_ROOM_MEMBER_DETAIL
            {
                MemberOfAcnt = new pbCOMDT_ROOMMEMBER_OF_ACNT
                {
                    Uid = role.GameUlongUID,
                    FromGameEntity = MatchRoom.GameEntity,
                    MemberName = role.InGameUsrName,
                    MemberLevel = RoleProfile.GetLevel(gameData, role),
                    MemberHeadId = RoleImage.GetHeadID(gameData, role),
                    TeamFlag = member.TeamFlag,
                    IsSettle = member.IsSettle && !member.SettlesAlone ? SettlingMember : ReturnedMember,
                    AcntMapInfo = member.AcntMapInfo.Clone(),
                    RoleAvatarInfo = role.RoleAvatarInfo.Clone(),
                    Gender = role.Gender,
                    RankedDivision = rankedDivision,
                    IsGuestAccount = member.Profile.IsGuestAccount ? GuestAccount : RegisteredAccount,
                    ABModeType = member.Profile.ABModeType,
                    OpenID = role.GameUlongUID.ToString(CultureInfo.InvariantCulture),
                    ChallengeTrainer = new pbCOMDT_CHALLENGE_TRAINER_INFO(),
                    BossRushUnLockGameLevelInfo = BuildBossRushUnLock(gameData, member),
                    TowerDefenseUnlockGameLevelInfo = new pbCOMDT_TOWER_DEFENSE_UNLOCK_GAME_LEVEL_INFO(),
                    HeavenTrainer = BuildHeavenTrainer(role),
                    UserPlantInfo = member.Profile.UserPlantInfo.Clone(),
                    TmpNameList = new pbCOMDT_TMP_NAME_LIST(),
                    BattleMemberConfigList = BuildBattleMemberConfigList(isValid: false),
                },
            },
        };

    public static pbCOMDT_ROOMMEMBER_DT BuildMatchMember(
        GameData gameData,
        LMNRoleInfo role,
        RoomMember member,
        int teamFlag,
        int anonymousUserNameID,
        pbCOMDT_TMP_NAME_LIST tmpNameList) => new()
        {
            PosOfCamp = member.PosOfCamp,
            RoomMemberType = AcntMember,
            MemberDetail = new pbCOMDT_ROOM_MEMBER_DETAIL
            {
                MemberOfAcnt = new pbCOMDT_ROOMMEMBER_OF_ACNT
                {
                    Uid = role.GameUlongUID,
                    FromGameEntity = MatchRoom.GameEntity,
                    MemberName = role.InGameUsrName,
                    MemberLevel = RoleProfile.GetLevel(gameData, role),
                    MemberHeadId = RoleImage.GetHeadID(gameData, role),
                    TeamFlag = teamFlag,
                    AcntMapInfo = new pbCOMDT_ACNTMAP_INFO(),
                    RoleAvatarInfo = role.RoleAvatarInfo.Clone(),
                    Gender = role.Gender,
                    RankedDivision = new pbCOMDT_RANKED_DIVISION(),
                    UsePlayTogetherBuff = new pbCOMDT_USET_PLAY_TOGETHER_ACTIVITY_BUFF(),
                    ChallengeTrainer = new pbCOMDT_CHALLENGE_TRAINER_INFO(),
                    BossRushUnLockGameLevelInfo = new pbCOMDT_BOSS_RUSH_UNLOCK_GAME_LEVEL_INFO(),
                    TowerDefenseUnlockGameLevelInfo = new pbCOMDT_TOWER_DEFENSE_UNLOCK_GAME_LEVEL_INFO(),
                    HeavenTrainer = new pbCOMDT_CHALLENGE_TRAINER_INFO(),
                    UserPlantInfo = member.Profile.UserPlantInfo.Clone(),
                    TmpNameList = tmpNameList,
                    BattleMemberConfigList = BuildBattleMemberConfigList(isValid: true),
                    AnonymousUserNameID = anonymousUserNameID,
                    DefaultRoleAvatarInfo = BuildDefaultRoleAvatarInfo(gameData, role.Gender),
                },
            },
        };

    public static pbCOMDT_ROOMMEMBER_DT BuildRoomBot(GameData gameData, RoomBot bot, int posOfCamp, int aiLevel) =>
        PveMatch.BuildNpc(gameData, bot.Row, posOfCamp, bot.Uid, NoAnonymousUserNameID, NoTitles, aiLevel);

    public static pbCOMDT_ROLE_AVATAR_INFO BuildDefaultRoleAvatarInfo(GameData gameData, int gender)
    {
        var anonymousGender = gender == MaleGender ? MaleGender : FemaleGender;
        var anonymousUser = gameData.AiInfoConfig.Rows
            .Where(row => row.NPCType == AnonymousUserNpcType && row.Gender == anonymousGender)
            .MinBy(row => row.ID);
        if (anonymousUser is null)
        {
            return new pbCOMDT_ROLE_AVATAR_INFO();
        }

        var avatar = new pbCOMDT_ROLE_AVATAR_INFO
        {
            RoleBaseModelID = anonymousUser.BaseModelID,
            SkinColorID = anonymousUser.SkinColorIdx,
        };
        avatar.AvatarIDList.AddRange(anonymousUser.AvatarItemID);
        return avatar;
    }

    private static pbCOMDT_CHALLENGE_TRAINER_INFO BuildHeavenTrainer(LMNRoleInfo role)
    {
        var heavenTrainer = new pbCOMDT_CHALLENGE_TRAINER_INFO();
        heavenTrainer.ChallengeInfo.AddRange(role.GetHeavenTrainerRecords()
            .GroupBy(record => record.MapId)
            .Select(map => new pbCOMDT_CHALLENGE_TRAINER_MAP_RECORD_INFO
            {
                MapId = map.Key,
                RecordInfo =
                {
                    map.Select(record => new pbCOMDT_CHALLENGE_TRAINER_RECORD_INFO
                    {
                        LevelType = record.LevelType,
                        ChallengeCnt = record.ChallengeCnt,
                    }),
                },
            }));
        return heavenTrainer;
    }

    private static pbCOMDT_BOSS_RUSH_UNLOCK_GAME_LEVEL_INFO BuildBossRushUnLock(GameData gameData, RoomMember member)
    {
        var firstLevelMap = gameData.SceneMap.Rows.FirstOrDefault(scene =>
            scene.GameType == BossRushGameType
            && scene.GameLevel == FirstBossRushGameLevel
            && member.UsableMaps.Contains(scene.SceneId));
        return firstLevelMap is null
            ? new pbCOMDT_BOSS_RUSH_UNLOCK_GAME_LEVEL_INFO()
            : new pbCOMDT_BOSS_RUSH_UNLOCK_GAME_LEVEL_INFO
            {
                MaxGameLevel = FirstBossRushGameLevel,
                MapID = firstLevelMap.SceneId,
                IsNewUnLock = NewUnLock,
            };
    }

    private static pbCOMDT_ROLE_CONFIG_BATTLE_MEMBER_LIST BuildBattleMemberConfigList(bool isValid)
    {
        var configList = new pbCOMDT_ROLE_CONFIG_BATTLE_MEMBER_LIST();
        configList.ConfigList.AddRange(BattleMemberSettings.Select(setting => new pbCOMDT_ROLE_CONFIG_INFO
        {
            ConfigType = (int)setting,
            ConfigValue = UnsetBattleSetting,
            IsValid = isValid ? ValidConfig : 0,
        }));
        return configList;
    }
}
