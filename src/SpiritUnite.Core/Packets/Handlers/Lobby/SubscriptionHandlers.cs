using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Lobby;

public sealed class SubscriptionHandlers(RoleInfoStore roles, WalletManager wallets, LobbyNotices notices, ILogger<SubscriptionHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSubscrEndTimeReq)]
    public GamePacket[] OnSubscrEndTimeReq(GamePacket request)
    {
        pbCSSubscrEndTimeReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCSubscrEndTimeRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSubscrEndTimeRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSubscriptionInterfaceReq)]
    public async Task<GamePacket[]> OnSubscriptionInterfaceReq(GamePacket request)
    {
        pbCSSubscriptionInterfaceReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var wallet = await wallets.LoadAsync(role.GameUlongUID);
        return
        [
            .. notices.BuildSubscriptionInterfaceReplies(wallet.BuildAcntCouponsRsp())
                .Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSubscrCollectCouponsReq)]
    public GamePacket[] OnSubscrCollectCouponsReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSSubscrCollectCouponsReq.Parser.ParseFrom(request.PkgData);
        var result = SubscriptionRules.CollectCoupons();
        logger.LogInformation("Subscription coupons collection for uid {Uid}: {Result}", request.Uid, result);
        var response = new pbSCSubscrCollectCouponsRsp { ResultInfo = new pbResultInfo { ResultId = (int)result } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSubscrCollectCouponsRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSubscrChangeCurBubbleReq)]
    public GamePacket[] OnSubscrChangeCurBubbleReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var bubbleID = pbCSSubscrChangeCurBubbleReq.Parser.ParseFrom(request.PkgData).CurBubbleInfo?.BubbleID ?? 0;
        var result = SubscriptionRules.ValidateBubbleChange(bubbleID);
        logger.LogInformation("Chat bubble change to {BubbleID} for uid {Uid}: {Result}", bubbleID, request.Uid, result);
        var response = new pbSCSubscrChangeCurBubbleRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            CurBubbleInfo = new pbCOMDT_BUBBLE_BASE_INFO { BubbleID = result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc ? bubbleID : SubscriptionRules.DefaultBubbleID },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSubscrChangeCurBubbleRsp, response)];
    }
}
