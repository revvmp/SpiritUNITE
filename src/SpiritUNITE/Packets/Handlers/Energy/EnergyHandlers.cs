using Microsoft.Extensions.Logging;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Energy;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Energy;

public sealed class EnergyHandlers(
    RoleInfoStore roles,
    GameData gameData,
    TimeProvider clock,
    ILogger<EnergyHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetEnergyInfoReq)]
    public GamePacket[] OnGetEnergyInfoReq(GamePacket request)
    {
        pbCSGetEnergyInfoReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetEnergyInfoRsp, new pbSCGetEnergyInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            EnergyInfo = EnergyCenter.BuildStartingEnergyInfo(),
            NextFlushTime = EnergyCenter.FindNextFlushTime(clock.GetUtcNow()),
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCurRewardPoolReq)]
    public GamePacket[] OnCurRewardPoolReq(GamePacket request)
    {
        var query = pbCSCurRewardPoolReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var response = new pbSCCurRewardPoolRsp { ResultInfo = new pbResultInfo(), PoolType = query.PoolType };
        if (EnergyCenter.IsKnownPoolType(query.PoolType))
        {
            response.RewardPoolID = EnergyCenter.FindCurrentRewardPoolId(gameData, clock.GetUtcNow());
        }
        else
        {
            logger.LogInformation("Energy reward pool type {PoolType} from uid {Uid} is unknown", query.PoolType, request.Uid);
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrEnergyDataNotConfig;
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCurRewardPoolRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidEnergyDrawReq)]
    public GamePacket[] OnEnergyDrawReq(GamePacket request)
    {
        var draw = pbCSEnergyDrawReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        logger.LogInformation(
            "Energy draw x{DrawNum} type {DrawType} pool {PoolType} refused for uid {Uid}: energy draws are not implemented",
            draw.DrawNum, draw.DrawType, draw.PoolType, request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidEnergyDrawRsp, new pbSCEnergyDrawRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrEnergyTicketNotEnough },
            DrawType = draw.DrawType,
            PoolType = draw.PoolType,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUseEnergyAccelerateReq)]
    public GamePacket[] OnUseEnergyAccelerateReq(GamePacket request)
    {
        var accelerate = pbCSUseEnergyAccelerateReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        logger.LogInformation(
            "Energy accelerate ratio {EnergyAccelerateRatio} refused for uid {Uid}: the role has no energy accelerators",
            accelerate.EnergyAccelerateRatio, request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseEnergyAccelerateRsp, new pbSCUseEnergyAccelerateRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid },
            EnergyAccelerateInfo = new pbCOMDT_ENERGY_ACCELERATE_INFO(),
        })];
    }
}
