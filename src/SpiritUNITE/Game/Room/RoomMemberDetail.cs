using System.Globalization;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed record RoomMemberProfile(pbCOMDT_ROLE_HEAD_URL_INFO UserPlantInfo, int ABModeType, bool IsGuestAccount);

public static class RoomMemberDetail
{
    public const int FirstAnonymousUserNameID = 1;

    private const int AcntMember = (int)PB_ENUM_COM_ROOM_MEMBEM_TYPE.PbComRoomMemberTacnt;
    private const int OwnTeamFlag = 1;
    private const int GuestAccount = 1;
    private const int RegisteredAccount = 0;
    private const int HasMapResource = 1;
    private const int BossRushGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeBossRush;
    private const int FirstBossRushGameLevel = 1;
    private const int NewUnLock = 1;
    private const int UnsetBattleSetting = -1;
    private const int ValidConfig = 1;
    private const int MaleGender = (int)PB_ENUM_RES_GENDER_TYPE.PbResGenderTypeMale;
    private const int MaleDefaultModelID = 20191219;
    private const int FemaleDefaultModelID = 20200501;
    private const int DefaultSkinColorID = 7;
    private const int DefaultAvatarSlotCount = 12;

    private static readonly int[] MaleDefaultAvatarIDs = [1004000114, 1004000111, 1004000110, 1004001106, 1004000113];
    private static readonly int[] FemaleDefaultAvatarIDs = [1004000214, 1004000111, 1004000110, 1004001106, 1004000113];

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
                    FromGameEntity = MatchRoom.RoomEntity,
                    MemberName = role.InGameUsrName,
                    MemberLevel = RoleProfile.GetLevel(gameData, role),
                    MemberHeadId = RoleImage.GetHeadID(gameData, role),
                    TeamFlag = OwnTeamFlag,
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
                    HeavenTrainer = new pbCOMDT_CHALLENGE_TRAINER_INFO(),
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
                    FromGameEntity = MatchRoom.RoomEntity,
                    MemberName = role.InGameUsrName,
                    MemberLevel = RoleProfile.GetLevel(gameData, role),
                    MemberHeadId = RoleImage.GetHeadID(gameData, role),
                    TeamFlag = OwnTeamFlag,
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
                    DefaultRoleAvatarInfo = BuildDefaultRoleAvatarInfo(role.Gender),
                },
            },
        };

    public static pbCOMDT_ROLE_AVATAR_INFO BuildDefaultRoleAvatarInfo(int gender)
    {
        var isMale = gender == MaleGender;
        var avatarIDs = isMale ? MaleDefaultAvatarIDs : FemaleDefaultAvatarIDs;
        var avatar = new pbCOMDT_ROLE_AVATAR_INFO
        {
            RoleBaseModelID = isMale ? MaleDefaultModelID : FemaleDefaultModelID,
            SkinColorID = DefaultSkinColorID,
        };
        avatar.AvatarIDList.AddRange(avatarIDs.Concat(Enumerable.Repeat(0, DefaultAvatarSlotCount - avatarIDs.Length)));
        return avatar;
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
