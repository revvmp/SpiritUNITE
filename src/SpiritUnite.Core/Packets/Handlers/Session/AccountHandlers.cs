using Microsoft.Extensions.Logging;
using SpiritUnite.Auth;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Session;

public sealed class AccountHandlers(AccountManager accounts, RoleInfoStore roles, ILogger<AccountHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcntWrittenOffReq)]
    public async Task<GamePacket[]> OnAcntWrittenOffReq(GamePacket request)
    {
        var writtenOff = pbCSAcntWrittenOffReq.Parser.ParseFrom(request.PkgData);
        if (request.Uid == 0 || writtenOff.Uid != request.Uid)
        {
            logger.LogInformation("Account deletion refused: uid {RequestedUid} is not the logged in account {Uid}",
                writtenOff.Uid, request.Uid);
            return [WrittenOffReply(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid)];
        }

        var reasons = writtenOff.WrittenOffInfo ?? new pbCOMDT_TLOG_WRITTEN_OFF_INFO();
        if (!await accounts.DeleteAsync(request.Uid))
        {
            logger.LogInformation("Account deletion found no account {Uid}", request.Uid);
            return [WrittenOffReply(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed)];
        }

        roles.Forget(request.Uid);
        logger.LogInformation(
            "Account {Uid} and its trainer were deleted at the player's request, reasons {ReasonType}, {ReasonType2}, {ReasonType3}, written reason {ReasonLength} characters",
            request.Uid, reasons.ReasonType, reasons.ReasonType2, reasons.ReasonType3, reasons.Reason.Length);
        return [WrittenOffReply(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGameLogoutReq)]
    public GamePacket[] OnGameLogoutReq(GamePacket request)
    {
        _ = pbCSGameLogoutReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation("Game logout from uid {Uid}", request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGameLogoutRsp, new pbSCGameLogoutRsp { ResultInfo = new pbResultInfo() })];
    }

    private static GamePacket WrittenOffReply(GamePacket request, PB_ENUM_RES_ERROR_CODE result) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntWrittenOffRsp, new pbSCAcntWrittenOffRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        });
}
