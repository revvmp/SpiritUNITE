using SpiritUnite.Game.BattleVoice;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.BattleVoice;

public sealed class BattleVoiceHandlers(RoleInfoStore roles, BattleVoiceManager voice)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClientOpenVoiceReq)]
    public GamePacket[] OnClientOpenVoiceReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var voiceType = pbCSClientOpenVoiceReq.Parser.ParseFrom(request.PkgData).VoiceType;
        var result = voice.OpenVoice(request.Uid, voiceType);
        var response = new pbSCClientOpenVoiceRsp { ResultInfo = new pbResultInfo { ResultId = (int)result } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClientOpenVoiceRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClientChgVoiceStatReq)]
    public GamePacket[] OnClientChgVoiceStatReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var change = pbCSClientChgVoiceStatReq.Parser.ParseFrom(request.PkgData);
        var result = voice.ChangeVoiceStat(request.Uid, change.VoiceType, change.IsOpen);
        var response = new pbSCClientChgVoiceStatRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            VoiceType = change.VoiceType,
            IsOpen = change.IsOpen,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClientChgVoiceStatRsp, response)];
    }
}
