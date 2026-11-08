using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Activity;

public sealed class InviteFriendHandlers(RoleInfoStore roles, InviteFriendManager inviteFriends)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBindInviteFriendReq)]
    public async Task<GamePacket[]> OnBindInviteFriendReq(GamePacket request)
    {
        var bind = pbCSBindInviteFriendReq.Parser.ParseFrom(request.PkgData);
        var result = await inviteFriends.BindAsync(roles.Require(request.Uid), bind.FriendShortId);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBindInviteFriendRsp, new pbSCBindInviteFriendRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetInviteFriendMailAwardInfoReq)]
    public async Task<GamePacket[]> OnGetInviteFriendMailAwardInfoReq(GamePacket request)
    {
        pbCSGetInviteFriendMailAwardInfoReq.Parser.ParseFrom(request.PkgData);
        var info = await inviteFriends.GetMailAwardInfoAsync(roles.Require(request.Uid).GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetInviteFriendMailAwardInfoRsp, new pbSCGetInviteFriendMailAwardInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            MailAwardOneCnt = info.MailAwardOneCnt,
            MailAwardTwoCnt = info.MailAwardTwoCnt,
            FriendShortId = info.FriendShortId,
        })];
    }
}
