using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.StarRoad;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Newbie;

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
    public const int FourteenDayRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdFourteenDay;

    private const int FirstNewbieGiftID = 1;
    private const int CanFetchGift = 1;
    private const int GiftFetched = 1;

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

    public async Task<pbCOMDT_RED_DOT_DETAIL?> FindFourteenDayRedDotAsync(LMNRoleInfo role)
    {
        var currDayGiftID = await GetCurrDayGiftIDAsync(role);
        var fetched = await repository.LoadFetchedGiftIDsAsync(role.GameUlongUID);
        return BuildFourteenDayRedDotDetail(currDayGiftID, fetched);
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
        foreach (var coinID in grant.Coins.Keys.Where(CoinID.IsSpecialCoin))
        {
            grants.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, commit.BeforeCoinNums.GetValueOrDefault(coinID), commit.Wallet.GetCoinVal(coinID))));
        }

        if (grant.PokemonIds.Count == 0 && commit.BeforeCoinNums.TryGetValue(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, out var licensePointsBefore))
        {
            grants.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, licensePointsBefore));
        }

        grants.AddRange(commit.Wallet.BuildCoinValPushes(grant.Coins.Keys));

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
            if (CoinID.IsGrantableCoin(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
                awards.Add(BagManager.BuildCoinAward(drop.ResID, drop.ResNum));
                continue;
            }

            if (UniteLicense.PlanGrant(gameData, role, drop.ResID, drop.ResNum, pokemonIds) is { } license)
            {
                license.AddTo(pokemonIds, coins, awards);
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

    private GamePacket BuildFourteenDayRedDot(int currDayGiftID, IReadOnlySet<int> fetched) =>
        BuildFourteenDayRedDotDetail(currDayGiftID, fetched) is { } redDot
            ? GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
            {
                RedDotInfo = new pbCOMDT_RED_DOT_INFO { RedDotDetail = { redDot } },
            })
            : GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = FourteenDayRedDotID });

    private pbCOMDT_RED_DOT_DETAIL? BuildFourteenDayRedDotDetail(int currDayGiftID, IReadOnlySet<int> fetched)
    {
        var unfetched = GetSchemeGifts().Count(gift => gift.GiftID <= currDayGiftID && !fetched.Contains(gift.GiftID));
        return unfetched == 0 ? null : new pbCOMDT_RED_DOT_DETAIL { RedDotID = FourteenDayRedDotID, RedDotBadgeNum = unfetched };
    }

    private int GetSchemeId() => notices.GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeNewbieGift);

    private IEnumerable<pbNewbieGift> GetSchemeGifts()
    {
        var schemeId = GetSchemeId();
        return gameData.NewbieGift.Rows.Where(gift => gift.SchemeId == schemeId).OrderBy(gift => gift.GiftID);
    }

    private int GetLastGiftID() => GetSchemeGifts().Select(gift => gift.GiftID).DefaultIfEmpty(FirstNewbieGiftID).Max();
}
