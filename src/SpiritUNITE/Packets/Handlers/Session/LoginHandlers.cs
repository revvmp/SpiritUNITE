using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Shop;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Session;

public sealed class LoginHandlers(
    RoleInfoStore roles,
    ActivityTasks activityTasks,
    GameData gameData,
    WalletManager wallets,
    ShopManager shop,
    TimeProvider clock,
    ILogger<LoginHandlers> logger)
{
    private const int LoginSucceeded = 1;
    private const int ShippedDatabinABTestID = 1;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcntLoginReq)]
    public async Task<GamePacket[]> OnAcntLoginReq(GamePacket request)
    {
        _ = pbCSAcntLoginReq.Parser.ParseFrom(request.PkgData);
        var uid = RequireAuthenticatedUid(request.Uid);
        var role = await roles.FindAsync(uid);
        var session = request with { Uid = uid };
        if (role is null)
        {
            logger.LogInformation("Trainer registration required for uid {Uid}", uid);
            return
            [
                session.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntRegisterNtf, new pbSCAcntRegisterNtf
                {
                    ServerTimeStamp = clock.GetUtcNow().ToUnixTimeSeconds(),
                }),
            ];
        }

        await activityTasks.RecordLoginDayAsync(uid);
        return await CompleteLoginAsync(session, role);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcntRegisterReq)]
    public async Task<GamePacket[]> OnAcntRegisterReq(GamePacket request)
    {
        var registration = pbCSAcntRegisterReq.Parser.ParseFrom(request.PkgData);
        var result = await roles.RegisterAsync(request.Uid, registration.UserName);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntRegisterRsp, new pbSCAcntRegisterRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        });
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Trainer registration refused for uid {Uid}: {Result}", request.Uid, result);
            return [response];
        }

        var role = await roles.FindAsync(request.Uid)
            ?? throw new InvalidOperationException("Registered role could not be loaded");
        logger.LogInformation("Trainer registration completed for uid {Uid}", request.Uid);
        await activityTasks.RecordLoginDayAsync(request.Uid);
        return [response, .. await CompleteLoginAsync(request, role)];
    }

    private async Task<GamePacket[]> CompleteLoginAsync(GamePacket request, LMNRoleInfo role)
    {
        logger.LogInformation("Login from uid {Uid} as {Name}", role.GameUlongUID, role.InGameUsrName);

        var wallet = await wallets.LoadAsync(role.GameUlongUID);
        var buyRecords = await shop.GetBuyRecordsAsync(role.GameUlongUID);

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var response = new pbSCAcntLoginRsp
        {
            ResultInfo = new pbResultInfo(),
            IsSucc = LoginSucceeded,
            GameAcntUid = role.GameUlongUID,
            Level = RoleProfile.GetLevel(gameData, role),
            RoleTotalExp = role.RoleTotalExp,
            NextLevelToTalExp = RoleProfile.GetNextLevelTotalExp(gameData, role),
            ReachMaxLevel = RoleProfile.HasReachedMaxLevel(gameData, role) ? 1 : 0,
            Name = role.InGameUsrName,
            FollowInfo = role.BuildFollowInfo(),
            Gender = role.Gender,
            RoleAvatarInfo = role.RoleAvatarInfo,
            NewbieState = role.BuildNewbieState(),
            ServerGameTimeStamp = now,
            ServerRealTimeStamp = now,
            ABTestID = ShippedDatabinABTestID,
            UsingLobbyBgId = role.GetUsingLobbyBgId(gameData),
            SeasonId = RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId,
            RankedDivision = (int)RankedMatch.StartingRankedDivision,
            CoinInfo = wallet.BuildCoinInfo(),
            AcntRealCoupous = wallet.AcntRealCoupous,
            SpecialSaleInfo = ShopBuyRecord.BuildSpecialSaleInfo(buyRecords),
        };

        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntLoginRsp, response),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidLoginFinishNtf, new pbSCLoginFinishNtf()),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAvatarListRsp, new pbSCGetAvatarListRsp
            {
                ResultInfo = new pbResultInfo(),
                AvatarList = { role.BuildAvatarList() },
            }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStarRoadInfoNtf, new pbSCStarRoadInfoNtf
            {
                StarRoadInfo = StarRoadProgress.Starting(gameData).BuildStarRoadInfo(isLoginNtf: true),
            }),
        ];
    }

    private static ulong RequireAuthenticatedUid(ulong authenticatedUid) =>
        authenticatedUid != 0 ? authenticatedUid : throw new InvalidDataException("Login has no valid account token");
}
