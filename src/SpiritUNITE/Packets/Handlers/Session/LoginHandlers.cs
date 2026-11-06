using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Login;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Servers.Lobby;

namespace SpiritUNITE.Packets.Handlers.Session;

public sealed class LoginHandlers(
    RoleInfoStore roles,
    LoginManager login,
    OffingRestartSessions offingRestarts,
    PendingLoginFinishes pendingLogins,
    TimeProvider clock,
    ILogger<LoginHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcntLoginReq)]
    public async Task<GamePacket[]> OnAcntLoginReq(GamePacket request)
    {
        var acntLogin = pbCSAcntLoginReq.Parser.ParseFrom(request.PkgData);
        var uid = RequireAuthenticatedUid(request.Uid);
        login.RememberRelayMD5(uid, acntLogin.RelayMD5);
        var role = await roles.FindAsync(uid);
        var session = request with { Uid = uid };
        if (role is null)
        {
            login.RememberLoginCountry(uid, acntLogin.CountryCode);
            logger.LogInformation("Trainer registration required for uid {Uid}", uid);
            return
            [
                session.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntRegisterNtf, new pbSCAcntRegisterNtf
                {
                    ServerTimeStamp = clock.GetUtcNow().ToUnixTimeSeconds(),
                }),
            ];
        }

        await login.RecordAcntLoginAsync(uid, acntLogin.CountryCode);
        return [await BeginLoginAsync(session, role, offingRestart: false)];
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
        await login.CompleteRegistrationAsync(request.Uid);
        return [response, await BeginLoginAsync(request, role, offingRestart: false)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidOffingRestartReq)]
    public async Task<GamePacket[]> OnOffingRestartReq(GamePacket request)
    {
        var restart = pbCSOffingRestartReq.Parser.ParseFrom(request.PkgData);
        var uid = RequireAuthenticatedUid(request.Uid);
        var role = offingRestarts.CanResume(uid) ? await roles.FindAsync(uid) : null;
        if (role is null)
        {
            logger.LogInformation("Offing restart refused for uid {Uid}: no lobby session to resume", uid);
            return
            [
                (request with { Uid = 0 }).Reply(PB_ENUM_CS_MSGID_DEF.PbScidOffingRestartRsp, new pbSCOffingRestartRsp
                {
                    ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrAcntNotLoginState },
                }),
            ];
        }

        login.RememberRelayMD5(uid, restart.RelayMD5);
        var session = request with { Uid = uid };
        if (restart.NeedLoginRsp == 0)
        {
            logger.LogInformation("Lobby session of uid {Uid} resumed", uid);
            return [OffingRestartSucceeded(session)];
        }

        await login.RecordLoginDayAsync(uid);
        return
        [
            session.Reply(PB_ENUM_CS_MSGID_DEF.PbScidActivityLoginShowNtf, new pbSCActivityLoginShowNtf()),
            await BeginLoginAsync(session, role, offingRestart: true),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCliBattleVersionReq)]
    public async Task<GamePacket[]> OnCliBattleVersionReq(GamePacket request)
    {
        pbCSCliBattleVersionReq.Parser.ParseFrom(request.PkgData);
        if (!pendingLogins.TryTake(request.Uid, out var offingRestart))
        {
            return [];
        }

        var role = roles.Require(request.Uid);
        return await login.BuildLoginFinishAsync(request, role, offingRestart);
    }

    private async Task<GamePacket> BeginLoginAsync(GamePacket request, LMNRoleInfo role, bool offingRestart)
    {
        var response = await login.BuildAcntLoginRspAsync(role);
        pendingLogins.Await(role.GameUlongUID, offingRestart);
        return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntLoginRsp, response);
    }

    private static GamePacket OffingRestartSucceeded(GamePacket request) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidOffingRestartRsp, new pbSCOffingRestartRsp { ResultInfo = new pbResultInfo() });

    private static ulong RequireAuthenticatedUid(ulong authenticatedUid) =>
        authenticatedUid != 0 ? authenticatedUid : throw new InvalidDataException("Login has no valid account token");
}
