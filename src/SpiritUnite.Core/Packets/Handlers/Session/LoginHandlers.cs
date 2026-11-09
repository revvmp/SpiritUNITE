using Microsoft.Extensions.Logging;
using SpiritUnite.Auth;
using SpiritUnite.Game.Login;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Servers.Lobby;

namespace SpiritUnite.Packets.Handlers.Session;

public sealed class LoginHandlers(
    RoleInfoStore roles,
    LoginManager login,
    AccountManager accounts,
    OffingRestartSessions offingRestarts,
    TimeProvider clock,
    ILogger<LoginHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcntLoginReq)]
    public async Task<GamePacket[]> OnAcntLoginReq(GamePacket request)
    {
        var acntLogin = pbCSAcntLoginReq.Parser.ParseFrom(request.PkgData);
        var uid = RequireAuthenticatedUid(request.Uid);
        var session = request with { Uid = uid };
        if (await FindLoginBanResultAsync(uid) is { } banResult)
        {
            return [session.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntLoginRsp, new pbSCAcntLoginRsp { ResultInfo = banResult })];
        }

        login.RememberRelayMD5(uid, acntLogin.RelayMD5);
        var role = await roles.FindAsync(uid);
        if (role is null)
        {
            SessionOf(request).RequireRegistration(acntLogin.CountryCode);
            logger.LogInformation("Trainer registration required for uid {Uid}", uid);
            return
            [
                session.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntRegisterNtf, new pbSCAcntRegisterNtf
                {
                    ServerTimeStamp = clock.GetUtcNow().ToUnixTimeSeconds(),
                }),
            ];
        }

        var isFirstLoginInToday = await login.RecordAcntLoginAsync(uid, acntLogin.CountryCode);
        return
        [
            .. (await login.BuildColdLoginPreludeAsync(role)).Select(packet => packet with { Uid = uid, ClientSeq = request.ClientSeq }),
            await BeginLoginAsync(session, role, offingRestart: false, isFirstLoginInToday),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcntRegisterReq)]
    public async Task<GamePacket[]> OnAcntRegisterReq(GamePacket request)
    {
        var registration = pbCSAcntRegisterReq.Parser.ParseFrom(request.PkgData);
        if (await FindLoginBanResultAsync(request.Uid) is { } banResult)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntRegisterRsp, new pbSCAcntRegisterRsp { ResultInfo = banResult })];
        }

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
        var isFirstLoginInToday = await login.CompleteRegistrationAsync(request.Uid, SessionOf(request).RegistrationCountryCode);
        return [response, await BeginLoginAsync(request, role, offingRestart: false, isFirstLoginInToday)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidOffingRestartReq)]
    public async Task<GamePacket[]> OnOffingRestartReq(GamePacket request)
    {
        var restart = pbCSOffingRestartReq.Parser.ParseFrom(request.PkgData);
        var uid = RequireAuthenticatedUid(request.Uid);
        if (await FindLoginBanResultAsync(uid) is { } banResult)
        {
            return [(request with { Uid = 0 }).Reply(PB_ENUM_CS_MSGID_DEF.PbScidOffingRestartRsp, new pbSCOffingRestartRsp { ResultInfo = banResult })];
        }

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
            SessionOf(request).Resume();
            logger.LogInformation("Lobby session of uid {Uid} resumed", uid);
            return [OffingRestartSucceeded(session)];
        }

        var isFirstLoginInToday = await login.RecordLoginDayAsync(uid);
        return
        [
            session.Reply(PB_ENUM_CS_MSGID_DEF.PbScidActivityLoginShowNtf, new pbSCActivityLoginShowNtf()),
            await BeginLoginAsync(session, role, offingRestart: true, isFirstLoginInToday),
        ];
    }

    private async Task<pbResultInfo?> FindLoginBanResultAsync(ulong uid)
    {
        if (await accounts.FindActiveBanAsync(uid, clock.GetUtcNow().ToUnixTimeSeconds()) is not { } ban)
        {
            return null;
        }

        logger.LogInformation("Lobby login refused for banned uid {Uid}", uid);
        return ban.BanEndTime == AccountBanState.Permanent
            ? new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrKickOffBan }
            : new pbResultInfo
            {
                ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrLoginForbidden3,
                Args = new pbArgList
                {
                    Args =
                    {
                        new pbArgData
                        {
                            Type = (int)PB_ENUM_ARGDATATYPE.PbAdtTimestamp,
                            Value = new pbArgDataValue { TimestampValue = new pbArgTimestampValue { TimestampValue = ban.BanEndTime } },
                        },
                    },
                },
            };
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCliBattleVersionReq)]
    public async Task<GamePacket[]> OnCliBattleVersionReq(GamePacket request)
    {
        pbCSCliBattleVersionReq.Parser.ParseFrom(request.PkgData);
        var session = SessionOf(request);
        if (!session.TryBeginCompletion(out var offingRestart, out var isFirstLoginInToday))
        {
            return [];
        }

        try
        {
            var replies = await login.BuildLoginFinishAsync(request, roles.Require(request.Uid), offingRestart, isFirstLoginInToday);
            session.Complete();
            return replies;
        }
        catch
        {
            session.FailCompletion();
            throw;
        }
    }

    private async Task<GamePacket> BeginLoginAsync(GamePacket request, LMNRoleInfo role, bool offingRestart, bool isFirstLoginInToday)
    {
        var response = await login.BuildAcntLoginRspAsync(role, isFirstLoginInToday);
        SessionOf(request).AwaitClientVersion(offingRestart, isFirstLoginInToday);
        return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntLoginRsp, response);
    }

    private static GamePacket OffingRestartSucceeded(GamePacket request) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidOffingRestartRsp, new pbSCOffingRestartRsp { ResultInfo = new pbResultInfo() });

    private static ulong RequireAuthenticatedUid(ulong authenticatedUid) =>
        authenticatedUid != 0 ? authenticatedUid : throw new InvalidDataException("Login has no valid account token");

    private static LoginSession SessionOf(GamePacket request) =>
        request.Login ?? throw new InvalidDataException("Login has no connection context");
}
