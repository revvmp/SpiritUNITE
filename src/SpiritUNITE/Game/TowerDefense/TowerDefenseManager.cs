using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Game.TowerDefense;

public sealed record TowerDefenseOutcome(PB_ENUM_RES_ERROR_CODE Result, GamePacket[] BeforeRsp)
{
    public static TowerDefenseOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed class TowerDefenseManager(
    GameData gameData,
    LobbyNotices notices,
    TowerDefenseRepository repository,
    ActivityManager activityManager,
    ActivityRewardNotices rewardNotices,
    TimeProvider clock,
    ILogger<TowerDefenseManager> logger)
{
    private const int StatusOpen = 1;
    private const int StatusExchange = 2;
    private const int IsBuffRow = 1;
    private const int FirstBuffLevel = 1;
    private const int NoTime = 0;
    private const int MinShopCnt = 1;
    private const int NoNewUnLock = 0;
    private const PB_ENUM_ENM_RESOURCE_CHG_REASON ShopBuyReason = PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTowerDefenseShopBuy;

    public async Task<pbSCQueryTowerDefenseRsp> BuildQueryRspAsync(ulong uid)
    {
        var period = notices.BuildQueryTowerDefenseNtf();
        var state = await repository.FindAsync(uid, period.PeriodID);
        return new pbSCQueryTowerDefenseRsp
        {
            ResultInfo = new pbResultInfo(),
            TowerDefenseInfo = new pbCOMDT_TOWER_DEFENSE_INFO
            {
                PeriodID = period.PeriodID,
                Status = period.Status,
                Score = state.Score,
                PassAwardInfo = new pbCOMDT_TOWER_DEFENSE_PASS_AWARD_INFO(),
                BuffInfo = new pbCOMDT_TOWER_DEFENSE_BUFF_INFO
                {
                    BuffList =
                    {
                        state.Buffs.Select(buff => new pbCOMDT_TOWER_DEFENSE_BUFF_ONE_INFO
                        {
                            BuffID = buff.BuffID,
                            BuffLevel = buff.BuffLevel,
                            IsNewUnLock = buff.IsNewUnLock,
                            AttrID = buff.AttrID,
                        }),
                    },
                },
                UnlockGameLevelInfo = BuildUnlockGameLevelInfo(period.PeriodID, state),
                ShopBuyInfo = new pbCOMDT_TOWER_DEFENSE_SHOP_BUY_INFO
                {
                    ShopList =
                    {
                        state.ShopBuyNums
                            .OrderBy(shop => shop.Key)
                            .Select(shop => new pbCOMDT_TOWER_DEFENSE_SHOP_BUY_ONE_INFO { ShopID = shop.Key, ShopBuyNum = shop.Value }),
                    },
                },
            },
        };
    }

    public async Task<TowerDefenseOutcome> BuyAsync(LMNRoleInfo role, int shopId, int shopCnt)
    {
        var uid = role.GameUlongUID;
        var period = notices.BuildQueryTowerDefenseNtf();
        if (!IsShopOpen(period))
        {
            return Refuse("shop buy", uid, PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseDisplayEnd);
        }

        var cfgNow = UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());
        if (shopCnt < MinShopCnt
            || gameData.TowerDefenseShop.Find(pbTowerDefenseShop.Key(shopId, period.PeriodID)) is not { } shop
            || !IsOnSale(shop, cfgNow))
        {
            return Refuse("shop buy", uid, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        var state = await repository.FindAsync(uid, period.PeriodID);
        if (shop.GameLevel > state.MaxPassGameLevel)
        {
            return Refuse("shop buy", uid, PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseShopBuyLevelLimit);
        }

        var buff = shop.IsBuff == IsBuffRow ? PlanBoughtBuff(shop, state) : null;
        if (shop.IsBuff == IsBuffRow && buff is null)
        {
            return Refuse("shop buy", uid, PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseShopBuyLevelLimit);
        }

        if ((long)state.ShopBuyNums.GetValueOrDefault(shopId) + shopCnt > shop.BuyNumLimit)
        {
            return Refuse("shop buy", uid, PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseShopBuyNumLimit);
        }

        var scoreCost = (long)shop.Score * shopCnt;
        if (scoreCost > state.Score)
        {
            return Refuse("shop buy", uid, PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseShopBuyScoreLimit);
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var grant = buff is null ? activityManager.PlanResourceGrant(role, shop.ResID, shopCnt, now) : ActivityDropRewards.Merge([]);
        if (grant is null)
        {
            return Refuse("shop buy", uid, PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var change = ActivityDropRewards.ToBagChange(grant, ShopBuyReason, now, new BagChange());
        var buy = new TowerDefenseShopBuy(shopId, shopCnt, shop.BuyNumLimit, (int)scoreCost, buff);
        var commit = await repository.CommitShopBuyAsync(uid, period.PeriodID, buy, change, now);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return Refuse("shop buy", uid, commit.Result);
        }

        ActivityManager.GrantToRole(role, grant, now, ShopBuyReason);
        logger.LogInformation(
            "Tower defense period {PeriodID} shop {ShopId} x{ShopCnt} bought by uid {Uid} for {ScoreCost} score",
            period.PeriodID, shopId, shopCnt, uid, scoreCost);
        if (buff is not null)
        {
            return new TowerDefenseOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, []);
        }

        return new TowerDefenseOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
        [
            .. await rewardNotices.BuildAsync(role, ActivityDropRewards.ToRewardGroup(grant, change, commit)),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = (int)ShopBuyReason,
                ResourcesList = { grant.Awards },
            }),
        ]);
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> LevelUpBuffAsync(ulong uid, int buffID)
    {
        var period = notices.BuildQueryTowerDefenseNtf();
        if (!IsShopOpen(period))
        {
            return Refuse("buff level up", uid, PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseDisplayEnd).Result;
        }

        var state = await repository.FindAsync(uid, period.PeriodID);
        if (state.Buffs.FirstOrDefault(buff => buff.BuffID == buffID) is not { } owned
            || FindBuffGroup(period.PeriodID, buffID) is not { } groupId
            || FindBuffDesign(groupId, owned.BuffLevel) is not { } current
            || FindBuffDesign(groupId, owned.BuffLevel + 1) is not { } next)
        {
            return Refuse("buff level up", uid, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid).Result;
        }

        if (current.GameLevel > state.MaxPassGameLevel)
        {
            return Refuse("buff level up", uid, PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseLevelUpGameLevelLimit).Result;
        }

        if (current.LevelUpCostScore > state.Score)
        {
            return Refuse("buff level up", uid, PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseLevelUpScoreLimit).Result;
        }

        var result = await repository.LevelUpBuffAsync(
            uid,
            period.PeriodID,
            owned,
            owned with { BuffLevel = next.Level, AttrID = next.Id },
            current.LevelUpCostScore);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return Refuse("buff level up", uid, result).Result;
        }

        logger.LogInformation(
            "Tower defense buff {BuffID} raised to level {Level} by uid {Uid} for {Score} score",
            buffID, next.Level, uid, current.LevelUpCostScore);
        return result;
    }

