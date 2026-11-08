using SpiritUnite.Game.Role;
using SpiritUnite.Game.TrainerExpGift;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.TrainerExpGift;

public sealed class TrainerExpGiftHandlers(RoleInfoStore roles, TrainerExpGiftManager trainerExpGiftManager)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryTrainerExpGiftReq)]
    public GamePacket[] OnQueryTrainerExpGiftReq(GamePacket request)
    {
        pbCSQueryTrainerExpGiftReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQueryTrainerExpGiftRsp, trainerExpGiftManager.BuildQueryTrainerExpGiftRsp(role))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetTrainerExpGiftReq)]
    public async Task<GamePacket[]> OnGetTrainerExpGiftReq(GamePacket request)
    {
        var claimRequest = pbCSGetTrainerExpGiftReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var claim = await trainerExpGiftManager.ClaimAsync(role, claimRequest.GiftType, claimRequest.RewardExp);
        if (claim.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await roles.SaveAsync(role);
        }

        var response = new pbSCGetTrainerExpGiftRsp
        {
            ResultInfo = claim.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
                ? new pbResultInfo()
                : new pbResultInfo { ResultId = (int)claim.Result },
            RewardExp = claimRequest.RewardExp,
        };
        return
        [
            .. claim.BeforeRsp.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetTrainerExpGiftRsp, response),
            .. claim.AfterRsp.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
        ];
    }
}
