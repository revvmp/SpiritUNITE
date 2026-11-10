using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using SpiritUnite.Battle;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.HeldItem;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.MSDKNotice;
using SpiritUnite.Game.Match;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Room;
using SpiritUnite.Game.ServerContent;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Admin;

public sealed record AdminFieldChoice(string Value, string Label);

public sealed record AdminFieldText(string? Label, string? Help)
{
    public string? Pick { get; init; }
    public string? KeyPick { get; init; }
    public string? Slots { get; init; }
    public IReadOnlyList<string>? ValueLabels { get; init; }
    public IReadOnlyList<AdminFieldChoice>? Choices { get; init; }
}

public static class AdminSettingsText
{
    private const int NormalNoticeType = 0;
    private const int EmergencyNoticeType = 1000;
    private const int ForcedAppUpdateNoticeType = 1001;
    private const int OptionalAppUpdateNoticeType = 1002;
    private const int SubscriptionOpen = 1;
    private const int SubscriptionClosed = 0;

    private static readonly string PokemonList = ListOf(nameof(AdminCatalogData.Pokemon));
    private static readonly string HolowearList = ListOf(nameof(AdminCatalogData.Holowear));
    private static readonly string MapList = ListOf(nameof(AdminCatalogData.Maps));
    private static readonly string HeldItemList = ListOf(nameof(AdminCatalogData.HeldItems));
    private static readonly string ShopItemList = ListOf(nameof(AdminCatalogData.ShopItems));
    private static readonly string RankDivisionList = ListOf(nameof(AdminCatalogData.RankDivisions));
    private static readonly string RankClassList = ListOf(nameof(AdminCatalogData.RankClasses));
    private static readonly string MatchModeList = ListOf(nameof(AdminCatalogData.MatchModes));
    private static readonly string ReleaseOrderList = ListOf(nameof(AdminCatalogData.ReleaseOrders));
    private static readonly string MegaEvolutionEventList = ListOf(nameof(AdminCatalogData.MegaEvolutionEvents));

    private static readonly AdminFieldChoice[] NoticeTypes =
    [
        Choice(NormalNoticeType, "Normal notice"),
        Choice(EmergencyNoticeType, "Login pop-up"),
        Choice(ForcedAppUpdateNoticeType, "App update pop-up, must update"),
        Choice(OptionalAppUpdateNoticeType, "App update pop-up, may skip"),
    ];

    private static readonly AdminFieldChoice[] NoticeGroups =
    [
        new("emergency1", "Event page notice"),
        new("EmergencyAndroid", "Login screen, Android"),
        new("EmergencyiOS", "Login screen, iOS"),
        new("EmergencySwitch", "Login screen, Switch"),
        new("AppUpdateAndroid", "App update, Android"),
        new("AppUpdateiOS", "App update, iOS"),
        new("AppUpdateSwitch", "App update, Switch"),
        new("Emergencyenhance", "Pokemon buffs help"),
        new("Emergencyweaken", "Pokemon nerfs help"),
    ];

    private static readonly AdminFieldChoice[] MapLockTypes =
    [
        Choice(SceneMapOpening.LockedMap, "Locked"),
        Choice(SceneMapOpening.LimitedTimeMapNotOpen, "Limited time, not open now"),
    ];

    private static readonly AdminFieldChoice[] SubscriptionStates =
    [
        Choice(SubscriptionOpen, "Shown"),
        Choice(SubscriptionClosed, "Hidden"),
    ];

    private static readonly AdminFieldChoice[] BusinessTypes =
    [
        Choice((int)PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeSystemUnlock, "Feature unlocks"),
        Choice((int)PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeTrainerExpGift, "Trainer level gifts"),
        Choice((int)PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeUnclockedSeasonPoints, "Season point unlocks"),
        Choice((int)PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeNewbieChallenge, "Beginner Challenge"),
        Choice((int)PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeNewbieGift, "New trainer gifts"),
        Choice((int)PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeZeActivity, "ZE event"),
        Choice((int)PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeActivityHeldItemIntensify, "Held item boost event"),
    ];

    private static readonly string[] SearchTimeBounds = ["Shortest (seconds)", "Longest (seconds)"];

