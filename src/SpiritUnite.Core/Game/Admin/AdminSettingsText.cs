using System.Collections.Frozen;
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

namespace SpiritUnite.Game.Admin;

public sealed record AdminFieldText(string? Label, string? Help);

public static class AdminSettingsText
{
    private static readonly FrozenDictionary<(Type Type, string Property), AdminFieldText> Fields = new Dictionary<(Type Type, string Property), AdminFieldText>
    {
        [(typeof(MSDKNoticeList), nameof(MSDKNoticeList.NoticeInfoList))] = new(null, "Notices the game shows on the login screen"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeId))] = new("Notice id", "A unique number for this notice"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeType))] = new("Type", "0 for a normal notice. 1000 is the startup pop-up and 1001 or 1002 an app update pop-up, each in their matching group"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeGroup))] = new("Group", "Where the game shows it. emergency1 is the event page notice"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.BeginTime))] = new("Starts", "Shown from this UTC time. Empty means right away"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.EndTime))] = new("Ends", "Hidden after this UTC time. Empty means never"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.Order))] = new(null, "Sort order among notices"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeTitle))] = new("Title", "Up to 200 characters"),
        [(typeof(MSDKNoticeInfo), nameof(MSDKNoticeInfo.NoticeContent))] = new("Content", "Up to 8000 characters"),
        [(typeof(ServerListContent), nameof(ServerListContent.FreePokemonList))] = new("Free Pokemon", "The free rotation. Pokemon every player can use"),
        [(typeof(ServerListContent), nameof(ServerListContent.SubscriptionFreePokemonList))] = new("Subscription free Pokemon", "Pokemon listed as free with the subscription"),
        [(typeof(ServerListContent), nameof(ServerListContent.SubscriptionFreePasterList))] = new("Subscription free holowear", "Holowear listed as free with the subscription"),
        [(typeof(ServerListContent), nameof(ServerListContent.NewSaleShopIDs))] = new("New sale shop ids", "Shop row ids from pbShop marked as new sale"),
        [(typeof(LobbyOptions), nameof(LobbyOptions.SubscriptionIsOpen))] = new("Subscription open", "1 shows the subscription feature in the lobby. 0 hides it"),
        [(typeof(LobbyOptions), nameof(LobbyOptions.UserSchemes))] = new(null, "The official lobby scheme for each business type. Change only with client values"),
        [(typeof(UserSchemeOptions), nameof(UserSchemeOptions.BusinessType))] = new(null, "Lobby business type"),
        [(typeof(UserSchemeOptions), nameof(UserSchemeOptions.SchemeId))] = new(null, "Scheme used for that business type"),
        [(typeof(WalletOptions), nameof(WalletOptions.StartingCookies))] = new("Starting Aeos coins", "Aeos coins a new trainer starts with"),
        [(typeof(WalletOptions), nameof(WalletOptions.StartingPokeCoins))] = new("Starting Aeos tickets", "Aeos tickets a new trainer starts with"),
        [(typeof(WalletOptions), nameof(WalletOptions.StartingCoupons))] = new("Starting Aeos gems", "Aeos gems a new trainer starts with"),
        [(typeof(MatchOptions), nameof(MatchOptions.BotFill))] = new(null, "Fill matches with bots when too few players are searching"),
        [(typeof(MatchOptions), nameof(MatchOptions.BotFillSearchSec))] = new("Bot fill wait (seconds)", "Seconds a player searches before bots fill the match"),
        [(typeof(MatchOptions), nameof(MatchOptions.ContLoseWarmBattleLosses))] = new("Losses before a warm-up match", "Losses in a row before the next match is a warm-up match against bots"),
        [(typeof(MatchOptions), nameof(MatchOptions.WarmBattleWaitTime))] = new("Warm-up match search time", "Search time shown before a warm-up match, per map sub mode, as [min, max] seconds"),
        [(typeof(RoomMapCanUseType), nameof(RoomMapCanUseType.CanUseType))] = new("Lock type", "3 locks the maps. 4 marks them limited time and not open"),
        [(typeof(RoomMapCanUseType), nameof(RoomMapCanUseType.MapIds))] = new(null, "Map ids"),
        [(typeof(RoomOptions), nameof(RoomOptions.MapCanUseTypes))] = new("Map locks", "Lock maps on top of the official map schedule"),
        [(typeof(RoomOptions), nameof(RoomOptions.HiddenMapIds))] = new("Hidden map ids", "Map ids removed from the room map list"),
        [(typeof(RoomOptions), nameof(RoomOptions.ManMachineSearchSec))] = new("CPU match wait (seconds)", "Seconds a CPU match searches before it starts"),
        [(typeof(RoomOptions), nameof(RoomOptions.TeamMMRScore))] = new("Team rating", "Team rating sent for each player in a match. There is no rating system, so everyone gets this value"),
        [(typeof(RoomOptions), nameof(RoomOptions.SettleTeamOfflineMinSec))] = new("Teammate offline delay min (seconds)", "After a battle, teammates show as offline after a random delay. Shortest delay in seconds"),
        [(typeof(RoomOptions), nameof(RoomOptions.SettleTeamOfflineMaxSec))] = new("Teammate offline delay max (seconds)", "Longest delay in seconds"),
        [(typeof(RankedOptions), nameof(RankedOptions.WinPoint))] = new("Win points", "Points for a win, one value per rank class from Beginner up"),
        [(typeof(RankedOptions), nameof(RankedOptions.LoseBasePoint))] = new("Points kept on a loss", "Points given back on a loss, one value per rank class from Beginner up"),
        [(typeof(RankedOptions), nameof(RankedOptions.SeasonInheritRankedDivision))] = new("Season reset", "Where each rank lands when a new season starts, as old division id to new division id"),
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
        [(typeof(HeroPickOptions), nameof(HeroPickOptions.NpcHeroMaxReleaseOrder))] = new("Bot Pokemon release limit", "Bots pick only Pokemon released up to this number. 0 lets them pick every Pokemon"),
        [(typeof(HeldItemOptions), nameof(HeldItemOptions.UnlockGrantItemIds))] = new("Held items given at unlock", "Held item ids given when the first held item slot unlocks"),
        [(typeof(MegaEvolutionOptions), nameof(MegaEvolutionOptions.TrainMinComplete))] = new("Least progress per training", "Least progress one Mega Evolution training gives"),
        [(typeof(MegaEvolutionOptions), nameof(MegaEvolutionOptions.TrainMaxComplete))] = new("Most progress per training", "Most progress one Mega Evolution training gives"),
    }.ToFrozenDictionary();

    public static AdminFieldText? Find(Type type, string property) => Fields.GetValueOrDefault((type, property));
}
