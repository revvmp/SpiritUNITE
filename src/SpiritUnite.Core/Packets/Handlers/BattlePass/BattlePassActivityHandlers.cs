using SpiritUnite.Game.BattlePass;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.BattlePass;

public sealed class BattlePassActivityHandlers(RoleInfoStore roles, BattlePassActivities activities)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassActivityStatusReq)]
    public GamePacket[] OnBattlePassActivityStatusReq(GamePacket request)
    {
        var query = pbCSBattlePassActivityStatusReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassActivityStatusRsp, new pbSCBattlePassActivityStatusRsp
        {
            ResultInfo = Refuse(request, "status", query.ActivityID),
            ActivityID = query.ActivityID,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassActivityBuyCertReq)]
    public GamePacket[] OnBattlePassActivityBuyCertReq(GamePacket request)
    {
        var purchase = pbCSBattlePassActivityBuyCertReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassActivityBuyCertRsp, new pbSCBattlePassActivityBuyCertRsp
        {
            ResultInfo = Refuse(request, "certificate purchase", purchase.ActivityID),
            ActivityID = purchase.ActivityID,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassActivityBuyScoreReq)]
    public GamePacket[] OnBattlePassActivityBuyScoreReq(GamePacket request)
    {
        var purchase = pbCSBattlePassActivityBuyScoreReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassActivityBuyScoreRsp, new pbSCBattlePassActivityBuyScoreRsp
        {
            ResultInfo = Refuse(request, "score purchase", purchase.ActivityID),
            ActivityID = purchase.ActivityID,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassActivityGetRewardReq)]
    public GamePacket[] OnBattlePassActivityGetRewardReq(GamePacket request)
    {
        var claim = pbCSBattlePassActivityGetRewardReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassActivityGetRewardRsp, new pbSCBattlePassActivityGetRewardRsp
        {
            ResultInfo = Refuse(request, $"reward {claim.RewardSeq} claim", claim.ActivityID),
            ActivityID = claim.ActivityID,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFriendGiftBpListReq)]
    public GamePacket[] OnFriendGiftBpListReq(GamePacket request)
    {
        var query = pbCSFriendGiftBpListReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFriendGiftBpListRsp, new pbSCFriendGiftBpListRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)activities.RefuseFriendGift(request.Uid, "list") },
            GiveType = query.GiveType,
            FriendListInfo = new pbCSDT_FRIEND_GIFT_BP_REPLY_LIST(),
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFriendGiftBpReq)]
    public GamePacket[] OnFriendGiftBpReq(GamePacket request)
    {
        var gift = pbCSFriendGiftBpReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFriendGiftBpRsp, new pbSCFriendGiftBpRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)activities.RefuseFriendGift(request.Uid, $"to {gift.FriendId}") },
        })];
    }

    private pbResultInfo Refuse(GamePacket request, string action, int activityID) =>
        new() { ResultId = (int)activities.RefuseActivityRequest(request.Uid, action, activityID) };
}
