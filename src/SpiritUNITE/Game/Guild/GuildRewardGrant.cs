using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.PokemonAvatar;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Guild;

public sealed record GuildRewardClaim(PB_ENUM_RES_ERROR_CODE Result, ActivityRewardGroup? Rewards)
{
    public static GuildRewardClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null);

    public static GuildRewardClaim Granted(GuildRewardGrant grant, GuildRewardCommit commit) => new(
        PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
        new ActivityRewardGroup(
            commit.Wallet!,
            [.. grant.Change.CoinGrants.Keys],
            commit.SpecialCoinsBefore,
            commit.ChangedItems,
            [],
            grant.Awards)
        {
            Items = grant.Items,
        });
}

public sealed record GuildRewardGrant(
    int GuildScore,
    IReadOnlyList<RoleItemInfo> Items,
    BagChange Change,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards)
{
    public const int GuildScoreResID = (int)PB_ENUM_ENM_RESOURCES_ID.PbEnmResourcesIdGuildScore;
    private const int SingleDecoration = 1;

    public static GuildRewardGrant Empty { get; } = new(0, [], new BagChange(), []);

    public bool IsEmpty => GuildScore == 0 && Items.Count == 0 && Change.Grants.Count == 0 && Change.CoinGrants.Count == 0;

    public static GuildRewardGrant? Plan(GameData gameData, BagManager bag, LMNRoleInfo role, IEnumerable<pbDrop_Static> drops, long rewardTime)
    {
        var guildScore = 0;
        var items = new List<RoleItemInfo>();
        var coins = new Dictionary<int, long>();
        var bagGrants = new List<BagGrant>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        foreach (var drop in drops)
        {
            if (drop.ResNum <= 0)
            {
                return null;
            }

            if (drop.ResID == GuildScoreResID)
            {
                guildScore += drop.ResNum;
                awards.Add(BagManager.BuildAward(drop.ResID, drop.ResNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr));
            }
            else if (CoinID.IsGrantableCoin(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
                awards.Add(BagManager.BuildCoinAward(drop.ResID, drop.ResNum));
            }
            else if (PasterGrant.IsPaster(gameData, drop.ResID))
            {
                if (PasterGrant.Plan(gameData, bag, role, drop.ResID, drop.ResNum, rewardTime, items) is not { } paster)
                {
                    return null;
                }

                paster.AddTo(items, bagGrants, coins, awards);
            }
            else if (drop.ResNum == SingleDecoration && gameData.ContestantCardDecoration.Contains(drop.ResID))
            {
                if (!role.OwnsItem(RoleItemType.ContestantCardDecoration, drop.ResID)
                    && items.All(item => item.ItemId != drop.ResID))
                {
                    items.Add(new RoleItemInfo(RoleItemType.ContestantCardDecoration, drop.ResID, rewardTime, RoleItemInfo.Permanent, RoleItemInfo.ShowNewTag));
                    awards.Add(BagManager.BuildAward(drop.ResID, drop.ResNum, CoinID.GetResIDType(drop.ResID)));
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

        return new GuildRewardGrant(guildScore, items, new BagChange { Grants = bagGrants, CoinGrants = coins }, awards);
    }
}
