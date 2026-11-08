using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Activity;

public sealed class FirstChargeHandlers(RoleInfoStore roles, FirstChargeManager firstCharge, ILogger<FirstChargeHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFirstChargeQueryReq)]
    public async Task<GamePacket[]> OnFirstChargeQueryReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSFirstChargeQueryReq.Parser.ParseFrom(request.PkgData);
        var query = await firstCharge.QueryAsync(role);
        LogRefusal("First charge query", request.Uid, query.Result);
        var response = new pbSCFirstChargeQueryRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)query.Result },
            FirstChargeInfo = query.FirstChargeInfo ?? new pbCOMDT_FIRST_CHARGE_INFO(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFirstChargeQueryRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFirstChargeAwardReq)]
    public async Task<GamePacket[]> OnFirstChargeAwardReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var loginSeq = pbCSFirstChargeAwardReq.Parser.ParseFrom(request.PkgData).LoginSeq;
        var outcome = await firstCharge.AwardAsync(role, loginSeq);
        LogRefusal($"First charge award {loginSeq}", request.Uid, outcome.Result);
        var response = new pbSCFirstChargeAwardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            LoginSeq = loginSeq,
        };
        return
        [
            .. outcome.BeforeRsp.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFirstChargeAwardRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFirstChargeBanFaceShotReq)]
    public async Task<GamePacket[]> OnFirstChargeBanFaceShotReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSFirstChargeBanFaceShotReq.Parser.ParseFrom(request.PkgData);
        await firstCharge.BanFaceShotAsync(role);
        logger.LogInformation("Uid {Uid} turned off the first charge face shot", request.Uid);
        var response = new pbSCFirstChargeBanFaceShotRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFirstChargeBanFaceShotRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFirstChargeFaceShotShowedReq)]
    public async Task<GamePacket[]> OnFirstChargeFaceShotShowedReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSFirstChargeFaceShotShowedReq.Parser.ParseFrom(request.PkgData);
        await firstCharge.RecordFaceShotShowedAsync(role);
        var response = new pbSCFirstChargeFaceShotShowedRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFirstChargeFaceShotShowedRsp, response)];
    }

    private void LogRefusal(string operation, ulong uid, PB_ENUM_RES_ERROR_CODE result)
    {
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("{Operation} refused for uid {Uid}: {Result}", operation, uid, result);
        }
    }
}
