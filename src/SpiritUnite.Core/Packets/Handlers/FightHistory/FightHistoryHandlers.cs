using SpiritUnite.Game.FightHistory;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.FightHistory;

public sealed class FightHistoryHandlers(RoleInfoStore roles, FightHistoryManager fightHistoryManager)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFightHistoryListReq)]
    public async Task<GamePacket[]> OnFightHistoryListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSFightHistoryListReq.Parser.ParseFrom(request.PkgData);
        var responses = await fightHistoryManager.BuildListAsync(query);
        return [.. responses.Select(response => request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFightHistoryListRsp, response))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFightHistoryDetailReq)]
    public async Task<GamePacket[]> OnFightHistoryDetailReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSFightHistoryDetailReq.Parser.ParseFrom(request.PkgData);
        var response = await fightHistoryManager.BuildDetailAsync(query);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFightHistoryDetailRsp, response)];
    }
}