    public async Task ClearBuffNewTagAsync(ulong uid)
    {
        var period = notices.BuildQueryTowerDefenseNtf();
        await repository.ClearBuffNewTagAsync(uid, period.PeriodID);
    }

    private pbCOMDT_TOWER_DEFENSE_UNLOCK_GAME_LEVEL_INFO BuildUnlockGameLevelInfo(int periodID, TowerDefenseState state)
    {
        var maps = gameData.TowerDefensePassAward.Rows
            .Where(award => award.PeriodID == periodID)
            .Select(award => gameData.SceneMap.Find(award.SceneId))
            .OfType<pbScene_Map>()
            .OrderBy(scene => scene.GameLevel)
            .ToArray();
        if (maps.Length == 0)
        {
            return new pbCOMDT_TOWER_DEFENSE_UNLOCK_GAME_LEVEL_INFO { MaxPassGameLevel = state.MaxPassGameLevel };
        }

        var unlocked = maps.FirstOrDefault(scene => scene.GameLevel > state.MaxPassGameLevel) ?? maps[^1];
        return new pbCOMDT_TOWER_DEFENSE_UNLOCK_GAME_LEVEL_INFO
        {
            MaxGameLevel = unlocked.GameLevel,
            MapID = unlocked.SceneId,
            IsNewUnLock = state.IsNewUnLock,
            MaxPassGameLevel = state.MaxPassGameLevel,
        };
    }

    private TowerDefenseBuff? PlanBoughtBuff(pbTowerDefenseShop shop, TowerDefenseState state)
    {
        var ownedLevel = state.Buffs.FirstOrDefault(buff => buff.BuffID == shop.ResID)?.BuffLevel ?? FirstBuffLevel - 1;
        return shop.Level == ownedLevel + 1 && FindBuffDesign(shop.GroupId, shop.Level) is { } design
            ? new TowerDefenseBuff(shop.ResID, shop.Level, design.Id, NoNewUnLock)
            : null;
    }

    private int? FindBuffGroup(int periodID, int buffID) =>
        gameData.TowerDefenseShop.Rows.FirstOrDefault(shop => shop.PeriodID == periodID && shop.IsBuff == IsBuffRow && shop.ResID == buffID)?.GroupId;

    private pbTowerDefenseBuffLevelUpDesign? FindBuffDesign(int groupId, int level) =>
        gameData.TowerDefenseBuffLevelUpDesign.Rows.FirstOrDefault(design => design.GroupId == groupId && design.Level == level);

    private static bool IsShopOpen(pbSCQueryTowerDefenseNtf period) =>
        period.Status is StatusOpen or StatusExchange;

    private static bool IsOnSale(pbTowerDefenseShop shop, long cfgNow) =>
        (shop.OnTime == NoTime || shop.OnTime <= cfgNow) && (shop.OffTime == NoTime || cfgNow <= shop.OffTime);

    private TowerDefenseOutcome Refuse(string action, ulong uid, PB_ENUM_RES_ERROR_CODE result)
    {
        logger.LogInformation("Tower defense {Action} refused for uid {Uid}: {Result}", action, uid, result);
        return TowerDefenseOutcome.Refused(result);
    }
}
