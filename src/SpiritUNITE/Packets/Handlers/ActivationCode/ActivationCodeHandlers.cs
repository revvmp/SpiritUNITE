using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.ActivationCode;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.ActivationCode;

public sealed class ActivationCodeHandlers(
    RoleInfoStore roles,
    ActivationCodeManager activationCodes,
    ILogger<ActivationCodeHandlers> logger)
{
    [PacketHandler(eNetEventServerType.DirServer, PB_ENUM_CS_MSGID_DEF.PbCsidUseActivationCodeReq)]
    public async Task<GamePacket[]> OnUseActivationCodeReq(GamePacket request)
    {
        var use = pbCSUseActivationCodeReq.Parser.ParseFrom(request.PkgData);
        var result = await activationCodes.UseActivationCodeAsync(request.Uid, use.ActivationCode);
        logger.LogInformation(
            "Activation code of length {Length} used by uid {Uid}: {Result}",
            use.ActivationCode.Length,
            request.Uid,
            result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseActivationCodeRsp, new pbSCUseActivationCodeRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                ActivationCode = use.ActivationCode,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetActivationCodeInfoReq)]
    public async Task<GamePacket[]> OnGetActivationCodeInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetActivationCodeInfoReq.Parser.ParseFrom(request.PkgData);
        var isOpen = activationCodes.IsInvitationOpen();
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetActivationCodeInfoRsp, new pbSCGetActivationCodeInfoRsp
            {
                ResultInfo = InvitationResult(isOpen),
                ActivationCodeInfo = isOpen
                    ? await activationCodes.BuildActivationCodeInfoAsync(role.GameUlongUID)
                    : new pbCOMDT_ACTIVATION_CODE_INFO(),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetActivationCodeDrawInfoReq)]
    public async Task<GamePacket[]> OnGetActivationCodeDrawInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetActivationCodeDrawInfoReq.Parser.ParseFrom(request.PkgData);
        var isOpen = activationCodes.IsInvitationOpen();
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetActivationCodeDrawInfoRsp, new pbSCGetActivationCodeDrawInfoRsp
            {
                ResultInfo = InvitationResult(isOpen),
                ButtonState = isOpen ? await activationCodes.GetDrawButtonStateAsync(role) : ActivationCodeManager.DrawLocked,
                FreshTime = ActivationCodeManager.NoFreshTime,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidActivationCodeDrawReq)]
    public async Task<GamePacket[]> OnActivationCodeDrawReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSActivationCodeDrawReq.Parser.ParseFrom(request.PkgData);
        var (result, activationCode) = await activationCodes.DrawActivationCodeAsync(role);
        logger.LogInformation("Activation code draw by uid {Uid}: {Result}", role.GameUlongUID, result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidActivationCodeDrawNtf, new pbSCActivationCodeDrawNtf
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                ActivationCode = activationCode,
            }),
        ];
    }

    private static pbResultInfo InvitationResult(bool isOpen) => new()
    {
        ResultId = (int)(isOpen ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit),
    };
}
