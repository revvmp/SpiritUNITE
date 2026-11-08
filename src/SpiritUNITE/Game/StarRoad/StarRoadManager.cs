using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using static SpiritUNITE.Game.StarRoad.StarRoadConstants;

namespace SpiritUNITE.Game.StarRoad;

public sealed record StarRoadSync(StarRoadRoute Route, IReadOnlyList<int> ChangedSiteIDs)
{
    public GamePacket[] BuildSiteUpdatePackets() =>
    [
        .. ChangedSiteIDs.Distinct().Select(siteId =>
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadSiteUpdateNtf, Route.BuildSiteUpdateNtf(siteId))),
    ];
}

public sealed record StarRoadReceive(PB_ENUM_RES_ERROR_CODE Result, GamePacket[] Packets)
{
    public static StarRoadReceive Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed class StarRoadManager(
    GameData gameData,
    StarRoadRepository repository,
    WalletManager wallets,
    TaskUpdates taskUpdates,
    TimeProvider clock,
    ILogger<StarRoadManager> logger)
{
    private const int AddPokemonRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdAddPokemon;
    private const int ReceiveRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdStarRoadPokemonReceive;
    private const int ReceiveRedDotBadgeNum = 1;
    private const int ReceivedPokemonCnt = 1;

    private readonly StarRoadScores scores = new(gameData);

    public async Task<StarRoadSync> SyncOwnedPokemonAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        var state = await repository.LoadAsync(uid);
        var commonScoreNum = (await wallets.LoadAsync(uid)).GetCoinVal(CoinID.UNIVERSAL_SHARDS_TOKEN_ID);
        var route = Build(state, commonScoreNum);
        var placed = state.EndSites.Values.ToHashSet();
        var changed = new List<int>();
        foreach (var owned in role.GetOwnedPokemon().OrderBy(owned => owned.GainTime).ThenBy(owned => owned.PokemonId))
        {
            if (placed.Contains(owned.PokemonId) || route.FindHoldingSite(owned.PokemonId) is not { } holder)
            {
                continue;
            }

            var endsRouteSite = holder.SiteStatus == SITE_STATUS.BEGIN;
            var targetSiteId = endsRouteSite ? holder.Site.SiteID : route.FindFreeExtraSite(holder.Site.SiteID);
            if (targetSiteId is not { } siteId
                || !await repository.TryEndSiteAsync(uid, siteId, owned.PokemonId, clock.GetUtcNow().ToUnixTimeSeconds()))
            {
                continue;
            }

            placed.Add(owned.PokemonId);
            route = Build(route.State.WithEndSite(siteId, owned.PokemonId), commonScoreNum);
            changed.Add(siteId);
            if (endsRouteSite && route.CurSiteID != StarRoadState.NoSite)
            {
                changed.Add(route.CurSiteID);
            }

            logger.LogInformation(
                "License Journey site {SiteId} ended for uid {Uid} with owned Pokemon {PokemonId}",
                siteId, uid, owned.PokemonId);
        }

        return new StarRoadSync(route, changed);
    }

    public async Task<GamePacket[]> BuildPokemonGainPacketsAsync(LMNRoleInfo role)
    {
        var sync = await SyncOwnedPokemonAsync(role);
        return [.. sync.BuildSiteUpdatePackets(), await BuildStarRoadInfoNtfAsync(role, sync.Route, isLoginNtf: false)];
    }

    public async Task<GamePacket[]> BuildSpecialCoinChangePacketsAsync(LMNRoleInfo role, long beforeCoinNum)
    {
        var route = (await SyncOwnedPokemonAsync(role)).Route;
        var before = Build(route.State, beforeCoinNum);
        return
        [
            .. BuildReceiveRedDot(before.CanReceiveAny, route) is { } redDot ? new[] { redDot } : [],
            await BuildStarRoadInfoNtfAsync(role, route, isLoginNtf: false),
        ];
    }

    public async Task<pbCSDT_STAR_ROAD_INFO> BuildStarRoadInfoAsync(LMNRoleInfo role, StarRoadRoute route, bool isLoginNtf)
    {
        var pokemonTipsTodayClose = IsToday(route.State.PokemonTipsCloseTime);
        var triggerTips = route.ShowsProgress && route.State.TipsSiteID != route.CurSiteID && !pokemonTipsTodayClose;
        if (triggerTips)
        {
            await repository.SetTipsSiteAsync(role.GameUlongUID, route.CurSiteID);
        }

        return route.BuildStarRoadInfo(isLoginNtf, triggerTips, pokemonTipsTodayClose);
    }

    public Task ClosePokemonTipsTodayAsync(LMNRoleInfo role) =>
        repository.SetPokemonTipsCloseTimeAsync(role.GameUlongUID, clock.GetUtcNow().ToUnixTimeSeconds());

    private bool IsToday(long unixTime)
    {
        var today = clock.GetUtcNow().UtcDateTime.Date;
        return unixTime > 0 && DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime.Date == today;
    }

