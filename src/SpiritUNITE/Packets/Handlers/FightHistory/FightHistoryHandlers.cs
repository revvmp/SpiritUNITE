using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.FightHistory;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.FightHistory;

public sealed class FightHistoryHandlers(
    RoleInfoStore roles,
    FightHistoryRecords records,
    ILogger<FightHistoryHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFightHistoryListReq)]
    public async Task<GamePacket[]> OnFightHistoryListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSFightHistoryListReq.Parser.ParseFrom(request.PkgData);
        var response = await records.BuildListAsync(query);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFightHistoryListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFightHistoryDetailReq)]
    public GamePacket[] OnFightHistoryDetailReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSFightHistoryDetailReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation(
            "Fight record {FightRecordIndex} of uid {QueryUid} requested by uid {Uid}: no matches are recorded",
            query.FightRecordIndex, query.QueryUid, request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFightHistoryDetailRsp, FightHistoryRecords.BuildMissingDetail(query))];
    }
}
