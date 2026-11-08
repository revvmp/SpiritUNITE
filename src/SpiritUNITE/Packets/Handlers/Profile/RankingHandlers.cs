using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Profile;

public sealed class RankingHandlers(RoleInfoStore roles, ILogger<RankingHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAcntAvatarInfoReq)]
    public async Task<GamePacket[]> OnGetAcntAvatarInfoReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var queryUid = pbCSGetAcntAvatarInfoReq.Parser.ParseFrom(request.PkgData).Uid;
        var queried = queryUid == 0 ? null : await roles.FindAsync(queryUid);
        var response = new pbSCGetAcntAvatarInfoRsp { ResultInfo = new pbResultInfo() };
        if (queried is null)
        {
            logger.LogInformation("Avatar of unknown uid {QueryUid} requested by uid {Uid}", queryUid, request.Uid);
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }
        else
        {
            response.RoleAvatarInfo = queried.RoleAvatarInfo.Clone();
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAcntAvatarInfoRsp, response)];
    }
}
