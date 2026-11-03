using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Shop;

public sealed record ShopPurchase(
    IReadOnlyDictionary<int, long> CoinCosts,
    IReadOnlyList<RoleItemInfo> Items,
    IReadOnlyList<int> PokemonIds,
    IReadOnlyList<ShopBuyRecord> BuyRecords)
{
    private const int AwardedPokemonCount = 1;

    public pbSCResourcesAwardNotify BuildResourcesAwardNotify()
    {
        var notify = new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonShopadditem,
        };
        var awards = Items.Select(item => item.ToResourceAwardInfo())
            .Concat(PokemonIds.Select(pokemonId => new pbCOMDT_ONE_RESOURCE_AWARD_INFO
            {
                ResID = pokemonId,
                ResCnt = AwardedPokemonCount,
                ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon,
            }));
        notify.ResourcesList.Add(awards.Select(award => new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO { FinallyAwardInfo = award }));
        return notify;
    }
}
