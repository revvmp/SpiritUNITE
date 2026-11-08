using SpiritUNITE.Game.Activity;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Activity;

public sealed class AllPkmChallengeHandlers(AllPkmChallengeManager allPkmChallenge)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAllPkmChallengeStatusReq)]
    public async Task<GamePacket[]> OnAllPkmChallengeStatusReq(GamePacket request)
    {
        pbCSAllPkmChallengeStatusReq.Parser.ParseFrom(request.PkgData);
        var response = await allPkmChallenge.BuildStatusRspAsync(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAllPkmChallengeStatusRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAllPkmChallengePlayReq)]
    public async Task<GamePacket[]> OnAllPkmChallengePlayReq(GamePacket request)
    {
        var pkmPlayList = pbCSAllPkmChallengePlayReq.Parser.ParseFrom(request.PkgData).PkmPlayList;
        var result = await allPkmChallenge.PlayAsync(request.Uid, pkmPlayList);
        var response = new pbSCAllPkmChallengePlayRsp { ResultInfo = new pbResultInfo { ResultId = (int)result } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAllPkmChallengePlayRsp, response)];
    }
}
