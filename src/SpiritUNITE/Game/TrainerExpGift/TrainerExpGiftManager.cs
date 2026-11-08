using System.Collections.Concurrent;
using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.TrainerExpGift;

public sealed record TrainerExpGiftClaim(PB_ENUM_RES_ERROR_CODE Result, GamePacket[] BeforeRsp, GamePacket[] AfterRsp)
{
    public static TrainerExpGiftClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, [], []);
}

public sealed class TrainerExpGiftManager(
    GameData gameData,
    LobbyNotices notices,
    BagManager bag,
    TrainerExpGiftRepository repository,
    StarRoadManager starRoad,
    TaskUpdates taskUpdates,
    TimeProvider clock,
    ILogger<TrainerExpGiftManager> logger)
{
    private const int NormalGiftType = (int)PB_ENUM_ENM_TRAINER_EXP_GIFT_TYPE.PbEnmTrainerExpGiftTypeNormal;
    private const int HasReward = 1;
    private const int TrainerLavelGiftRedDotBadgeNum = 1;
    private const int ReachedMaxSeasonGift = 1;
    private const int NotReachedMaxSeasonGift = 0;
    private const int NoDrop = 0;
    private const int TrainerLavelGiftRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdTrainerLavelGift;
    private const int LevelGiftReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonLevelGift;

    private readonly ConcurrentDictionary<ulong, FrozenSet<int>> claimedRewardExps = new();

    private readonly ILookup<int, pbDrop_Static> dropsById = gameData.DropStatic.Rows
        .OrderBy(drop => drop.SDropSeq)
        .ToLookup(drop => drop.SDropID);

    public async Task LoadAsync(ulong gameUlongUID) =>
        claimedRewardExps[gameUlongUID] = (await repository.FindClaimedRewardExpsAsync(gameUlongUID, NormalGiftType)).ToFrozenSet();

    public static int GetSeasonPoint(LMNRoleInfo role) => role.RoleTotalExp;

    public int GetReachMaxSeasonGift(LMNRoleInfo role)
    {
        var seasonId = RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId;
        var seasonGifts = gameData.TrainerSeasonExpGift.Rows.Where(gift => gift.SeasonID == seasonId).ToArray();
        return seasonGifts.Length == 0 || GetSeasonPoint(role) >= seasonGifts.Max(gift => gift.TotalExp)
            ? ReachedMaxSeasonGift
            : NotReachedMaxSeasonGift;
    }

    public pbSCQueryTrainerExpGiftRsp BuildQueryTrainerExpGiftRsp(LMNRoleInfo role)
    {
        var claimed = GetClaimed(role);
        return new pbSCQueryTrainerExpGiftRsp
        {
            TrainerExpGiftList =
            {
                FindGifts().Select(gift => new pbCSDT_TRAINER_EXP_GIFT_DETAIL
                {
                    RewardExp = gift.TotalExp,
                    StatusList =
                    {
                        GetAwardItemParams(gift).Select(param => new pbCSDT_TRAINER_EXP_GIFT_ITEM_DETAIL
                        {
                            AwardItemParam = param,
                            Status = (int)GetStatus(role, claimed, gift),
                        }),
                    },
                }),
            },
            RoleTotalExp = role.RoleTotalExp,
            RoleHistoryMaxTotalExp = role.RoleHistoryMaxTotalExp,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            SeasonPoint = GetSeasonPoint(role),
        };
    }

    public pbSCTrainerExpNextGiftInfoNtf BuildTrainerExpNextGiftInfoNtf(LMNRoleInfo role)
    {
        var claimed = GetClaimed(role);
        var next = FindGifts().FirstOrDefault(gift => !claimed.Contains(gift.TotalExp));
        return next is null
            ? new pbSCTrainerExpNextGiftInfoNtf()
            : new pbSCTrainerExpNextGiftInfoNtf
            {
                HasReward = HasReward,
                RewardExp = next.TotalExp,
                Status = (int)GetStatus(role, claimed, next),
                ShowDropId = next.AwardParam,
            };
    }

    public int CountAbleToGet(LMNRoleInfo role)
    {
        var claimed = GetClaimed(role);
        return FindGifts().Count(gift => GetStatus(role, claimed, gift) == PB_ENUM_ENUM_TRAINER_EXP_GIFT_STATUS.PbEnumTrainerExpGiftStatusAbleToGet);
    }

    public GamePacket BuildTrainerLavelGiftRedDot(LMNRoleInfo role) => CountAbleToGet(role) > 0
        ? GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = TrainerLavelGiftRedDotID, RedDotBadgeNum = TrainerLavelGiftRedDotBadgeNum } },
            },
        })
        : GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = TrainerLavelGiftRedDotID });

    public async Task<TrainerExpGiftClaim> ClaimAsync(LMNRoleInfo role, int giftType, int rewardExp)
    {
        var uid = role.GameUlongUID;
        var gift = FindGifts().FirstOrDefault(row => row.TotalExp == rewardExp);
        var claimed = GetClaimed(role);
        var refusal = gift is null || giftType != NormalGiftType ? PB_ENUM_RES_ERROR_CODE.PbErrTrainnerExpGiftInvaildGift
            : claimed.Contains(rewardExp) ? PB_ENUM_RES_ERROR_CODE.PbErrTrainnerExpGiftAlreadyGet
            : role.RoleTotalExp < rewardExp ? PB_ENUM_RES_ERROR_CODE.PbErrTrainnerExpGiftNotMeetExpGet
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        if (refusal != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Trainer exp gift {RewardExp} refused for uid {Uid}: {Result}", rewardExp, uid, refusal);
            return TrainerExpGiftClaim.Refused(refusal);
        }

        var reward = BuildReward(role, gift!);
        if (reward is null)
        {
            logger.LogWarning(
                "Trainer exp gift {RewardExp} refused for uid {Uid}: a drop of {AwardParam} has no grant rule",
                rewardExp, uid, gift!.AwardParam);
            return TrainerExpGiftClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTrainnerExpGiftNotAbleToGet);
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var commit = await repository.CommitClaimAsync(uid, NormalGiftType, rewardExp, reward.Change, reward.PokemonIds, now);
        if (commit.Wallet is not { } wallet)
        {
            logger.LogInformation("Trainer exp gift {RewardExp} refused for uid {Uid}: {Result}", rewardExp, uid, commit.Result);
            return TrainerExpGiftClaim.Refused(commit.Result);
        }

        claimedRewardExps[uid] = claimed.Append(rewardExp).ToFrozenSet();
        foreach (var pokemonId in reward.PokemonIds)
        {
            role.GrantPokemon(pokemonId, now, LevelGiftReason);
        }

        logger.LogInformation(
            "Trainer exp gift {RewardExp} claimed by uid {Uid}: {AwardCount} awards",
            rewardExp, uid, reward.Awards.Count);
        return new TrainerExpGiftClaim(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            await BuildClaimPacketsAsync(role, reward, commit, wallet),
            [BuildTrainerLavelGiftRedDot(role), GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidTrainerExpNextGiftInfoNtf, BuildTrainerExpNextGiftInfoNtf(role))]);
    }

    private async Task<GamePacket[]> BuildClaimPacketsAsync(
        LMNRoleInfo role,
        TrainerExpGiftReward reward,
        TrainerExpGiftCommit commit,
        RoleWallet wallet)
    {
        var packets = new List<GamePacket>();
        foreach (var (coinID, before) in commit.SpecialCoinsBefore)
        {
            packets.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, before, wallet.GetCoinVal(coinID))));
        }

        packets.AddRange(wallet.BuildCoinValPushes(reward.Change.CoinGrants.Keys));

        if (commit.ChangedItems.Count > 0)
        {
            packets.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify,
                BagManager.BuildOutSideItemUpdateNotify(commit.ChangedItems)));
        }

        foreach (var pokemonId in reward.PokemonIds)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemonId),
            }));
        }

        if (reward.PokemonIds.Count > 0)
        {
            packets.AddRange(await starRoad.BuildPokemonGainPacketsAsync(role));
            packets.Add(StarRoadManager.BuildAddPokemonRedDot(role));
        }
        else if (commit.SpecialCoinsBefore.TryGetValue(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, out var licensePointsBefore))
        {
            packets.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, licensePointsBefore));
        }

        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = LevelGiftReason,
            ResourcesList = { reward.Awards },
        }));
        packets.Add(GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf,
            await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)));
        return [.. packets];
    }

    private TrainerExpGiftReward? BuildReward(LMNRoleInfo role, pbTrainerExpGift gift)
    {
        var coins = new Dictionary<int, long>();
        var grants = new List<BagGrant>();
        var pokemonIds = new List<int>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        if (gift.AwardType != (int)PB_ENUM_RES_TRAINER_EXP_AWARD_TYPE.PbResTrainerExpAwardTypeDropGift)
        {
            return new TrainerExpGiftReward(new BagChange(), pokemonIds, awards);
        }

        foreach (var drop in GetAwardItemParams(gift).SelectMany(sDropID => dropsById[sDropID]))
        {
            if (CoinID.IsGrantableCoin(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
                awards.Add(BagManager.BuildCoinAward(drop.ResID, drop.ResNum));
                continue;
            }

            if (UniteLicense.IsUniteLicense(gameData, drop.ResID))
            {
                if (UniteLicense.PlanGrant(gameData, role, drop.ResID, drop.ResNum, pokemonIds) is not { } license)
                {
                    return null;
                }

                license.AddTo(pokemonIds, coins, awards);
                continue;
            }

            if (bag.CreateOutSideItemGrant(drop.ResID, drop.ResNum) is not { } grant)
            {
                return null;
            }

            grants.Add(grant);
            awards.Add(BagManager.BuildAward(drop.ResID, drop.ResNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
        }

        return new TrainerExpGiftReward(new BagChange { Grants = grants, CoinGrants = coins }, pokemonIds, awards);
    }

    private IEnumerable<pbTrainerExpGift> FindGifts()
    {
        var schemeId = notices.GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeTrainerExpGift);
        return gameData.TrainerExpGift.Rows.Where(gift => gift.SchemeId == schemeId).OrderBy(gift => gift.TotalExp);
    }

    private static IEnumerable<int> GetAwardItemParams(pbTrainerExpGift gift)
    {
        var drops = (gift.AwardItemParam ?? []).Where(param => param != NoDrop).ToArray();
        return drops.Length > 0 ? drops : [gift.AwardParam];
    }

    private static PB_ENUM_ENUM_TRAINER_EXP_GIFT_STATUS GetStatus(LMNRoleInfo role, FrozenSet<int> claimed, pbTrainerExpGift gift) =>
        claimed.Contains(gift.TotalExp) ? PB_ENUM_ENUM_TRAINER_EXP_GIFT_STATUS.PbEnumTrainerExpGiftStatusAlradyGet
        : role.RoleTotalExp >= gift.TotalExp ? PB_ENUM_ENUM_TRAINER_EXP_GIFT_STATUS.PbEnumTrainerExpGiftStatusAbleToGet
        : PB_ENUM_ENUM_TRAINER_EXP_GIFT_STATUS.PbEnumTrainerExpGiftStatusLocked;

    private FrozenSet<int> GetClaimed(LMNRoleInfo role) =>
        claimedRewardExps.TryGetValue(role.GameUlongUID, out var claimed)
            ? claimed
            : throw new InvalidOperationException($"Trainer exp gift claims for uid {role.GameUlongUID} are not loaded");

    private sealed record TrainerExpGiftReward(
        BagChange Change,
        IReadOnlyList<int> PokemonIds,
        IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards);
}
