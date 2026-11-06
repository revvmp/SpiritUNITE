using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.HeldItem;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.PveChallenge;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.TrainerExpGift;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Newbie;

public sealed class NewbieManager(
    GameData gameData,
    PveChallengeProgress pveChallenge,
    LobbyNotices notices,
    PokemonManager pokemon,
    HeldItemManager heldItems,
    TaskUpdates taskUpdates,
    TrainerExpGifts trainerExpGifts,
    StarRoadManager starRoad,
    SettingsManager settings,
    RoleRepository roleRepository,
    NewbieDropRepository newbieDrops,
    TimeProvider clock,
    ILogger<NewbieManager> logger)
{
    public const int TutorialSkipID = 1;

    private const int LicensePokemonParamIndex = 0;
    private const int Replaced = 1;
    private const int NoDrop = 0;
    private const int HasTrainerLevelGift = 1;
    private const int NoPokemonPointTarget = 0;
    private const int AcntExpResID = (int)PB_ENUM_ENM_RESOURCES_ID.PbEnmResourcesIdAcntExp;
    private const int TutorialRewardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTutorialReward;
    private const int AddPokemonRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdAddPokemon;
    private const int PveChallengeNormalRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdPveChallengeNormal;
    private const int TrainerLavelGiftRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdTrainerLavelGift;

    private static readonly int[] TutorialBattleNewbies =
    [
        (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeTutorialRound1,
        (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeTutorialRound2,
    ];

    private readonly IReadOnlyDictionary<int, int> dropByNewbie = gameData.NewbieGuideOrder.Rows
        .Where(order => order.SDropID != NoDrop)
        .GroupBy(order => order.NewbieEnum)
        .ToDictionary(group => group.Key, group => group.First().SDropID);

    private readonly ILookup<int, pbDrop_Static> dropsById = gameData.DropStatic.Rows
        .OrderBy(drop => drop.SDropSeq)
        .ToLookup(drop => drop.SDropID);

    public async Task<GamePacket[]> ArchiveAsync(LMNRoleInfo role, int newbie)
    {
        if (!role.TrySetNewbieState(newbie))
        {
            return [];
        }

        var archive = new NewbieArchive(newbie, role.BuildNewbieState());
        logger.LogInformation("Newbie {Newbie} archived for uid {Uid}", newbie, role.GameUlongUID);
        return [.. CompletePveChallenge(role, newbie), .. await GrantGuideOrderDropAsync(role, archive)];
    }

    public async Task<GamePacket[]> SkipTutorialAsync(LMNRoleInfo role)
    {
        var grants = new List<GamePacket>();
        foreach (var newbie in TutorialBattleNewbies)
        {
            grants.AddRange(await ArchiveAsync(role, newbie));
        }

        return [.. grants];
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, GamePacket[] Grants)> GetTutorialRewardAsync(LMNRoleInfo role, int pokemonId)
    {
        var followBefore = role.FollowPokemonId;
        var result = role.GetTutorialReward(gameData, pokemonId, clock.GetUtcNow().ToUnixTimeSeconds(), TutorialRewardReason);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return (result, []);
        }

        return (result,
        [
            .. BuildPokemonGain(role, pokemonId, followBefore),
            .. await starRoad.BuildPokemonGainPacketsAsync(role),
            BuildAddPokemonRedDot(role),
            await BuildOwnershipUpdateTaskInfoNtfAsync(role),
        ]);
    }

    public Task<long> GetRegisterTimeAsync(LMNRoleInfo role) => roleRepository.FindCreateTimeAsync(role.GameUlongUID);

    public static pbSCNewbieStateInfoNtf BuildNewbieStateInfoNtf(LMNRoleInfo role) =>
        new() { NewbieState = role.BuildNewbieState() };

    public static GamePacket BuildAddPokemonRedDot(LMNRoleInfo role) =>
        BuildRedDotReplaceNtf(AddPokemonRedDotID, role.CountNewTagPokemon());

    private IEnumerable<GamePacket> CompletePveChallenge(LMNRoleInfo role, int newbie)
    {
        var completions = pveChallenge.CompleteByNewbie(newbie);
        if (completions.Length == 0)
        {
            return [];
        }

        var openNormalNodes = pveChallenge.Load(role)
            .Count(node => node.DiffLevel == PveChallengeProgress.NormalDifficultyLevel && !node.IsComplete);
        return
        [
            BuildRedDotReplaceNtf(PveChallengeNormalRedDotID, openNormalNodes),
            .. completions.Select(ntf => GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeStageInfoNtf, ntf)),
        ];
    }

    private async Task<IEnumerable<GamePacket>> GrantGuideOrderDropAsync(LMNRoleInfo role, NewbieArchive archive)
    {
        if (!dropByNewbie.TryGetValue(archive.Newbie, out var sDropID))
        {
            return [];
        }

        var drops = dropsById[sDropID].ToArray();
        var grants = new List<GamePacket>();
        var specialCoins = drops
            .Where(drop => CoinID.IsSpecialCoin(drop.ResID))
            .GroupBy(drop => drop.ResID)
            .ToDictionary(group => group.Key, group => group.Sum(drop => (long)drop.ResNum));
        if (specialCoins.Count > 0)
        {
            grants.AddRange(await GrantSpecialCoinsAsync(role, sDropID, archive, specialCoins));
        }

        foreach (var drop in drops.Where(drop => !CoinID.IsSpecialCoin(drop.ResID)))
        {
            grants.AddRange(await GrantDropAsync(role, drop));
        }

        return grants;
    }

    private async Task<IEnumerable<GamePacket>> GrantSpecialCoinsAsync(
        LMNRoleInfo role,
        int sDropID,
        NewbieArchive archive,
        IReadOnlyDictionary<int, long> specialCoins)
    {
        var commit = await newbieDrops.ArchiveWithCoinsAsync(role.GameUlongUID, archive.NewbieStateAfter, specialCoins);
        if (commit is null)
        {
            logger.LogError(
                "Newbie drop {SDropID} for uid {Uid} is not granted: newbie {Newbie} has no stored role to archive into",
                sDropID,
                role.GameUlongUID,
                archive.Newbie);
            return [];
        }

        var grants = new List<GamePacket>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        foreach (var (coinID, amount) in specialCoins.OrderBy(coin => coin.Key))
        {
            var beforeCoinNum = commit.BeforeCoinNums.GetValueOrDefault(coinID);
            logger.LogInformation(
                "Newbie drop {SDropID} gives uid {Uid} {Amount} of special coin {CoinID}, total {CoinVal}",
                sDropID,
                role.GameUlongUID,
                amount,
                coinID,
                commit.Wallet.GetCoinVal(coinID));
            grants.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, beforeCoinNum, commit.Wallet.GetCoinVal(coinID))));
            grants.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, beforeCoinNum));
            awards.Add(new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
            {
                FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
                {
                    ResID = coinID,
                    ResCnt = (int)amount,
                    ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr,
                },
                OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
            });
        }

        grants.Add(BuildTutorialRewardNotify([.. awards]));
        grants.Add(await BuildOwnershipUpdateTaskInfoNtfAsync(role));
        return grants;
    }

    private async Task<IEnumerable<GamePacket>> GrantDropAsync(LMNRoleInfo role, pbDrop_Static drop)
    {
        if (drop.ResID == AcntExpResID)
        {
            return await GrantAcntExpAsync(role, drop);
        }

        if (gameData.OutSideItemBase.Find(drop.ResID) is { OutSideItemType: (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeEos } license
            && license.ItemParam.Count > LicensePokemonParamIndex)
        {
            return await GrantPokemonLicenseAsync(role, drop, license.ItemParam[LicensePokemonParamIndex]);
        }

        logger.LogWarning(
            "Newbie drop {SDropID} resource {ResID} x{ResNum} for uid {Uid} is not granted: no grant rule for this resource",
            drop.SDropID,
            drop.ResID,
            drop.ResNum,
            role.GameUlongUID);
        return [];
    }

    private async Task<IEnumerable<GamePacket>> GrantPokemonLicenseAsync(LMNRoleInfo role, pbDrop_Static drop, int pokemonId)
    {
        var followBefore = role.FollowPokemonId;
        if (!role.GrantPokemon(pokemonId, clock.GetUtcNow().ToUnixTimeSeconds(), TutorialRewardReason))
        {
            logger.LogWarning(
                "Newbie drop {SDropID} license {ResID} for uid {Uid} is not granted: Pokemon {PokemonId} is already owned",
                drop.SDropID,
                drop.ResID,
                role.GameUlongUID,
                pokemonId);
            return [];
        }

        var award = new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
        {
            IsReplaced = Replaced,
            FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
            {
                ResID = pokemonId,
                ResCnt = 1,
                ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon,
            },
            OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
            {
                ResID = drop.ResID,
                ResCnt = drop.ResNum,
                ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem,
            },
        };
        return
        [
            .. BuildPokemonGain(role, pokemonId, followBefore),
            .. await starRoad.BuildPokemonGainPacketsAsync(role),
            BuildAddPokemonRedDot(role),
            BuildTutorialRewardNotify(award),
            await BuildOwnershipUpdateTaskInfoNtfAsync(role),
        ];
    }

    private async Task<IEnumerable<GamePacket>> GrantAcntExpAsync(LMNRoleInfo role, pbDrop_Static drop)
    {
        var levelBefore = RoleProfile.GetLevel(gameData, role);
        var slotUnlockNumBefore = heldItems.GetHeldItemSlotUnlockNum(role);
        var curToTalExp = role.AddRoleTotalExp(drop.ResNum);
        var levelChanged = RoleProfile.GetLevel(gameData, role) != levelBefore;
        logger.LogInformation(
            "Newbie drop {SDropID} gives uid {Uid} {ChgExp} trainer exp, total {CurToTalExp}",
            drop.SDropID,
            role.GameUlongUID,
            drop.ResNum,
            curToTalExp);
        var award = new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
        {
            FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
            {
                ResID = drop.ResID,
                ResCnt = drop.ResNum,
                ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr,
            },
            OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
        };
        var grants = new List<GamePacket>();
        if (drop.ExtraIntParam != NoPokemonPointTarget && !role.OwnsPokemon(drop.ExtraIntParam))
        {
            logger.LogWarning(
                "Newbie drop {SDropID} Pokemon points for uid {Uid} are not granted: Pokemon {PokemonId} is not owned",
                drop.SDropID,
                role.GameUlongUID,
                drop.ExtraIntParam);
        }
        else if (drop.ExtraIntParam != NoPokemonPointTarget)
        {
            grants.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidUpdateUsedPokemonNtf,
                await pokemon.AddPokemonPointAsync(role, drop.ExtraIntParam, drop.ResNum)));
        }

        if (levelChanged)
        {
            grants.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf,
                await taskUpdates.BuildTrainerLevelUpdateTaskInfoNtfAsync(role)));
            grants.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf,
                LobbyNotices.BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewActivityTask)));
            var countryCode = (await settings.LoadAsync(role.GameUlongUID)).CountryCode;
            grants.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidActivityInfoNtf, notices.BuildActivityInfoNtf(countryCode)));
        }

        grants.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTrainerExpNtf, new pbSCUpdateTrainerExpNtf
        {
            Level = RoleProfile.GetLevel(gameData, role),
            ChgExp = drop.ResNum,
            CurToTalExp = curToTalExp,
            ReachMaxLevel = RoleProfile.HasReachedMaxLevel(gameData, role) ? 1 : 0,
            NextLevelToTalExp = RoleProfile.GetNextLevelTotalExp(gameData, role),
            SeasonPoint = TrainerExpGifts.GetSeasonPoint(role),
            ReachMaxSeasonGift = trainerExpGifts.GetReachMaxSeasonGift(role),
        }));
        grants.AddRange(await heldItems.BuildHeldItemSlotUnlockPacketsAsync(role, slotUnlockNumBefore));
        var nextGift = trainerExpGifts.BuildTrainerExpNextGiftInfoNtf(role);
        if (trainerExpGifts.CountAbleToGet(role) > 0)
        {
            grants.Add(BuildRedDotReplaceNtf(TrainerLavelGiftRedDotID, HasTrainerLevelGift));
        }

        grants.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidTrainerExpNextGiftInfoNtf, nextGift));
        grants.Add(BuildTutorialRewardNotify(award));
        grants.Add(await BuildOwnershipUpdateTaskInfoNtfAsync(role));
        return grants;
    }

    private async Task<GamePacket> BuildOwnershipUpdateTaskInfoNtfAsync(LMNRoleInfo role) => GamePacket.Create(
        PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf,
        await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role));

    private IEnumerable<GamePacket> BuildPokemonGain(LMNRoleInfo role, int pokemonId, int followBefore)
    {
        var followAfter = role.FollowPokemonId;
        if (followAfter != followBefore)
        {
            yield return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidSetPokemonFollowRsp, new pbSCSetPokemonFollowRsp
            {
                ResultInfo = new pbResultInfo(),
                PokemonId = followAfter,
            });
        }

        yield return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
        {
            UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
            PokemonInfo = role.BuildPokemonInfo(pokemonId),
        });
    }

    private static GamePacket BuildRedDotReplaceNtf(int redDotID, int redDotBadgeNum) => redDotBadgeNum > 0
        ? GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = redDotID, RedDotBadgeNum = redDotBadgeNum } },
            },
        })
        : GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = redDotID });

    private static GamePacket BuildTutorialRewardNotify(params pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO[] awards) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = TutorialRewardReason,
            ResourcesList = { awards },
        });

    private sealed record NewbieArchive(int Newbie, pbCOMDT_NEWBIE_STATE_INFO NewbieStateAfter);
}