    public async Task<GamePacket> BuildStarRoadInfoNtfAsync(LMNRoleInfo role, StarRoadRoute route, bool isLoginNtf) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadInfoNtf, new pbSCStarRoadInfoNtf
        {
            StarRoadInfo = await BuildStarRoadInfoAsync(role, route, isLoginNtf),
        });

    public async Task<GamePacket> BuildLoginStarRoadInfoNtfAsync(LMNRoleInfo role) =>
        await BuildStarRoadInfoNtfAsync(role, (await SyncOwnedPokemonAsync(role)).Route, isLoginNtf: true);

    public static GamePacket? BuildReceiveRedDot(bool couldReceiveBefore, StarRoadRoute after) =>
        (couldReceiveBefore, after.CanReceiveAny) switch
        {
            (false, true) => GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
            {
                RedDotInfo = new pbCOMDT_RED_DOT_INFO
                {
                    RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = ReceiveRedDotID, RedDotBadgeNum = ReceiveRedDotBadgeNum } },
                },
            }),
            (true, false) => GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = ReceiveRedDotID }),
            _ => null,
        };

    public static GamePacket BuildAddPokemonRedDot(LMNRoleInfo role) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail =
                {
                    new pbCOMDT_RED_DOT_DETAIL
                    {
                        RedDotID = AddPokemonRedDotID,
                        RedDotBadgeNum = role.CountNewTagPokemon(),
                    },
                },
            },
        });

    public async Task<StarRoadReceive> ReceivePokemonAsync(LMNRoleInfo role, int siteId, int pokemonId)
    {
        var uid = role.GameUlongUID;
        var route = (await SyncOwnedPokemonAsync(role)).Route;
        var refusal = CheckReceive(role, route, siteId, pokemonId);
        if (refusal != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation(
                "License Journey Pokemon {PokemonId} at site {SiteId} refused for uid {Uid}: {Result}",
                pokemonId, siteId, uid, refusal);
            return StarRoadReceive.Refused(refusal);
        }

        var needScore = route.CurSite!.NeedScore;
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var receipt = await repository.CommitReceivePokemonAsync(uid, siteId, pokemonId, needScore, now);
        if (receipt.Wallet is not { } wallet)
        {
            logger.LogInformation(
                "License Journey Pokemon {PokemonId} at site {SiteId} refused for uid {Uid}: {Result}",
                pokemonId, siteId, uid, receipt.Result);
            return StarRoadReceive.Refused(receipt.Result);
        }

        var commonScoreNum = wallet.GetCoinVal(CoinID.UNIVERSAL_SHARDS_TOKEN_ID);
        var received = Build(route.State.WithEndSite(siteId, pokemonId), commonScoreNum);
        var tasksBefore = await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role);
        role.GrantPokemon(pokemonId, now, (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonStarRoadReceivePokemon);
        logger.LogInformation(
            "License Journey Pokemon {PokemonId} received at site {SiteId} by uid {Uid} for {NeedScore} license points",
            pokemonId, siteId, uid, needScore);

        GamePacket[] packets =
        [
            GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, receipt.BeforeCoinNum, commonScoreNum)),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, tasksBefore),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadSiteUpdateNtf, received.BuildSiteUpdateNtf(siteId)),
            .. received.CurSiteID == StarRoadState.NoSite
                ? []
                : new[] { GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadSiteUpdateNtf, received.BuildSiteUpdateNtf(received.CurSiteID)) },
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemonId),
            }),
            BuildAddPokemonRedDot(role),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, BuildReceiveAwardNotify(pokemonId)),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)),
            .. BuildReceiveRedDot(route.CanReceiveAny, received) is { } redDot ? new[] { redDot } : [],
            await BuildStarRoadInfoNtfAsync(role, received, isLoginNtf: false),
        ];
        return new StarRoadReceive(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, packets);
    }

    public StarRoadRoute Build(StarRoadState state, long commonScoreNum) =>
        StarRoadRoute.Build(gameData, scores, state, commonScoreNum, clock.GetUtcNow());

    private static PB_ENUM_RES_ERROR_CODE CheckReceive(LMNRoleInfo role, StarRoadRoute route, int siteId, int pokemonId)
    {
        if (route.FindSite(siteId) is not { } site)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrStarRoadSiteIdoutRange;
        }

        if (site.SiteStatus == SITE_STATUS.END)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrStarRoadSiteAlreadyEnd;
        }

        if (site.SiteStatus != SITE_STATUS.BEGIN || !site.PokemonIDs.Contains(pokemonId) || role.OwnsPokemon(pokemonId))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        return route.CommonScoreNum >= site.NeedScore
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrNotEnoughCoin;
    }

    private static pbSCResourcesAwardNotify BuildReceiveAwardNotify(int pokemonId) => new()
    {
        ResultInfo = new pbResultInfo(),
        AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonStarRoadReceivePokemon,
        ResourcesList =
        {
            new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
            {
                FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
                {
                    ResID = pokemonId,
                    ResCnt = ReceivedPokemonCnt,
                    ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon,
                },
                OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
            },
        },
    };
}
