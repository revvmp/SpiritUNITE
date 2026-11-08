using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.BattlePass;

public sealed record BattlePassResource(int ResID, int ResNum);

public sealed record BattlePassGrant(
    IReadOnlyList<RoleItemInfo> Items,
    IReadOnlyList<int> PokemonIds,
    BagChange Change,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards)
{
    public static BattlePassGrant Empty { get; } = new([], [], new BagChange(), []);
}

public sealed class BattlePassRewards(GameData gameData, BagManager bag)
{
    private const int NoAward = 0;
    private const int SinglePermanentItem = 1;

    public static IEnumerable<BattlePassResource> FindAwards(pbBattlePassLevelReward reward, PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE type) =>
        type == PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE.PbEnmBattlepassLevelRewardTypeCommon
            ? reward.CommonAwardID == NoAward || reward.CommonAwardNum <= 0
                ? []
                : [new BattlePassResource(reward.CommonAwardID, reward.CommonAwardNum)]
            : reward.EliteAward
                .Where(award => award.ID != NoAward && award.Num > 0)
                .Select(award => new BattlePassResource(award.ID, award.Num));

    public static bool HasAwards(pbBattlePassLevelReward reward, PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE type) => FindAwards(reward, type).Any();

    public static pbCOMDT_ONE_RESOURCE_AWARD_INFO ToAwardInfo(BattlePassResource resource) => new()
    {
        ResID = resource.ResID,
        ResCnt = resource.ResNum,
        ResType = (int)(CoinID.IsGrantableCoin(resource.ResID)
            ? CoinID.GetCoinResourceType(resource.ResID)
            : CoinID.GetResIDType(resource.ResID)),
    };

    public BattlePassGrant? Plan(LMNRoleInfo role, IEnumerable<BattlePassResource> resources, long gainTime)
    {
        var items = new List<RoleItemInfo>();
        var pokemonIds = new List<int>();
        var coins = new Dictionary<int, long>();
        var bagGrants = new List<BagGrant>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        foreach (var resource in resources)
        {
            if (resource.ResNum <= 0)
            {
                return null;
            }

            if (CoinID.IsGrantableCoin(resource.ResID))
            {
                coins[resource.ResID] = coins.GetValueOrDefault(resource.ResID) + resource.ResNum;
                awards.Add(BagManager.BuildCoinAward(resource.ResID, resource.ResNum));
            }
            else if (UniteLicense.IsUniteLicense(gameData, resource.ResID))
            {
                if (UniteLicense.PlanGrant(gameData, role, resource.ResID, resource.ResNum, pokemonIds) is not { } license)
                {
                    return null;
                }

                license.AddTo(pokemonIds, coins, awards);
            }
            else if (PasterGrant.IsPaster(gameData, resource.ResID))
            {
                if (PasterGrant.Plan(gameData, bag, role, resource.ResID, resource.ResNum, gainTime, items) is not { } paster)
                {
                    return null;
                }

                paster.AddTo(items, bagGrants, coins, awards);
            }
            else if (resource.ResNum == SinglePermanentItem && FindPermanentItems(resource.ResID) is { } permanentItems)
            {
                foreach (var (itemType, itemId) in permanentItems)
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
            else if (bag.CreateOutSideItemGrant(resource.ResID, resource.ResNum) is { } bagGrant)
            {
                bagGrants.Add(bagGrant);
                awards.Add(BagManager.BuildAward(resource.ResID, resource.ResNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
            }
            else
            {
                return null;
            }
        }

        return new BattlePassGrant(items, pokemonIds, new BagChange { Grants = bagGrants, CoinGrants = coins }, awards);
    }

    private (RoleItemType ItemType, int ItemId)[]? FindPermanentItems(int resID)
    {
        if (gameData.AvatarItem.Contains(resID))
        {
            return [(RoleItemType.AvatarItem, resID)];
        }

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

        if (gameData.HeadIconBase.Contains(resID))
        {
            return [(RoleItemType.HeadIcon, resID)];
        }

        if (gameData.ContestantCardDecoration.Contains(resID))
        {
            return [(RoleItemType.ContestantCardDecoration, resID)];
        }

        return gameData.LobbyBg.Contains(resID) && resID != gameData.DefaultLobbyBgId
            ? [(RoleItemType.LobbyBg, resID)]
            : null;
    }
}
