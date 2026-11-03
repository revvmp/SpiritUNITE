using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class ProfileHandlers(RoleInfoStore roles, ILogger<ProfileHandlers> logger)
{
    private const int MaximumContextLength = 64;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAcntProfileNewReq)]
    public async Task<GamePacket[]> OnGetAcntProfileNewReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSGetAcntProfileNewReq.Parser.ParseFrom(request.PkgData);
        if (query.Context.Length > MaximumContextLength)
        {
            throw new InvalidDataException($"Profile context length {query.Context.Length} is too long");
        }

        var queried = await FindQueriedRoleAsync(query.QueryUid);
        var response = new pbSCGetAcntProfileNewRsp
        {
            ResultInfo = new pbResultInfo(),
            Context = query.Context,
        };
        if (queried is null)
        {
            logger.LogInformation("Profile of unknown uid {QueryUid} requested by uid {Uid}", query.QueryUid, request.Uid);
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }
        else
        {
            response.ProfileDetail = RoleProfile.BuildProfileDetail(queried);
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAcntProfileNewRsp, EmptyChildren.Fill(response))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidProfileImageInfoReq)]
    public async Task<GamePacket[]> OnProfileImageInfoReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSProfileImageInfoReq.Parser.ParseFrom(request.PkgData);
        var queried = await FindQueriedRoleAsync(query.QueryUid);
        if (queried is null)
        {
            logger.LogInformation("Profile image of unknown uid {QueryUid} requested by uid {Uid}", query.QueryUid, request.Uid);
            var refusal = new pbSCProfileImageInfoRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid },
                QueryUid = query.QueryUid,
            };
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidProfileImageInfoRsp, EmptyChildren.Fill(refusal))];
        }

        var response = RoleProfile.BuildProfileImageInfo(queried);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidProfileImageInfoRsp, EmptyChildren.Fill(response))];
    }

    private async Task<LMNRoleInfo?> FindQueriedRoleAsync(ulong queryUid) =>
        queryUid == 0 ? null : await roles.FindAsync(queryUid);
}
