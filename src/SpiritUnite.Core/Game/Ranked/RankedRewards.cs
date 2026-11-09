using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Ranked;

public sealed record RankedGrant(
    IReadOnlyList<RoleItemInfo> Items,
    BagChange Change,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards)
{
    public static RankedGrant Empty { get; } = new([], new BagChange(), []);

    public bool IsEmpty => Items.Count == 0 && Change.Grants.Count == 0 && Change.CoinGrants.Count == 0 && Change.Costs.Count == 0;
}

public sealed class RankedRewards(GameData gameData, BagManager bag)
{
    private const int SinglePermanentItem = 1;

    private readonly ILookup<int, pbDrop_Static> dropsById = gameData.DropStatic.Rows
        .OrderBy(drop => drop.SDropSeq)
        .ToLookup(drop => drop.SDropID);

    public bool HasDrops(int sDropID) => dropsById[sDropID].Any();

    public RankedGrant? Plan(LMNRoleInfo role, IEnumerable<int> sDropIDs, long gainTime)
    {
        var items = new List<RoleItemInfo>();
        var coins = new Dictionary<int, long>();
        var bagGrants = new List<BagGrant>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        foreach (var drop in sDropIDs.SelectMany(sDropID => dropsById[sDropID]))
        {
            if (drop.ResNum <= 0)
            {
                return null;
            }

            if (CoinID.WalletCoins.Contains(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
                awards.Add(BagManager.BuildCoinAward(drop.ResID, drop.ResNum));
            }
            else if (CosmeticGrant.FindItemType(gameData, drop.ResID) is not null)
            {
                if (CosmeticGrant.Plan(gameData, bag, role, drop.ResID, drop.ResNum, gainTime, items) is not { } cosmetic)
                {
                    return null;
                }

                cosmetic.AddTo(items, bagGrants, coins, awards);
            }
            else if (drop.ResNum == SinglePermanentItem && FindPermanentItems(drop.ResID) is { } owned)
            {
                foreach (var (itemType, itemId) in owned)
                {
                    if (role.OwnsItem(itemType, itemId) || items.Any(item => item.ItemType == itemType && item.ItemId == itemId))
                    {
                        continue;
                    }

                    var item = new RoleItemInfo(itemType, itemId, gainTime, RoleItemInfo.Permanent, RoleItemInfo.ShowNewTag);
                    items.Add(item);
                    awards.Add(new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
                    {
                        FinallyAwardInfo = item.ToResourceAwardInfo(),
                        OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
                    });
                }
            }
            else if (bag.CreateOutSideItemGrant(drop.ResID, drop.ResNum) is { } bagGrant)
            {
                bagGrants.Add(bagGrant);
                awards.Add(BagManager.BuildAward(drop.ResID, drop.ResNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
            }
            else
            {
                return null;
            }
        }

        return new RankedGrant(items, new BagChange { Grants = bagGrants, CoinGrants = coins }, awards);
    }

    private (RoleItemType ItemType, int ItemId)[]? FindPermanentItems(int resID)
    {
        if (gameData.AvatarSuit.Find(resID) is { } avatarSuit)
        {
            return
            [
                .. avatarSuit.ContianAvatarID
                    .Where(avatarID => gameData.AvatarItem.Contains(avatarID))
                    .Distinct()
                    .Select(avatarID => (RoleItemType.AvatarItem, avatarID)),
            ];
        }

        return gameData.LobbyBg.Contains(resID) && resID != gameData.DefaultLobbyBgId
            ? [(RoleItemType.LobbyBg, resID)]
            : null;
    }
}
