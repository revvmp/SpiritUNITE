using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Badge;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Badge;

public sealed class BadgeHandlers(
    RoleInfoStore roles,
    BadgeManager badges,
    PokemonManager pokemon,
    ILogger<BadgeHandlers> logger)
{
    private const int NotUseBadge = 1;
    private const int UseBadge = 0;
    private const int NoPokemon = 0;
    private const int OverRide = 1;
    private const int NotFixed = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetBadgeAllInfoReq)]
    public async Task<GamePacket[]> OnGetBadgeAllInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetBadgeAllInfoReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidGetBadgeAllInfoRsp,
                await badges.BuildGetBadgeAllInfoRspAsync(role.GameUlongUID)),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetBadgePictureReq)]
    public async Task<GamePacket[]> OnGetBadgePictureReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetBadgePictureReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidGetBadgePictureRsp,
                await badges.BuildGetBadgePictureRspAsync(role.GameUlongUID)),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetBadgePictureShowReq)]
    public async Task<GamePacket[]> OnSetBadgePictureShowReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var badgeShowList = pbCSSetBadgePictureShowReq.Parser.ParseFrom(request.PkgData).BadgeShowInfo?.BadgeShowList ?? [];
        var result = await badges.SetBadgePictureShowAsync(role.GameUlongUID, badgeShowList);
        logger.LogInformation(
            "Emblem show list [{BadgeShowList}] for uid {Uid}: {Result}",
            string.Join(',', badgeShowList),
            role.GameUlongUID,
            result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetBadgePictureShowRsp, new pbSCSetBadgePictureShowRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                BadgeShowInfo = await badges.GetBadgeShowInfoAsync(role.GameUlongUID),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearBadgePictureRedReq)]
    public async Task<GamePacket[]> OnClearBadgePictureRedReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var badgeID = pbCSClearBadgePictureRedReq.Parser.ParseFrom(request.PkgData).BadgeID;
        var result = await badges.ClearBadgeNewTagAsync(role.GameUlongUID, badgeID);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearBadgePictureRedRsp, new pbSCClearBadgePictureRedRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                BadgeID = badgeID,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearNewBadgeRedReq)]
    public async Task<GamePacket[]> OnClearNewBadgeRedReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var badgeInfo = pbCSClearNewBadgeRedReq.Parser.ParseFrom(request.PkgData).BadgeInfo ?? new pbCOMDT_ONE_BADGE_UNIQUEID_INFO();
        var result = badgeInfo.UniqueID == badgeInfo.BadgeID
            ? await badges.ClearBadgeNewTagAsync(role.GameUlongUID, badgeInfo.BadgeID)
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearNewBadgeRedRsp, new pbSCClearNewBadgeRedRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                BadgeInfo = badgeInfo,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryBadgeSchedueListReq)]
    public async Task<GamePacket[]> OnQueryBadgeSchedueListReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSQueryBadgeSchedueListReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidQueryBadgeSchedueListRsp,
                await badges.BuildQueryBadgeSchedueListRspAsync(role.GameUlongUID)),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryBadgeSchedueInfoReq)]
    public async Task<GamePacket[]> OnQueryBadgeSchedueInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSQueryBadgeSchedueInfoReq.Parser.ParseFrom(request.PkgData);
        var cur = await badges.GetCurSchedueAsync(role.GameUlongUID);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQueryBadgeSchedueInfoRsp, new pbSCQueryBadgeSchedueInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                BadgeSchedueId = cur.BadgeSchedueId,
                SchedueInfo = badges.BuildSchedueInfo(cur),
                HasFixed = NotFixed,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSelectBadgeSchedueReq)]
    public async Task<GamePacket[]> OnSelectBadgeSchedueReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var select = pbCSSelectBadgeSchedueReq.Parser.ParseFrom(request.PkgData);
        var notUseBadge = select.NotUseBadge == NotUseBadge ? NotUseBadge : UseBadge;
        var (result, schedue) = notUseBadge == NotUseBadge
            ? (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, await badges.GetCurSchedueAsync(role.GameUlongUID))
            : select.PokemonID == NoPokemon
                ? await badges.SelectCurSchedueAsync(role.GameUlongUID, select.BadgeSchedueId)
                : await badges.FindUnlockedSchedueAsync(role.GameUlongUID, select.BadgeSchedueId);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc && select.PokemonID != NoPokemon)
        {
            (result, _) = await pokemon.SelectBadgeSchedueAsync(role, select.PokemonID, select.BadgeSchedueId, notUseBadge);
        }

        logger.LogInformation(
            "Emblem loadout {BadgeSchedueId} selected for Pokemon {PokemonID}, not use {NotUseBadge}, uid {Uid}: {Result}",
            select.BadgeSchedueId,
            select.PokemonID,
            notUseBadge,
            role.GameUlongUID,
            result);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSelectBadgeSchedueRsp, new pbSCSelectBadgeSchedueRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            BadgeSchedueId = select.BadgeSchedueId,
            SchedueInfo = schedue is null ? new pbCOMDT_BADGE_SCHEDUE() : badges.BuildSchedueInfo(schedue),
            HasFixed = NotFixed,
            NotUseBadge = notUseBadge,
            PokemonID = select.PokemonID,
        });
        var curSchedueChanged = result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            && notUseBadge == UseBadge
            && select.PokemonID == NoPokemon;
        return curSchedueChanged && schedue is { } selected
            ? [reply, BuildBadgePlateNtf(request, selected)]
            : [reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidModifySchedueNameReq)]
    public async Task<GamePacket[]> OnModifySchedueNameReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var modify = pbCSModifySchedueNameReq.Parser.ParseFrom(request.PkgData);
        var result = await badges.ModifySchedueNameAsync(role.GameUlongUID, modify.BadgeSchedueId, modify.SchedueName);
        logger.LogInformation(
            "Emblem loadout {BadgeSchedueId} rename for uid {Uid}: {Result}",
            modify.BadgeSchedueId,
            role.GameUlongUID,
            result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidModifySchedueNameRsp, new pbSCModifySchedueNameRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                SchedueName = modify.SchedueName,
                BadgeSchedueId = modify.BadgeSchedueId,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidInlayBadgeReq)]
    public async Task<GamePacket[]> OnInlayBadgeReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var inlay = pbCSInlayBadgeReq.Parser.ParseFrom(request.PkgData);
        var (result, schedue) = await badges.InlayBadgeAsync(
            role.GameUlongUID,
            inlay.IsOverRide == OverRide,
            [.. inlay.BadgeIDList]);
        logger.LogInformation(
            "Emblems {BadgeIDList} inlaid into loadout {BadgeSchedueId} for uid {Uid}: {Result}",
            string.Join(',', inlay.BadgeIDList),
            schedue.BadgeSchedueId,
            role.GameUlongUID,
            result);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidInlayBadgeRsp, new pbSCInlayBadgeRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            BadgeIDList = { inlay.BadgeIDList },
        });
        return result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc ? [reply, BuildBadgePlateNtf(request, schedue)] : [reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUnloadBadgeReq)]
    public async Task<GamePacket[]> OnUnloadBadgeReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var unload = pbCSUnloadBadgeReq.Parser.ParseFrom(request.PkgData);
        var (result, schedue) = await badges.UnloadBadgeAsync(role.GameUlongUID, [.. unload.BadgeIDList]);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUnloadBadgeRsp, new pbSCUnloadBadgeRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            BadgeIDList = { unload.BadgeIDList },
        });
        return result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc ? [reply, BuildBadgePlateNtf(request, schedue)] : [reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBadgeUnlockReq)]
    public async Task<GamePacket[]> OnBadgeUnlockReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var unlock = pbCSBadgeUnlockReq.Parser.ParseFrom(request.PkgData);
        var (result, cost, wallet) = await badges.UnlockSchedueAsync(
            role.GameUlongUID,
            unlock.BadgeSchedueId,
            (PB_ENUM_RES_COIN_TYPE)unlock.ResCoinType);
        logger.LogInformation(
            "Emblem loadout {BadgeSchedueId} unlock with coin type {ResCoinType} cost {Cost} for uid {Uid}: {Result}",
            unlock.BadgeSchedueId,
            unlock.ResCoinType,
            cost,
            role.GameUlongUID,
            result);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBadgeUnlockRsp, new pbSCBadgeUnlockRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            BadgeSchedueId = unlock.BadgeSchedueId,
            ResCoinType = unlock.ResCoinType,
            ResCoinCount = (int)cost,
        });
        return wallet is null ? [reply] : [.. BuildWalletUpdates(request, wallet), reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBadgeUpgradeReq)]
    public async Task<GamePacket[]> OnBadgeUpgradeReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var upgrade = pbCSBadgeUpgradeReq.Parser.ParseFrom(request.PkgData);
        var outcome = await badges.UpgradeBadgeAsync(role.GameUlongUID, upgrade.BadgeId, upgrade.UpgradeNum, upgrade.Force);
        logger.LogInformation(
            "Emblem {BadgeId} upgrade x{UpgradeNum} for uid {Uid}: {Result}, confirm {NeedConfirm}, success {RandSucc}",
            upgrade.BadgeId,
            upgrade.UpgradeNum,
            role.GameUlongUID,
            outcome.Result,
            outcome.NeedConfirm,
            outcome.RandSucc);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBadgeUpgradeRsp, new pbSCBadgeUpgradeRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            NeedConfirm = outcome.NeedConfirm,
            BeforeBadgeId = upgrade.BadgeId,
            UpgradeNum = upgrade.UpgradeNum,
            AfterBadgeId = outcome.AfterBadgeId,
            RandSucc = outcome.RandSucc,
        });
        var updates = new List<GamePacket>();
        if (outcome.ChangedBadges is { Count: > 0 } changed)
        {
            updates.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateBadgeNotify, BadgeManager.BuildUpdateBadgeNotify(changed)));
        }

        if (outcome.ChangedCurSchedue is { } schedue)
        {
            updates.Add(BuildBadgePlateNtf(request, schedue));
        }

        if (outcome.ChangedBadges is not null && outcome.Wallet is { } wallet)
        {
            updates.AddRange(BuildWalletUpdates(request, wallet));
        }

        return [.. updates, reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidResolveBadgeReq)]
    public GamePacket[] OnResolveBadgeReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var resolve = pbCSResolveBadgeReq.Parser.ParseFrom(request.PkgData);
        var result = badges.ValidateResolveBadge(resolve.BadgeID, resolve.ResolveNum);
        logger.LogInformation(
            "Emblem {BadgeID} resolve x{ResolveNum} refused for uid {Uid}: {Result}",
            resolve.BadgeID,
            resolve.ResolveNum,
            role.GameUlongUID,
            result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidResolveBadgeRsp, new pbSCResolveBadgeRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                BadgeID = resolve.BadgeID,
                ResolveNum = resolve.ResolveNum,
            }),
        ];
    }

    private GamePacket BuildBadgePlateNtf(GamePacket request, BadgeSchedue schedue) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBadgePlateNtf, new pbSCBadgePlateNtf
        {
            BadgePlate = badges.BuildBadgePlate(schedue),
        });

    private static IEnumerable<GamePacket> BuildWalletUpdates(GamePacket request, RoleWallet wallet)
    {
        yield return request.Reply(
            PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf,
            new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() });
        yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp());
    }
}
