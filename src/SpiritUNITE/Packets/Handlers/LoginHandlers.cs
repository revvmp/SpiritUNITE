using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class LoginHandlers(
    RoleInfoStore roles,
    GameData gameData,
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

        return CompleteLogin(session, role);
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
        return [response, .. CompleteLogin(request, role)];
    }

    private GamePacket[] CompleteLogin(GamePacket request, LMNRoleInfo role)
    {
        logger.LogInformation("Login from uid {Uid} as {Name}", role.GameUlongUID, role.InGameUsrName);

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var response = new pbSCAcntLoginRsp
        {
            ResultInfo = new pbResultInfo(),
            IsSucc = LoginSucceeded,
            GameAcntUid = role.GameUlongUID,
            Level = RoleProfile.StartingLevel,
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
