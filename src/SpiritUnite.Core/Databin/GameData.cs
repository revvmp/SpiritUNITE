using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Databin;

public sealed class GameData
{
    public static readonly string DirectoryName = Path.Combine("Databin", "Tables");

    private const string FileExtension = ".json";
    private const int DefaultLobbyBgFlag = 1;

    private static readonly JsonParser MessageParser = new(JsonParser.Settings.Default);

    private static readonly JsonSerializerOptions RecordOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string directory;

    private GameData(string directory)
    {
        this.directory = directory;
        CreateRoleShowAvatar = LoadMessages<pbCreateRoleShowAvatar>(row => row.AvatarID);
        DefaultAvatarItem = LoadMessages<pbDefaultAvatarItem>(row => row.AvatarItemId);
        RoleImage = LoadMessages<pbRoleImage>(row => row.ID);
        TrainerLevel = LoadMessages<pbTrainerLevel>(row => row.Level);
        NewbieRewardHero = LoadMessages<pbNewbie_RewardHero>(row => row.HeroId);
        LobbyBg = LoadRecords<pbLobbyBg>(row => row.LobbyBgID);
        StarRoadSite = LoadRecords<pbStarRoadSite>(row => row.SiteID);
        NewbieGift = LoadRecords<pbNewbieGift>(row => pbNewbieGift.Key(row.GiftID, row.SchemeId));
        ReportText = LoadRecords<pbReportText>(row => pbReportText.Key(row.ReportCategory, row.ReportReason));
        PvePrivilegeActivity = LoadRecords<pbPvePrivilegeActivity>(row => row.ActivityID);
        PveChallengeShop = LoadRecords<pbPveChallengeShop>(row => row.ShopID);
        PveBuffLevelUpDesign = LoadRecords<pbPveBuffLevelUpDesign>(row => row.Id);
        ActivityExchange = LoadRecords<pbActivityExchange>(row => row.ExchangeID);
        FirstChargeAward = LoadRecords<pbFirstChargeAward>(row => row.LoginSeq);
        MysteryShopDiscount = LoadRecords<pbMysteryShopDiscount>(row => pbMysteryShopDiscount.Key(row.AreaID, row.ActivityID));
        NewbieChallengeTask = LoadRecords<pbNewbieChallengeTask>(row => pbNewbieChallengeTask.Key(row.SchemeId, row.TaskDay));
        StarRoadPokemon = LoadRecords<pbStarRoadPokemon>(row => row.PokemonId);
        ActivityBase = LoadMessages<pbActivity_Base>(row => row.ActivityID);
        TaskBase = LoadMessages<pbTask_Base>(row => row.TaskID);
        DropStatic = LoadMessages<pbDrop_Static>(row => pbDrop_Static.Key(row.SDropID, row.SDropSeq));
        ResIDTypeMatchBase = LoadMessages<pbResIDTypeMatch_Base>(row => row.ResIDMin);
        DropBase = LoadRecords<pbDrop_Base>(row => pbDrop_Base.Key(row.DropID, row.DropSeq));
        DropPack = LoadRecords<pbDrop_Pack>(row => pbDrop_Pack.Key(row.DropPackID, row.DropPackSeq));
        Shop = LoadMessages<pbShop>(row => row.ShopID);
        AvatarItem = LoadMessages<pbAvatarItem>(row => row.AvatarItemId);
        AvatarSuit = LoadMessages<pbAvatarSuit>(row => row.AvatarSuitId);
        OutSideItemBase = LoadMessages<pbOutSideItem_Base>(row => row.OutSideItemId);
        RankedMatchSeason = LoadMessages<pbRankedMatchSeason>(row => row.SeasonId);
        RankedDivision = LoadMessages<pbRankedDivision>(row => row.Index);
        RankedMatchMapOpenTime = LoadRecords<pbRankedMatchMapOpenTime>(row => row.Index);
        RankMatchPointMedal = LoadRecords<pbRankMatchPointMedal>(row => row.MedalID);
        RankedMatchSeasonAward = LoadMessages<pbRankedMatchSeasonAward>(row => pbRankedMatchSeasonAward.Key(row.AwardId, row.BigRankedDivision));
        RankedMatchSeasonTaskAward = LoadRecords<pbRankedMatchSeasonTaskAward>(row =>
            pbRankedMatchSeasonTaskAward.Key(row.AwardGroupId, row.BigRankedDivision, row.RankedDivision));
        BanPickRule = LoadRecords<pbBanPickRule>(row => row.BanPickRuleId);
        RoundRule = LoadRecords<pbRoundRule>(row => row.RoundRuleId);
        FriendlyRoomRule = LoadRecords<pbFriendlyRoomRule>(row => row.Id);
        SceneMap = LoadMessages<pbScene_Map>(row => row.SceneId);
        SceneOpenTime = LoadRecords<pbScene_OpenTime>(row => pbScene_OpenTime.Key(row.Index, row.SceneId));
        GlobalBase = LoadMessages<pbGlobal_Base>(row => row.GlobalId);
        MapBase = LoadMessages<pbMapBase>(row => row.MapBaseId);
        MvpConf = LoadRecords<pbMvp_Conf>(row => pbMvp_Conf.Key(row.GameType, row.ParamType));
        MatchTrainer = LoadRecords<pbMatchTrainer>(row => pbMatchTrainer.Key(row.MapId, row.DifficultyLevel));
        Achievement = LoadRecords<pbAchievement>(row => row.AchievementID);
        AchievementBattleCondition = LoadMessages<pbAchievementBattleCondition>(row => row.AchievementId);
        BattlePassSeason = LoadMessages<pbBattlePassSeason>(row => row.SeasonID);
        BattlePassLevel = LoadMessages<pbBattlePassLevel>(row => pbBattlePassLevel.Key(row.SeasonID, row.Level));
        BattlePassLevelReward = LoadMessages<pbBattlePassLevelReward>(row => pbBattlePassLevelReward.Key(row.SeasonID, row.Level));
        BattlePassSeasonTask = LoadRecords<pbBattlePass_SeasonTask>(row => pbBattlePass_SeasonTask.Key(row.SeasonID, row.WeekIndex));
        BattlePassTimeLimitTask = LoadRecords<pbBattlePass_TimeLimitTask>(row => row.ID);
        BattlePassInfo = LoadRecords<pbBattlePassInfo>(row => row.SeasonID);
        BattlePassWeekIndex = LoadMessages<pbBattlePass_WeekIndex>(row => pbBattlePass_WeekIndex.Key(row.SeasonID, row.WeekIndex));
        BattlePassWeekTaskGroup = LoadMessages<pbBattlePass_WeekTaskGroup>(row => row.WeekTaskGroupID);
        RewardPool = LoadRecords<pbReward_Pool>(row => row.LotteryPoolId);
        HeadIconBase = LoadMessages<pbHeadIcon_Base>(row => row.HeadIconID);
        HeadIcon = LoadMessages<pbHeadIcon>(row => row.HeadIconID);
        ContestantCardDecoration = LoadMessages<pbContestant_Card_Decoration>(row => row.DecorationID);
        CanopyBase = LoadRecords<pbCanopy_Base>(row => row.CanopyID);
        CardFace = LoadRecords<pbCardFace>(row => row.CardFaceID);
        PersonStyleHeadFrame = LoadRecords<pbPersonStyleHeadFrame>(row => row.HeadIconID);
        PersonalityLabel = LoadRecords<pbPersonalityLabel>(row => row.ID);
        BattleSignalCommunicate = LoadMessages<pbBattleSignalCommunicate>(row => row.Id);
        NationSelectInformation = LoadMessages<pbNationSelectInformation>(row => row.NationID);
        SystemMenuList = LoadRecords<pbSystemMenuList>(row => row.MenuItem);
        HeldItemLevelUpDesign = LoadMessages<pbHeldItemLevelUpDesign>(row => row.Id);
        HeldItemSystemConfig = LoadRecords<pbHeldItemSystemConfig>(row => row.Id);
        SystemUnlock = LoadMessages<pbSystemUnlock>(row => pbSystemUnlock.Key(row.ID, row.SchemeId));
        HangupScheme = LoadMessages<pbHangupScheme>(row => pbHangupScheme.Key(row.HangupSchemeID, row.KeyID));
        BadgeBase = LoadMessages<pbBadge_Base>(row => row.BadgeId);
        PokemonBase = LoadMessages<pbPokemon_Base>(row => row.PokemonId);
        PokemonRelease = LoadMessages<pbPokemon_Release>(row => row.PokemonId);
        BadgeUnlock = LoadRecords<pbBadgeUnlock>(row => row.BadgeSchemeID);
        BadgeColor = LoadRecords<pbBadgeColor>(row => pbBadgeColor.Key(row.BadgeColor, row.BadgeColorLv));
        NewbieGuideOrder = LoadMessages<pbNewbieGuideOrder>(row => row.ID);
        NewbieGuideSteps = LoadMessages<pbNewbieGuideSteps>(row => pbNewbieGuideSteps.Key(row.NewbieEnum, row.StepIndex));
        PveChallengeStageRoute = LoadMessages<pbPveChallengeStageRoute>(row =>
            pbPveChallengeStageRoute.Key(row.StageID, row.NodeID, row.DifficultyLevel));
        AiInfoConfig = LoadMessages<pbAiInfoConfig>(row => row.ID);
        TrainerPokemon = LoadMessages<pbTrainerPokemon>(row => row.PokemonUnitId);
        PokemonAvatarBase = LoadMessages<pbPokemon_Avatar_Base>(row => row.PokemonAvatarId);
        SkillEffectExpandPackBase = LoadMessages<pbSkillEffectExpandPack_Base>(row => row.SkillEffectsExpandPackID);
        PokemonEquipment = LoadMessages<pbPokemon_Equipment>(row => row.EquipmentId);
        UnitBase = LoadMessages<pbUnit_Base>(row => row.UnitId);
        NationGroup = LoadRecords<pbNationGroup>(row => row.NationGroupID);
        TowerDefenseBase = LoadRecords<pbTowerDefenseBase>(row => row.PeriodID);
        TowerDefenseShop = LoadRecords<pbTowerDefenseShop>(row => pbTowerDefenseShop.Key(row.ShopID, row.PeriodID));
        TowerDefenseBuffLevelUpDesign = LoadMessages<pbTowerDefenseBuffLevelUpDesign>(row => row.Id);
        TowerDefensePassAward = LoadRecords<pbTowerDefensePassAward>(row => pbTowerDefensePassAward.Key(row.SceneId, row.PeriodID));
        UniteChallenge = LoadRecords<pbUniteChallenge>(row => row.ID);
        BossRushActivityBase = LoadRecords<pbBossRushActivityBase>(row => row.PeriodID);
        BossRushGuildActivityScoreAward = LoadRecords<pbBossRushGuildActivityScoreAward>(row =>
            pbBossRushGuildActivityScoreAward.Key(row.GearID, row.RewardID));
        MegaEvolutionOther = LoadRecords<pbMegaEvolutionOther>(row => pbMegaEvolutionOther.Key(row.ActivityID, row.OtherType));
        MegaEvolutionReward = LoadRecords<pbMegaEvolutionReward>(row => pbMegaEvolutionReward.Key(row.ActivityID, row.AwardKey));
        MegaEvolutionTaskPool = LoadRecords<pbMegaEvolutionTaskPool>(row => row.Index);
        SubScriptionPeriod = LoadRecords<pbSubScriptionPeriod>(row => row.ID);
        TrainerExpGift = LoadRecords<pbTrainerExpGift>(row => pbTrainerExpGift.Key(row.SchemeId, row.TotalExp));
        TrainerSeasonExpGift = LoadRecords<pbTrainerSeasonExpGift>(row => pbTrainerSeasonExpGift.Key(row.SeasonID, row.TotalExp));
        PasterSpray = LoadRecords<pbPasterSpray>(row => row.PasterSprayID);
        NewPlayerDefaultRes = LoadRecords<pbNewPlayerDefaultRes>(row => row.NewplayerRewardId);
        EeveeChallenge = LoadRecords<pbEeveeChallenge>(row => pbEeveeChallenge.Key(row.EeveeID, row.ActivityID));
        GuildLabel = LoadRecords<pbGuildLabel>(row => row.LableID);
        GuildActivityBase = LoadRecords<pbGuildActivity_Base>(row => row.PeriodID);
        GuildActivityRewardPool = LoadRecords<pbGuildActivity_RewardPool>(row => row.PoolID);
        GuildActivityScoreGear = LoadRecords<pbGuildActivity_ScoreGear>(row => row.GearID);
        GuildActivityScoreReward = LoadRecords<pbGuildActivity_ScoreReward>(row => pbGuildActivity_ScoreReward.Key(row.RewardID, row.GearID));
        GuildActivityTask = LoadRecords<pbGuildActivity_Task>(row => row.RewardID);
        TenGames = LoadRecords<pbTenGames>(row => row.GamesId);
        TenGamesRankReward = LoadRecords<pbTenGamesRankReward>(row => pbTenGamesRankReward.Key(row.GamesId, row.GearID));
        TenGamesBPActivityOption = LoadRecords<pbTenGamesBPActivityOption>(row => row.ActivityID);
        TenGamesBPActivityReward = LoadRecords<pbTenGamesBPActivityReward>(row => pbTenGamesBPActivityReward.Key(row.ActivityID, row.Seq));
        PredictionTime = LoadRecords<pbPredictionTime>(row => row.PredictionStatge);
        IntimacySetting = LoadRecords<pbIntimacySetting>(row => row.ID);
        IntimacyRule = LoadRecords<pbIntimacyRule>(row => row.ID);
        IntimacyLevel = LoadRecords<pbIntimacyLevel>(row => row.Level);
        DefaultLobbyBgId = FindDefaultLobbyBgId(LobbyBg);
    }

