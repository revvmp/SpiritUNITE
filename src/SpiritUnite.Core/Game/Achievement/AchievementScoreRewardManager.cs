using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.StarRoad;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Achievement;

public sealed record AchievementScoreRewardClaim(
    PB_ENUM_RES_ERROR_CODE Result,
    IReadOnlyList<GamePacket> BeforeRsp,
    pbCSDT_ACHIEVEMENT_SCORE_REWARD_DETAIL_LIST AchievementInfo);

public sealed class AchievementScoreRewardManager(
    GameData gameData,
    AchievementManager achievements,
    AchievementRepository repository,
    BagManager bag,
    StarRoadManager starRoad,
    TaskUpdates taskUpdates,
    TimeProvider clock,
    ILogger<AchievementScoreRewardManager> logger)
{
    private const int ScoreGiftAwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonAchivementScoreGift;

    private readonly ILookup<int, pbDrop_Static> dropsById =
        gameData.DropStatic.Rows.OrderBy(drop => drop.SDropSeq).ToLookup(drop => drop.SDropID);

    public async Task<pbCSDT_ACHIEVEMENT_SCORE_REWARD_DETAIL_LIST> BuildDetailListAsync(ulong gameUlongUID, int score) =>
        BuildDetailList(score, await repository.FindScoreRewardScoresAsync(gameUlongUID));

    public async Task<AchievementScoreRewardClaim> ClaimAsync(LMNRoleInfo role, int totalScore)
    {
        var uid = role.GameUlongUID;
        var score = await achievements.GetScoreAsync(uid);
        var claimedScores = await repository.FindScoreRewardScoresAsync(uid);
        var refusal = FindRefusal(totalScore, score, claimedScores);
        if (refusal is { } refused)
        {
            logger.LogInformation(
                "Achievement score reward {TotalScore} refused for uid {Uid} at score {Score}: {Result}",
                totalScore, uid, score, refused);
            return new(refused, [], BuildDetailList(score, claimedScores));
        }

        var gift = gameData.AchievementScoreGift.Find(totalScore)!;
        var rewardTime = clock.GetUtcNow().ToUnixTimeSeconds();
        if (PlanGrant(role, gift.DropID, rewardTime) is not { } grant)
        {
            logger.LogWarning(
                "Achievement score reward {TotalScore} refused for uid {Uid}: drop {DropID} has a reward with no grant rule",
                totalScore, uid, gift.DropID);
            return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, [], BuildDetailList(score, claimedScores));
        }

        var commit = await repository.CommitScoreRewardAsync(uid, totalScore, grant.Change, rewardTime);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || commit.Wallet is not { } wallet)
        {
            logger.LogInformation("Achievement score reward {TotalScore} refused for uid {Uid}: {Result}", totalScore, uid, commit.Result);
            return new(commit.Result, [], await BuildDetailListAsync(uid, score));
        }

        foreach (var pokemon in grant.Change.PokemonGrants)
        {
            role.GrantPokemon(pokemon.PokemonId, rewardTime, ScoreGiftAwardReason);
        }

        logger.LogInformation(
            "Achievement score reward {TotalScore} claimed by uid {Uid}: drop {DropID}, {RewardCount} rewards granted",
            totalScore, uid, gift.DropID, grant.Awards.Count);
        var claimed = new HashSet<int>(claimedScores) { totalScore };
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, await BuildGrantPushesAsync(role, grant, commit, wallet), BuildDetailList(score, claimed));
    }

    private PB_ENUM_RES_ERROR_CODE? FindRefusal(int totalScore, int score, IReadOnlySet<int> claimedScores) =>
        gameData.AchievementScoreGift.Find(totalScore) is null
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid
            : claimedScores.Contains(totalScore)
                ? PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked
                : score < totalScore
                    ? PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid
                    : null;

    private pbCSDT_ACHIEVEMENT_SCORE_REWARD_DETAIL_LIST BuildDetailList(int score, IReadOnlySet<int> claimedScores) => new()
    {
        AchievementInfoList =
        {
            gameData.AchievementScoreGift.Rows
                .OrderBy(gift => gift.TotalScore)
                .Select(gift => new pbCSDT_ACHIEVEMENT_SCORE_REWARD_DETAIL
                {
                    Score = gift.TotalScore,
                    Status = (int)RewardStatusOf(gift.TotalScore, score, claimedScores),
                }),
        },
    };

    private static PB_ENUM_ENUM_ACHIEVEMENT_REWARD_STATUS RewardStatusOf(int totalScore, int score, IReadOnlySet<int> claimedScores) =>
        claimedScores.Contains(totalScore)
            ? PB_ENUM_ENUM_ACHIEVEMENT_REWARD_STATUS.PbEnumAchievementRewardStatusAlradyGet
            : score >= totalScore
                ? PB_ENUM_ENUM_ACHIEVEMENT_REWARD_STATUS.PbEnumAchievementRewardStatusAbleToGet
                : PB_ENUM_ENUM_ACHIEVEMENT_REWARD_STATUS.PbEnumAchievementRewardStatusLocked;

    private ScoreRewardGrant? PlanGrant(LMNRoleInfo role, int dropID, long rewardTime)
    {
        var coins = new Dictionary<int, long>();
        var grants = new List<BagGrant>();
        var pokemonIds = new List<int>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        foreach (var drop in dropsById[dropID])
        {
            if (drop.ResNum <= 0)
            {
                return null;
            }

            if (CoinID.IsGrantableCoin(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
                awards.Add(BagManager.BuildCoinAward(drop.ResID, drop.ResNum));
            }
            else if (UniteLicense.IsUniteLicense(gameData, drop.ResID))
            {
                if (UniteLicense.PlanGrant(gameData, role, drop.ResID, drop.ResNum, pokemonIds) is not { } license)
                {
                    return null;
                }

                license.AddTo(pokemonIds, coins, awards);
            }
            else if (bag.CreateOutSideItemGrant(drop.ResID, drop.ResNum) is { } bagGrant)
            {
                grants.Add(bagGrant);
                awards.Add(BagManager.BuildAward(drop.ResID, drop.ResNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
            }
            else
            {
                return null;
            }
        }

        return awards.Count == 0
            ? null
            : new ScoreRewardGrant(
                new BagChange
                {
                    Grants = grants,
                    CoinGrants = coins,
                    PokemonGrants =
                    [
                        .. pokemonIds.Select(pokemonId =>
                            new RolePokemonInfo(pokemonId, rewardTime, ScoreGiftAwardReason, RolePokemonInfo.HasNewTag)),
                    ],
                },
                awards);
    }

    private async Task<IReadOnlyList<GamePacket>> BuildGrantPushesAsync(
        LMNRoleInfo role,
        ScoreRewardGrant grant,
        BagCommit commit,
        RoleWallet wallet)
    {
        var pushes = new List<GamePacket>();
        foreach (var coinID in grant.Change.CoinGrants.Keys.Where(CoinID.IsSpecialCoin))
        {
            pushes.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, commit.BeforeCoinNums.GetValueOrDefault(coinID), wallet.GetCoinVal(coinID))));
        }

        foreach (var pokemon in grant.Change.PokemonGrants)
        {
            pushes.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemon.PokemonId),
            }));
        }

        if (grant.Change.PokemonGrants.Count > 0)
        {
            pushes.AddRange(await starRoad.BuildPokemonGainPacketsAsync(role));
            pushes.Add(StarRoadManager.BuildAddPokemonRedDot(role));
        }
        else if (commit.BeforeCoinNums.TryGetValue(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, out var licensePointsBefore))
        {
            pushes.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, licensePointsBefore));
        }

        pushes.AddRange(wallet.BuildCoinValPushes(grant.Change.CoinGrants.Keys));
        if (commit.ChangedItems.Count > 0)
        {
            pushes.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(commit.ChangedItems)));
        }

        pushes.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = ScoreGiftAwardReason,
            ResourcesList = { grant.Awards },
        }));
        pushes.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)));
        return pushes;
    }

    private sealed record ScoreRewardGrant(BagChange Change, IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards);
}
