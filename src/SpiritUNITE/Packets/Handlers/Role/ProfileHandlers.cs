using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Role;

public sealed class ProfileHandlers(RoleInfoStore roles, GameData gameData, TimeProvider clock, ILogger<ProfileHandlers> logger)
{
    private const int MaximumContextLength = 64;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryTrainerExpGiftReq)]
    public GamePacket[] OnQueryTrainerExpGiftReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var query = pbCSQueryTrainerExpGiftReq.Parser.ParseFrom(request.PkgData);
        var validGiftType = Enum.IsDefined((PB_ENUM_ENM_TRAINER_EXP_GIFT_TYPE)query.GiftType);
        var response = new pbSCQueryTrainerExpGiftRsp
        {
            ResultInfo = new pbResultInfo
            {
                ResultId = (int)(validGiftType
                    ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
                    : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid),
            },
            GiftType = validGiftType ? query.GiftType : (int)PB_ENUM_ENM_TRAINER_EXP_GIFT_TYPE.PbEnmTrainerExpGiftTypeNormal,
            RoleTotalExp = role.RoleTotalExp,
            RoleHistoryMaxTotalExp = role.RoleHistoryMaxTotalExp,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            SeasonId = RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQueryTrainerExpGiftRsp, response)];
    }

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
            response.ProfileDetail = RoleProfile.BuildProfileDetail(gameData, queried);
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
