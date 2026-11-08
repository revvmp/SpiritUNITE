using SpiritUNITE.Game.Accusation;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Accusation;

public sealed class AccusationHandlers(RoleInfoStore roles, AccusationManager accusations)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAccusationReq)]
    public async Task<GamePacket[]> OnAccusationReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var info = pbCSAccusationReq.Parser.ParseFrom(request.PkgData).AccusationInfo;
        var outcome = await accusations.AccuseAsync(request.Uid, info);
        var response = new pbSCAccusationRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            AccusationResult = outcome.AccusationResult,
            Uid = outcome.Uid,
            RoleName = outcome.RoleName,
            CreditPunish = outcome.CreditPunish,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAccusationRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidVoiceAccusationReq)]
    public async Task<GamePacket[]> OnVoiceAccusationReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var accusation = pbCSVoiceAccusationReq.Parser.ParseFrom(request.PkgData);
        var result = await accusations.AccuseVoiceAsync(request.Uid, accusation.AccusationInfo, accusation.VoiceAccusationSource);
        var response = new pbSCVoiceAccusationRsp { ResultInfo = new pbResultInfo { ResultId = (int)result } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidVoiceAccusationRsp, response)];
    }
}
