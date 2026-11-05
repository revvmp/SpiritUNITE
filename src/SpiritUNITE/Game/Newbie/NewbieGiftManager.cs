using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Newbie;

public sealed record NewbieGiftGrant(
    int GiftID,
    IReadOnlyDictionary<int, long> Coins,
    IReadOnlyList<int> PokemonIds,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards);

public sealed class NewbieGiftManager(
    GameData gameData,
    LobbyNotices notices,
    NewbieGiftRepository repository,
    RoleRepository roleRepository,
    TaskUpdates taskUpdates,
    StarRoadManager starRoad,
    TimeProvider clock,
    ILogger<NewbieGiftManager> logger)
{
    public const int NewbieGiftReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonNewbieGift;

    private const int FirstNewbieGiftID = 1;
    private const int CanFetchGift = 1;
    private const int GiftFetched = 1;
    private const int Replaced = 1;
    private const int LicensePokemonParamIndex = 0;
    private const int FourteenDayRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdFourteenDay;

    private readonly ILookup<int, pbDrop_Static> dropsById = gameData.DropStatic.Rows
        .OrderBy(drop => drop.SDropSeq)
        .ToLookup(drop => drop.SDropID);

    public async Task<pbSCNewbieGiftInfoNtf> BuildNewbieGiftInfoNtfAsync(LMNRoleInfo role)
    {
        var currDayGiftID = await GetCurrDayGiftIDAsync(role);
        var fetched = await repository.LoadFetchedGiftIDsAsync(role.GameUlongUID);
        return new pbSCNewbieGiftInfoNtf
        {
            CurrDayGiftID = currDayGiftID,
            GiftInfo = BuildGiftInfo(currDayGiftID, fetched),
        };
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, GamePacket[] Grants, pbCSDT_NEWBIE_GIFT_INFO? GiftInfo)> FetchNewbieGiftAsync(
        LMNRoleInfo role,
        int giftID)
    {
        var gift = gameData.NewbieGift.Find(pbNewbieGift.Key(giftID, GetSchemeId()));
        if (gift is null)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, [], null);
        }

        var currDayGiftID = await GetCurrDayGiftIDAsync(role);
        if (giftID > currDayGiftID)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrNewbieGiftNotGotYet, [], null);
        }

        if ((await repository.LoadFetchedGiftIDsAsync(role.GameUlongUID)).Contains(giftID))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrNewbieGiftAlreadyFetched, [], null);
        }

        var grant = PlanGrant(role, gift);
        if (grant is null)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, [], null);
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var commit = await repository.FetchAsync(role.GameUlongUID, grant, now);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || commit.Wallet is null)
        {
            return (commit.Result, [], null);
        }

        foreach (var pokemonId in grant.PokemonIds)
        {
            role.GrantPokemon(pokemonId, now, NewbieGiftReason);
        }

        logger.LogInformation("Newbie gift {GiftID} fetched by uid {Uid}", giftID, role.GameUlongUID);
        var fetched = await repository.LoadFetchedGiftIDsAsync(role.GameUlongUID);
        var grants = new List<GamePacket>();
        foreach (var (coinID, amount) in grant.Coins.Where(coin => CoinID.IsSpecialCoin(coin.Key)))
        {
            var beforeCoinNum = commit.BeforeCoinNums.GetValueOrDefault(coinID);
            grants.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, beforeCoinNum, commit.Wallet.GetCoinVal(coinID))));
            grants.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, beforeCoinNum));
        }

        if (grant.Coins.Keys.Any(coinID => !CoinID.IsSpecialCoin(coinID)))
        {
            grants.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf,
                new pbSCGetCoinValNtf { CoinInfo = commit.Wallet.BuildCoinInfo() }));
        }

        foreach (var pokemonId in grant.PokemonIds)
        {
            grants.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemonId),
            }));
        }

        if (grant.PokemonIds.Count > 0)
        {
            grants.AddRange(await starRoad.BuildPokemonGainPacketsAsync(role));
            grants.Add(NewbieManager.BuildAddPokemonRedDot(role));
        }

        grants.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = NewbieGiftReason,
            ResourcesList = { grant.Awards },
        }));
        grants.Add(GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf,
            await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)));
        grants.Add(BuildFourteenDayRedDot(currDayGiftID, fetched));
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, [.. grants], BuildGiftInfo(currDayGiftID, fetched));
    }

    private NewbieGiftGrant? PlanGrant(LMNRoleInfo role, pbNewbieGift gift)
    {
        var coins = new Dictionary<int, long>();
        var pokemonIds = new List<int>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        foreach (var drop in dropsById[gift.SDropID])
        {
            if (CoinID.IsSpecialCoin(drop.ResID) || CoinID.WalletCoins.Contains(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
                awards.Add(BuildAward(drop.ResID, drop.ResNum, CoinID.IsSpecialCoin(drop.ResID)
                    ? PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr
                    : PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin));
                continue;
            }

            if (gameData.OutSideItemBase.Find(drop.ResID) is { OutSideItemType: (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeEos } license
                && license.ItemParam.Count > LicensePokemonParamIndex
                && license.ItemParam[LicensePokemonParamIndex] is var pokemonId
                && !role.OwnsPokemon(pokemonId)
                && !pokemonIds.Contains(pokemonId))
            {
                pokemonIds.Add(pokemonId);
                awards.Add(new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
                {
                    IsReplaced = Replaced,
                    FinallyAwardInfo = BuildAwardInfo(pokemonId, 1, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon),
                    OriginAwardInfo = BuildAwardInfo(drop.ResID, drop.ResNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem),
                });
                continue;
            }

            logger.LogWarning(
                "Newbie gift {GiftID} for uid {Uid} is refused: drop {SDropID} resource {ResID} x{ResNum} has no grant rule",
                gift.GiftID,
                role.GameUlongUID,
                drop.SDropID,
                drop.ResID,
                drop.ResNum);
            return null;
        }

        return new NewbieGiftGrant(gift.GiftID, coins, pokemonIds, awards);
    }

    private async Task<int> GetCurrDayGiftIDAsync(LMNRoleInfo role)
    {
        var registerTime = await roleRepository.FindCreateTimeAsync(role.GameUlongUID);
        var registerDay = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(registerTime).UtcDateTime);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return Math.Clamp(today.DayNumber - registerDay.DayNumber + FirstNewbieGiftID, FirstNewbieGiftID, GetLastGiftID());
    }

    private pbCSDT_NEWBIE_GIFT_INFO BuildGiftInfo(int currDayGiftID, IReadOnlySet<int> fetched) => new()
    {
        NewBieGiftDetail =
        {
            GetSchemeGifts().Select(gift => new pbCSDT_NEWBIE_GIFT_DETAIL
            {
                GiftID = gift.GiftID,
                CanFetchGift = gift.GiftID <= currDayGiftID ? CanFetchGift : 0,
                GiftFetched = fetched.Contains(gift.GiftID) ? GiftFetched : 0,
            }),
        },
    };

    private GamePacket BuildFourteenDayRedDot(int currDayGiftID, IReadOnlySet<int> fetched)
    {
        var unfetched = GetSchemeGifts().Count(gift => gift.GiftID <= currDayGiftID && !fetched.Contains(gift.GiftID));
        if (unfetched == 0)
        {
            return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = FourteenDayRedDotID });
        }

        return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = FourteenDayRedDotID, RedDotBadgeNum = unfetched } },
            },
        });
    }

    private int GetSchemeId() => notices.GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeNewbieGift);

    private IEnumerable<pbNewbieGift> GetSchemeGifts()
    {
        var schemeId = GetSchemeId();
        return gameData.NewbieGift.Rows.Where(gift => gift.SchemeId == schemeId).OrderBy(gift => gift.GiftID);
    }

    private int GetLastGiftID() => GetSchemeGifts().Select(gift => gift.GiftID).DefaultIfEmpty(FirstNewbieGiftID).Max();

    private static pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO BuildAward(int resID, int resCnt, PB_ENUM_RES_RESOURCES_TYPE resType) => new()
    {
        FinallyAwardInfo = BuildAwardInfo(resID, resCnt, resType),
        OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
    };

    private static pbCOMDT_ONE_RESOURCE_AWARD_INFO BuildAwardInfo(int resID, int resCnt, PB_ENUM_RES_RESOURCES_TYPE resType) => new()
    {
        ResID = resID,
        ResCnt = resCnt,
        ResType = (int)resType,
    };
}
