namespace SpiritUNITE.Game.Lobby;

public sealed class LobbyOptions
{
    public const string SectionName = "LobbyRules";

    public CompetitionOpenOptions CompetitionOpen { get; init; } = new();
    public int StartingCreditPoint { get; init; }
    public int SubscriptionIsOpen { get; init; }
    public int AcntSysPlat { get; init; }
    public int[] NewSaleShopIDs { get; init; } = [];
    public UserSchemeOptions[] UserSchemes { get; init; } = [];
    public BubTipOptions[] RegistBubTips { get; init; } = [];
    public SubscriptionFreeOptions SubscriptionFree { get; init; } = new();
    public ProtoVarintOptions[] SubscriptionInterfaceVarints { get; init; } = [];

    public bool IsValid() =>
        CompetitionOpen.BeginTime <= CompetitionOpen.EndTime
        && CompetitionOpen.IsOpen is 0 or 1
        && StartingCreditPoint >= 0
        && SubscriptionIsOpen is 0 or 1
        && AcntSysPlat >= 0
        && NewSaleShopIDs.All(shopID => shopID > 0)
        && UserSchemes.All(scheme => scheme.BusinessType > 0 && scheme.SchemeId > 0)
        && UserSchemes.DistinctBy(scheme => scheme.BusinessType).Count() == UserSchemes.Length
        && RegistBubTips.All(bubTip => bubTip.BubTipID > 0 && bubTip.BubTipBadgeNum > 0)
        && RegistBubTips.DistinctBy(bubTip => bubTip.BubTipID).Count() == RegistBubTips.Length
        && SubscriptionFree.FreePokemonList.All(pokemonID => pokemonID > 0)
        && SubscriptionFree.FreePasterList.All(pasterID => pasterID > 0)
        && SubscriptionInterfaceVarints.All(varint => varint.FieldNumber > ProtoVarintOptions.MaxKnownSubscriptionInterfaceField
            && varint.FieldNumber <= ProtoVarintOptions.MaxFieldNumber)
        && SubscriptionInterfaceVarints.DistinctBy(varint => varint.FieldNumber).Count() == SubscriptionInterfaceVarints.Length;
}

public sealed class CompetitionOpenOptions
{
    public int IsOpen { get; init; }
    public long BeginTime { get; init; }
    public long EndTime { get; init; }
}

public sealed class BubTipOptions
{
    public int BubTipID { get; init; }
    public int BubTipBadgeNum { get; init; }
}

public sealed class SubscriptionFreeOptions
{
    public int[] FreePokemonList { get; init; } = [];
    public int[] FreePasterList { get; init; } = [];
}

public sealed class ProtoVarintOptions
{
    public const int MaxKnownSubscriptionInterfaceField = 5;
    public const int MaxFieldNumber = 536870911;

    public int FieldNumber { get; init; }
    public long Value { get; init; }
}

public sealed class UserSchemeOptions
{
    public int BusinessType { get; init; }
    public int SchemeId { get; init; }
}
