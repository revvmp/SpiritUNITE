using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Databin;

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
        SceneMap = LoadMessages<pbScene_Map>(row => row.SceneId);
        MapBase = LoadMessages<pbMapBase>(row => row.MapBaseId);
        MvpConf = LoadRecords<pbMvp_Conf>(row => pbMvp_Conf.Key(row.GameType, row.ParamType));
        Achievement = LoadRecords<pbAchievement>(row => row.AchievementID);
        BattlePassSeason = LoadMessages<pbBattlePassSeason>(row => row.SeasonID);
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
        UnitBase = LoadMessages<pbUnit_Base>(row => row.UnitId);
        NationGroup = LoadRecords<pbNationGroup>(row => row.NationGroupID);
        TowerDefenseBase = LoadRecords<pbTowerDefenseBase>(row => row.PeriodID);
        SubScriptionPeriod = LoadRecords<pbSubScriptionPeriod>(row => row.ID);
        TrainerExpGift = LoadRecords<pbTrainerExpGift>(row => pbTrainerExpGift.Key(row.SchemeId, row.TotalExp));
        TrainerSeasonExpGift = LoadRecords<pbTrainerSeasonExpGift>(row => pbTrainerSeasonExpGift.Key(row.SeasonID, row.TotalExp));
        PasterSpray = LoadRecords<pbPasterSpray>(row => row.PasterSprayID);
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
    public DatabinTable<pbNewbieChallengeTask> NewbieChallengeTask { get; }
    public DatabinTable<pbStarRoadPokemon> StarRoadPokemon { get; }
    public DatabinTable<pbActivity_Base> ActivityBase { get; }
    public DatabinTable<pbNationGroup> NationGroup { get; }
    public DatabinTable<pbTowerDefenseBase> TowerDefenseBase { get; }
    public DatabinTable<pbSubScriptionPeriod> SubScriptionPeriod { get; }
    public DatabinTable<pbTrainerExpGift> TrainerExpGift { get; }
    public DatabinTable<pbTrainerSeasonExpGift> TrainerSeasonExpGift { get; }
    public DatabinTable<pbPasterSpray> PasterSpray { get; }
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
    public DatabinTable<pbScene_Map> SceneMap { get; }
    public DatabinTable<pbMapBase> MapBase { get; }
    public DatabinTable<pbMvp_Conf> MvpConf { get; }
    public DatabinTable<pbAchievement> Achievement { get; }
    public DatabinTable<pbBattlePassSeason> BattlePassSeason { get; }
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
    public DatabinTable<pbUnit_Base> UnitBase { get; }
    public int DefaultLobbyBgId { get; }

    public static GameData Load(string directory) => new(directory);

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
