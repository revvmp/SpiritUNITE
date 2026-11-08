using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Newbie;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Newbie;

public sealed class NewbieGiftHandlers(RoleInfoStore roles, NewbieGiftManager newbieGifts, ILogger<NewbieGiftHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFetchNewbieGiftReq)]
    public async Task<GamePacket[]> OnFetchNewbieGiftReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var giftID = pbCSFetchNewbieGiftReq.Parser.ParseFrom(request.PkgData).GiftID;
        var (result, grants, giftInfo) = await newbieGifts.FetchNewbieGiftAsync(role, giftID);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Newbie gift {GiftID} refused for uid {Uid}: {Result}", giftID, role.GameUlongUID, result);
        }

        return
        [
            .. grants.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFetchNewbieGiftRsp, new pbSCFetchNewbieGiftRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                GiftID = giftID,
                GiftInfo = giftInfo ?? new pbCSDT_NEWBIE_GIFT_INFO(),
            }),
        ];
    }
}