    public DatabinTable<pbCreateRoleShowAvatar> CreateRoleShowAvatar { get; }
    public DatabinTable<pbDefaultAvatarItem> DefaultAvatarItem { get; }
    public DatabinTable<pbRoleImage> RoleImage { get; }
    public DatabinTable<pbTrainerLevel> TrainerLevel { get; }
    public DatabinTable<pbNewbie_RewardHero> NewbieRewardHero { get; }
    public DatabinTable<pbLobbyBg> LobbyBg { get; }
    public DatabinTable<pbStarRoadSite> StarRoadSite { get; }
    public DatabinTable<pbNewbieGift> NewbieGift { get; }
    public DatabinTable<pbReportText> ReportText { get; }
    public DatabinTable<pbPvePrivilegeActivity> PvePrivilegeActivity { get; }
    public DatabinTable<pbPveChallengeShop> PveChallengeShop { get; }
    public DatabinTable<pbPveBuffLevelUpDesign> PveBuffLevelUpDesign { get; }
    public DatabinTable<pbActivityExchange> ActivityExchange { get; }
    public DatabinTable<pbFirstChargeAward> FirstChargeAward { get; }
    public DatabinTable<pbMysteryShopDiscount> MysteryShopDiscount { get; }
    public DatabinTable<pbNewbieChallengeTask> NewbieChallengeTask { get; }
    public DatabinTable<pbStarRoadPokemon> StarRoadPokemon { get; }
    public DatabinTable<pbActivity_Base> ActivityBase { get; }
    public DatabinTable<pbNationGroup> NationGroup { get; }
    public DatabinTable<pbIntimacySetting> IntimacySetting { get; }
    public DatabinTable<pbIntimacyRule> IntimacyRule { get; }
    public DatabinTable<pbIntimacyLevel> IntimacyLevel { get; }
    public DatabinTable<pbTowerDefenseBase> TowerDefenseBase { get; }
    public DatabinTable<pbTowerDefenseShop> TowerDefenseShop { get; }
    public DatabinTable<pbTowerDefenseBuffLevelUpDesign> TowerDefenseBuffLevelUpDesign { get; }
    public DatabinTable<pbTowerDefensePassAward> TowerDefensePassAward { get; }
    public DatabinTable<pbUniteChallenge> UniteChallenge { get; }
    public DatabinTable<pbBossRushActivityBase> BossRushActivityBase { get; }
    public DatabinTable<pbBossRushGuildActivityScoreAward> BossRushGuildActivityScoreAward { get; }
    public DatabinTable<pbMegaEvolutionOther> MegaEvolutionOther { get; }
    public DatabinTable<pbMegaEvolutionReward> MegaEvolutionReward { get; }
    public DatabinTable<pbMegaEvolutionTaskPool> MegaEvolutionTaskPool { get; }
    public DatabinTable<pbSubScriptionPeriod> SubScriptionPeriod { get; }
    public DatabinTable<pbTrainerExpGift> TrainerExpGift { get; }
    public DatabinTable<pbTrainerSeasonExpGift> TrainerSeasonExpGift { get; }
    public DatabinTable<pbPasterSpray> PasterSpray { get; }
    public DatabinTable<pbNewPlayerDefaultRes> NewPlayerDefaultRes { get; }
    public DatabinTable<pbEeveeChallenge> EeveeChallenge { get; }
    public DatabinTable<pbTask_Base> TaskBase { get; }
    public DatabinTable<pbDrop_Static> DropStatic { get; }
    public DatabinTable<pbResIDTypeMatch_Base> ResIDTypeMatchBase { get; }
    public DatabinTable<pbDrop_Base> DropBase { get; }
    public DatabinTable<pbDrop_Pack> DropPack { get; }
    public DatabinTable<pbShop> Shop { get; }
    public DatabinTable<pbAvatarItem> AvatarItem { get; }
    public DatabinTable<pbAvatarSuit> AvatarSuit { get; }
    public DatabinTable<pbOutSideItem_Base> OutSideItemBase { get; }
    public DatabinTable<pbRankedMatchSeason> RankedMatchSeason { get; }
    public DatabinTable<pbRankedDivision> RankedDivision { get; }
    public DatabinTable<pbRankedMatchMapOpenTime> RankedMatchMapOpenTime { get; }
    public DatabinTable<pbRankMatchPointMedal> RankMatchPointMedal { get; }
    public DatabinTable<pbRankedMatchSeasonAward> RankedMatchSeasonAward { get; }
    public DatabinTable<pbRankedMatchSeasonTaskAward> RankedMatchSeasonTaskAward { get; }
    public DatabinTable<pbBanPickRule> BanPickRule { get; }
    public DatabinTable<pbRoundRule> RoundRule { get; }
    public DatabinTable<pbFriendlyRoomRule> FriendlyRoomRule { get; }
    public DatabinTable<pbScene_Map> SceneMap { get; }
    public DatabinTable<pbScene_OpenTime> SceneOpenTime { get; }
    public DatabinTable<pbGlobal_Base> GlobalBase { get; }
    public DatabinTable<pbMapBase> MapBase { get; }
    public DatabinTable<pbMvp_Conf> MvpConf { get; }
    public DatabinTable<pbMatchTrainer> MatchTrainer { get; }
    public DatabinTable<pbAchievement> Achievement { get; }
    public DatabinTable<pbAchievementBattleCondition> AchievementBattleCondition { get; }
    public DatabinTable<pbBattlePassSeason> BattlePassSeason { get; }
    public DatabinTable<pbBattlePassLevel> BattlePassLevel { get; }
    public DatabinTable<pbBattlePassLevelReward> BattlePassLevelReward { get; }
    public DatabinTable<pbBattlePass_SeasonTask> BattlePassSeasonTask { get; }
    public DatabinTable<pbBattlePass_TimeLimitTask> BattlePassTimeLimitTask { get; }
    public DatabinTable<pbBattlePassInfo> BattlePassInfo { get; }
    public DatabinTable<pbBattlePass_WeekIndex> BattlePassWeekIndex { get; }
    public DatabinTable<pbBattlePass_WeekTaskGroup> BattlePassWeekTaskGroup { get; }
    public DatabinTable<pbReward_Pool> RewardPool { get; }
    public DatabinTable<pbHeadIcon_Base> HeadIconBase { get; }
    public DatabinTable<pbHeadIcon> HeadIcon { get; }
    public DatabinTable<pbContestant_Card_Decoration> ContestantCardDecoration { get; }
    public DatabinTable<pbCanopy_Base> CanopyBase { get; }
    public DatabinTable<pbCardFace> CardFace { get; }
    public DatabinTable<pbPersonStyleHeadFrame> PersonStyleHeadFrame { get; }
    public DatabinTable<pbPersonalityLabel> PersonalityLabel { get; }
    public DatabinTable<pbBattleSignalCommunicate> BattleSignalCommunicate { get; }
    public DatabinTable<pbNationSelectInformation> NationSelectInformation { get; }
    public DatabinTable<pbSystemMenuList> SystemMenuList { get; }
    public DatabinTable<pbHeldItemLevelUpDesign> HeldItemLevelUpDesign { get; }
    public DatabinTable<pbHeldItemSystemConfig> HeldItemSystemConfig { get; }
    public DatabinTable<pbSystemUnlock> SystemUnlock { get; }
    public DatabinTable<pbHangupScheme> HangupScheme { get; }
    public DatabinTable<pbBadge_Base> BadgeBase { get; }
    public DatabinTable<pbPokemon_Base> PokemonBase { get; }
    public DatabinTable<pbPokemon_Release> PokemonRelease { get; }
    public DatabinTable<pbBadgeUnlock> BadgeUnlock { get; }
    public DatabinTable<pbBadgeColor> BadgeColor { get; }
    public DatabinTable<pbNewbieGuideOrder> NewbieGuideOrder { get; }
    public DatabinTable<pbNewbieGuideSteps> NewbieGuideSteps { get; }
    public DatabinTable<pbPveChallengeStageRoute> PveChallengeStageRoute { get; }
    public DatabinTable<pbAiInfoConfig> AiInfoConfig { get; }
    public DatabinTable<pbTrainerPokemon> TrainerPokemon { get; }
    public DatabinTable<pbPokemon_Avatar_Base> PokemonAvatarBase { get; }
    public DatabinTable<pbSkillEffectExpandPack_Base> SkillEffectExpandPackBase { get; }
    public DatabinTable<pbPokemon_Equipment> PokemonEquipment { get; }
    public DatabinTable<pbUnit_Base> UnitBase { get; }
    public DatabinTable<pbGuildLabel> GuildLabel { get; }
    public DatabinTable<pbGuildActivity_Base> GuildActivityBase { get; }
    public DatabinTable<pbGuildActivity_RewardPool> GuildActivityRewardPool { get; }
    public DatabinTable<pbGuildActivity_ScoreGear> GuildActivityScoreGear { get; }
    public DatabinTable<pbGuildActivity_ScoreReward> GuildActivityScoreReward { get; }
    public DatabinTable<pbTenGames> TenGames { get; }
    public DatabinTable<pbTenGamesRankReward> TenGamesRankReward { get; }
    public DatabinTable<pbTenGamesBPActivityOption> TenGamesBPActivityOption { get; }
    public DatabinTable<pbTenGamesBPActivityReward> TenGamesBPActivityReward { get; }
    public DatabinTable<pbPredictionTime> PredictionTime { get; }
    public DatabinTable<pbGuildActivity_Task> GuildActivityTask { get; }
    public int DefaultLobbyBgId { get; }

