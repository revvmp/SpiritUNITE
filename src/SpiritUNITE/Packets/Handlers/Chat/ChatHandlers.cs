using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Chat;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Chat;

public sealed class ChatHandlers(RoleInfoStore roles, ChatManager chat, FriendManager friends, ILogger<ChatHandlers> logger)
{
    private const int NoTextChatAlarm = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChatReq)]
    public async Task<GamePacket[]> OnChatReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var chatMsg = pbCSChatReq.Parser.ParseFrom(request.PkgData).ChatMsg ?? new pbCOMDT_CHAT_MSG();
        var outcome = await chat.ChatAsync(role, chatMsg);
        if (outcome.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Chat of type {Type} from uid {Uid} refused: {Result}", chatMsg.Type, request.Uid, outcome.Result);
        }

        var response = new pbSCChatRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            ChatMsg = outcome.ChatMsg ?? chatMsg,
            CreditCheckInfo = EmptyChildren.Fill(new pbCOMDT_COMMON_CREDIT_CHECK_INFO()),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChatRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetChatMsgReq)]
    public async Task<GamePacket[]> OnGetChatMsgReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGetChatMsgReq.Parser.ParseFrom(request.PkgData);
        return await chat.TakeOfflineChatAsync(request.Uid) is { } offline
            ? [offline with { Uid = request.Uid, ClientSeq = request.ClientSeq }]
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChatMsgNtf, new pbSCChatMsgNtf())];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCleanFriendChatMsgReq)]
    public async Task<GamePacket[]> OnCleanFriendChatMsgReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var clean = pbCSCleanFriendChatMsgReq.Parser.ParseFrom(request.PkgData);
        await chat.CleanFriendChatMsgAsync(request.Uid, clean.Uid);
        var response = new pbSCCleanFriendChatMsgRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCleanFriendChatMsgRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBanFriendChatReq)]
    public async Task<GamePacket[]> OnBanFriendChatReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var ban = pbCSBanFriendChatReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCBanFriendChatRsp { ResultInfo = new pbResultInfo(), Uid = ban.Uid };
        if (ban.BanChatFlag is not (0 or 1))
        {
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBanFriendChatRsp, response)];
        }

        var result = await friends.SetBanChatFlagAsync(request.Uid, ban.Uid, ban.BanChatFlag);
        var saved = await friends.FindFriendAsync(request.Uid, ban.Uid);
        response.ResultInfo.ResultId = (int)result;
        response.BanChatFlag = saved?.BanChatFlag ?? 0;
        logger.LogInformation("Uid {Uid} set chat ban {BanChatFlag} on uid {FriendUid}: {Result}", request.Uid, ban.BanChatFlag, ban.Uid, result);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBanFriendChatRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcceptChatConventionReq)]
    public async Task<GamePacket[]> OnAcceptChatConventionReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSAcceptChatConventionReq.Parser.ParseFrom(request.PkgData);
        await chat.AcceptChatConventionAsync(request.Uid);
        logger.LogInformation("Uid {Uid} accepted the chat convention", request.Uid);
        var response = new pbSCAcceptChatConventionRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcceptChatConventionRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChatSecurityTextAlarmReq)]
    public GamePacket[] OnChatSecurityTextAlarmReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSChatSecurityTextAlarmReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCChatSecurityTextAlarmRsp { IsTextChatAlarmNtf = NoTextChatAlarm };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChatSecurityTextAlarmRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSubScrGetBubbleInfoReq)]
    public GamePacket[] OnSubScrGetBubbleInfoReq(GamePacket request)
    {
        pbCSSubScrGetBubbleInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCSubScrGetBubbleInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            BubbleInfo = new pbCOMDT_BUBBLE_INFO { BubbleDetail = { SubscriptionRules.OwnedBubbleIDs.Select(bubbleID => new pbCOMDT_BUBBLE_BASE_INFO { BubbleID = bubbleID }) } },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSubScrGetBubbleInfoRsp, response)];
    }
}
