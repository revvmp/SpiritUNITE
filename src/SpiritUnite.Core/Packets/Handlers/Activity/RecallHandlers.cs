using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Activity;

public sealed class RecallHandlers
{
    private const int REWARED = 2;
    private const PB_ENUM_RES_ERROR_CODE RecallClosed = PB_ENUM_RES_ERROR_CODE.PbErrRecallActivityNotExist;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRecallBattleAwardInfoReq)]
    public GamePacket[] OnRecallBattleAwardInfoReq(GamePacket request)
    {
        pbCSRecallBattleAwardInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRecallBattleAwardInfoRsp, new pbSCRecallBattleAwardInfoRsp())];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRecallBattleAwardReq)]
    public GamePacket[] OnRecallBattleAwardReq(GamePacket request)
    {
        pbCSRecallBattleAwardReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCRecallBattleAwardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)RecallClosed },
            BattleAwardState = REWARED,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRecallBattleAwardRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRecallLoginAwardInfoReq)]
    public GamePacket[] OnRecallLoginAwardInfoReq(GamePacket request)
    {
        pbCSRecallLoginAwardInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRecallLoginAwardInfoRsp, new pbSCRecallLoginAwardInfoRsp())];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRecallLoginAwardReq)]
    public GamePacket[] OnRecallLoginAwardReq(GamePacket request)
    {
        pbCSRecallLoginAwardReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCRecallLoginAwardRsp { ResultInfo = new pbResultInfo { ResultId = (int)RecallClosed } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRecallLoginAwardRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRecallScoreAwardInfoReq)]
    public GamePacket[] OnRecallScoreAwardInfoReq(GamePacket request)
    {
        pbCSRecallScoreAwardInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRecallScoreAwardInfoRsp, new pbSCRecallScoreAwardInfoRsp())];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRecallScoreAwardReq)]
    public GamePacket[] OnRecallScoreAwardReq(GamePacket request)
    {
        pbCSRecallScoreAwardReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCRecallScoreAwardRsp { ResultInfo = new pbResultInfo { ResultId = (int)RecallClosed } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRecallScoreAwardRsp, response)];
    }
}