    private static readonly FrozenDictionary<(Type Type, string Property), AdminFieldText> Fields = new Dictionary<(Type Type, string Property), AdminFieldText>
    {
        [(typeof(MSDKNoticeList), nameof(MSDKNoticeList.NoticeInfoList))] = new("Notices", "Notices the game shows on the login screen"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeId))] = new("Notice number", "A unique number for this notice"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeType))] = new("Type", "Login pop-up goes with a login screen group, and the app update pop-ups with an app update group") { Choices = NoticeTypes },
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeGroup))] = new("Shown on", "Where the game shows it") { Choices = NoticeGroups },
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.BeginTime))] = new("Starts", "Shown from this UTC time. Empty means right away"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.EndTime))] = new("Ends", "Hidden after this UTC time. Empty means never"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.Order))] = new(null, "Sort order among notices"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeTitle))] = new("Title", "Up to 200 characters"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeContent))] = new("Content", "Up to 8000 characters"),
        [(typeof(ServerListContent), nameof(ServerListContent.FreePokemonList))] = new("Free Pokemon", "The free rotation. Pokemon every player can use") { Pick = PokemonList },
        [(typeof(ServerListContent), nameof(ServerListContent.SubscriptionFreePokemonList))] = new("Subscription free Pokemon", "Pokemon listed as free with the subscription") { Pick = PokemonList },
        [(typeof(ServerListContent), nameof(ServerListContent.SubscriptionFreePasterList))] = new("Subscription free holowear", "Holowear listed as free with the subscription") { Pick = HolowearList },
        [(typeof(ServerListContent), nameof(ServerListContent.NewSaleShopIDs))] = new("New sale items", "Shop items marked as new sale. One item can be in the shop several times, so check the shop and date") { Pick = ShopItemList },
        [(typeof(LobbyOptions), nameof(LobbyOptions.SubscriptionIsOpen))] = new("Subscription", "Shows or hides the subscription feature in the lobby") { Choices = SubscriptionStates },
        [(typeof(LobbyOptions), nameof(LobbyOptions.UserSchemes))] = new("Feature schemes", "Which client scheme each feature uses. Change only with client values"),
        [(typeof(UserSchemeOptions), nameof(UserSchemeOptions.BusinessType))] = new("Feature", "The game feature this scheme applies to") { Choices = BusinessTypes },
        [(typeof(UserSchemeOptions), nameof(UserSchemeOptions.SchemeId))] = new("Scheme number", "Scheme the feature uses"),
        [(typeof(WalletOptions), nameof(WalletOptions.StartingCookies))] = new("Starting Aeos coins", "Aeos coins a new trainer starts with"),
        [(typeof(WalletOptions), nameof(WalletOptions.StartingPokeCoins))] = new("Starting Aeos tickets", "Aeos tickets a new trainer starts with"),
        [(typeof(WalletOptions), nameof(WalletOptions.StartingCoupons))] = new("Starting Aeos gems", "Aeos gems a new trainer starts with"),
        [(typeof(MatchOptions), nameof(MatchOptions.BotFill))] = new(null, "Fill matches with bots when too few players are searching"),
        [(typeof(MatchOptions), nameof(MatchOptions.BotFillSearchSec))] = new("Bot fill wait (seconds)", "Seconds a player searches before bots fill the match"),
        [(typeof(MatchOptions), nameof(MatchOptions.ContLoseWarmBattleLosses))] = new("Losses before a warm-up match", "Losses in a row before the next match is a warm-up match against bots"),
        [(typeof(MatchOptions), nameof(MatchOptions.WarmBattleWaitTime))] = new("Warm-up match search time", "Search time shown before a warm-up match in each match mode") { KeyPick = MatchModeList, ValueLabels = SearchTimeBounds },
        [(typeof(RoomMapCanUseType), nameof(RoomMapCanUseType.CanUseType))] = new("Lock type", "How these maps show in the room map list") { Choices = MapLockTypes },
        [(typeof(RoomMapCanUseType), nameof(RoomMapCanUseType.MapIds))] = new("Maps", "Maps with this lock") { Pick = MapList },
        [(typeof(RoomOptions), nameof(RoomOptions.MapCanUseTypes))] = new("Map locks", "Lock maps on top of the official map schedule"),
        [(typeof(RoomOptions), nameof(RoomOptions.HiddenMapIds))] = new("Hidden maps", "Maps removed from the room map list") { Pick = MapList },
        [(typeof(RoomOptions), nameof(RoomOptions.ManMachineSearchSec))] = new("CPU match wait (seconds)", "Seconds a CPU match searches before it starts"),
        [(typeof(RoomOptions), nameof(RoomOptions.TeamMMRScore))] = new("Team rating", "Team rating sent for each player in a match. There is no rating system, so everyone gets this value"),
        [(typeof(RoomOptions), nameof(RoomOptions.SettleTeamOfflineMinSec))] = new("Teammate offline delay min (seconds)", "After a battle, teammates show as offline after a random delay. Shortest delay in seconds"),
        [(typeof(RoomOptions), nameof(RoomOptions.SettleTeamOfflineMaxSec))] = new("Teammate offline delay max (seconds)", "Longest delay in seconds"),
        [(typeof(RankedOptions), nameof(RankedOptions.WinPoint))] = new("Win points", "Points for a win in each rank class") { Slots = RankClassList },
        [(typeof(RankedOptions), nameof(RankedOptions.LoseBasePoint))] = new("Points kept on a loss", "Points given back on a loss in each rank class") { Slots = RankClassList },
        [(typeof(RankedOptions), nameof(RankedOptions.SeasonInheritRankedDivision))] = new("Season reset", "Where each rank lands when a new season starts. Ranks not listed stay where they are") { KeyPick = RankDivisionList, Pick = RankDivisionList },
        [(typeof(RankedOptions), nameof(RankedOptions.TeamMMRScore))] = new("Team rating", "Team rating sent in ranked matches"),
        [(typeof(RankedOptions), nameof(RankedOptions.FakePlayerWinPointMin))] = new("Bot points min", "Lowest points shown for bots in ranked"),
        [(typeof(RankedOptions), nameof(RankedOptions.FakePlayerWinPointMax))] = new("Bot points max", "Highest points shown for bots in ranked"),
        [(typeof(BattleSettlementOptions), nameof(BattleSettlementOptions.PveChallengeWinBaseExp))] = new("PvE challenge win exp", "Trainer exp for winning a PvE challenge"),
        [(typeof(BattleSettlementOptions), nameof(BattleSettlementOptions.PveChallengeLoseBaseExp))] = new("PvE challenge loss exp", "Trainer exp for losing a PvE challenge"),
        [(typeof(BattleSettlementOptions), nameof(BattleSettlementOptions.PveChallengeWinEnergy))] = new("PvE challenge win energy", "Energy for winning a PvE challenge"),
        [(typeof(BattleSettlementOptions), nameof(BattleSettlementOptions.MatchWinExpPerMinute))] = new("Win exp per minute", "Trainer exp per minute of map time for a win"),
        [(typeof(BattleSettlementOptions), nameof(BattleSettlementOptions.MatchLoseExpPerMinute))] = new("Loss exp per minute", "Trainer exp per minute of map time for a loss"),
        [(typeof(BattleSettlementOptions), nameof(BattleSettlementOptions.MatchWinEnergy))] = new("Win energy", "Energy for a win"),
        [(typeof(BattleSettlementOptions), nameof(BattleSettlementOptions.MatchLoseEnergyPerMinute))] = new("Loss energy per minute", "Energy per minute of map time for a loss"),
        [(typeof(BattleSettlementOptions), nameof(BattleSettlementOptions.RankedMatchWinEnergy))] = new("Ranked win energy", "Energy for a ranked win"),
        [(typeof(BattleSettlementOptions), nameof(BattleSettlementOptions.RankedMatchLoseEnergy))] = new("Ranked loss energy", "Energy for a ranked loss"),
        [(typeof(HeroPickOptions), nameof(HeroPickOptions.NpcHeroMaxReleaseOrder))] = new("Newest Pokemon bots can pick", "Bots pick only Pokemon released up to and including this one") { Pick = ReleaseOrderList },
        [(typeof(HeldItemOptions), nameof(HeldItemOptions.UnlockGrantItemIds))] = new("Held items given at unlock", "Held items given when the first held item slot unlocks") { Pick = HeldItemList },
        [(typeof(MegaEvolutionOptions), nameof(MegaEvolutionOptions.TrainResults))] = new("Training outcomes", "The progress values one training can give in each Mega Evolution event, each equally likely. Change only with official values"),
        [(typeof(MegaEvolutionTrainResultOptions), nameof(MegaEvolutionTrainResultOptions.ActivityID))] = new("Event", "The Mega Evolution event these outcomes are for") { Pick = MegaEvolutionEventList },
        [(typeof(MegaEvolutionTrainResultOptions), nameof(MegaEvolutionTrainResultOptions.TrainResults))] = new("Progress values", "Each equally likely"),
        [(typeof(MegaEvolutionOptions), nameof(MegaEvolutionOptions.FallbackTrainResults))] = new("Outcomes for other events", "Progress values for a Mega Evolution event with no published outcomes, each equally likely"),
    }.ToFrozenDictionary();

    public static AdminFieldText? Find(Type type, string property) => Fields.GetValueOrDefault((type, property));

    private static AdminFieldChoice Choice(int value, string label) => new(value.ToString(CultureInfo.InvariantCulture), label);

    private static string ListOf(string catalogProperty) => JsonNamingPolicy.CamelCase.ConvertName(catalogProperty);
}