    public static GameData Load(string directory) => new(directory);

    public int GetGlobalIntData(PB_ENUM_RES_RESGLOBAL globalId) =>
        GlobalBase.Find((long)globalId)?.Value
        ?? throw new InvalidDataException($"Game data table {GlobalBase.TableName} has no row for {globalId}");

    private DatabinTable<TRow> LoadMessages<TRow>(Func<TRow, long> keySelector)
        where TRow : class, IMessage<TRow>, new()
    {
        var tableName = typeof(TRow).Name;
        var rows = ReadFile(tableName, json =>
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("the file is not a JSON array of rows");
            }

            return document.RootElement.EnumerateArray()
                .Select(row => MessageParser.Parse<TRow>(row.GetRawText()))
                .ToArray();
        });
        return new DatabinTable<TRow>(tableName, rows, keySelector);
    }

    private DatabinTable<TRow> LoadRecords<TRow>(Func<TRow, long> keySelector)
        where TRow : class
    {
        var tableName = typeof(TRow).Name;
        var rows = ReadFile(tableName, json =>
        {
            var parsed = JsonSerializer.Deserialize<TRow[]>(json, RecordOptions)
                ?? throw new InvalidDataException("the file is not a JSON array of rows");
            return parsed.Contains(null) ? throw new InvalidDataException("a row is null") : parsed;
        });
        return new DatabinTable<TRow>(tableName, rows, keySelector);
    }

    private TRow[] ReadFile<TRow>(string tableName, Func<string, TRow[]> parse)
    {
        var path = Path.Combine(directory, tableName + FileExtension);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Game data file {path} cannot be read: {exception.Message} Rebuild so the Data folder is copied to the output.",
                exception);
        }

        try
        {
            return parse(json);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException
            or InvalidProtocolBufferException or InvalidJsonException)
        {
            throw new InvalidDataException(
                $"Game data file {path} is invalid: {exception.Message} Regenerate it with tools/convert_databins.py.",
                exception);
        }
    }

    private static int FindDefaultLobbyBgId(DatabinTable<pbLobbyBg> lobbyBg)
    {
        var defaults = lobbyBg.Rows.Where(row => row.IsDefault == DefaultLobbyBgFlag).ToArray();
        return defaults.Length == 1
            ? defaults[0].LobbyBgID
            : throw new InvalidDataException(
                $"Game data table {lobbyBg.TableName} has {defaults.Length} default backgrounds; expected exactly one");
    }
}
