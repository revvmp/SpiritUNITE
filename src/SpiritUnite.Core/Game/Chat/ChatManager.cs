using System.Collections.Concurrent;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUnite.Battle;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Friend;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Room;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Online;

namespace SpiritUnite.Game.Chat;

public sealed record ChatOutcome(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_CHAT_MSG? ChatMsg = null);

public sealed class ChatManager
{
    public const int MaxChatContentLen = (int)PB_MACRO_COM_MAX_CHAT_CONTENT_LEN.PbComMaxChatContentLen;
    public const int MaxOfflineChatNum = (int)PB_MACRO_COM_MAX_OFFLINE_CHAT_MSG_NUM.PbComMaxOfflineChatMsgNum;
    public const int AcceptedChatConvention = 1;

    private const int FriendChatTextContent = (int)PB_ENUM_ENM_CHAT_CONTENT_TYPE.PbEnmChatContentTypeContent;
    private const int FriendChatFastContent = (int)PB_ENUM_ENM_CHAT_CONTENT_TYPE.PbEnmChatContentTypeFast;
    private const int RoomChatStr = (int)PB_ENUM_ENM_CHAT_ROOM_CHAT_TYPE.PbEnmChatRoomChatTypeStr;
    private const int RoomChatId = (int)PB_ENUM_ENM_CHAT_ROOM_CHAT_TYPE.PbEnmChatRoomChatTypeId;
    private const int BattleChatStr = (int)PB_ENUM_ENM_CHAT_BATTLE_CHAT_TYPE.PbEnmChatBattleChatTypeStr;
    private const int BattleChatId = (int)PB_ENUM_ENM_CHAT_BATTLE_CHAT_TYPE.PbEnmChatBattleChatTypeId;
    private const int BannedChat = 1;

    private static readonly TimeSpan ChatCoolDown = TimeSpan.FromMilliseconds(500);

    private readonly ChatRepository repository;
    private readonly FriendRepository friendRepository;
    private readonly RoomManager rooms;
    private readonly DeskManager desks;
    private readonly LobbyRoles lobbyRoles;
    private readonly TimeProvider clock;
    private readonly ILogger<ChatManager> logger;
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> lastChatTimes = new();

    public ChatManager(
        ChatRepository repository,
        FriendRepository friendRepository,
        RoomManager rooms,
        DeskManager desks,
        LobbyRoles lobbyRoles,
        TimeProvider clock,
        ILogger<ChatManager> logger)
    {
        this.repository = repository;
        this.friendRepository = friendRepository;
        this.rooms = rooms;
        this.desks = desks;
        this.lobbyRoles = lobbyRoles;
        this.clock = clock;
        this.logger = logger;
        lobbyRoles.RoleOnline += DeliverOfflineAsync;
        lobbyRoles.RoleOffline += ForgetAsync;
    }

    public async Task<ChatOutcome> ChatAsync(LMNRoleInfo role, pbCOMDT_CHAT_MSG chatMsg)
    {
        var now = clock.GetUtcNow();
        if (lastChatTimes.TryGetValue(role.GameUlongUID, out var lastChatTime) && now - lastChatTime < ChatCoolDown)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrChatInCd);
        }

        lastChatTimes[role.GameUlongUID] = now;
        return (PB_ENUM_RES_CHAT_MSG_TYPE)chatMsg.Type switch
        {
            PB_ENUM_RES_CHAT_MSG_TYPE.PbResChatMsgTypeFriend when chatMsg.ChatInfo?.PrivateChat is { } privateChat =>
                await ChatToFriendAsync(role, privateChat, now),
            PB_ENUM_RES_CHAT_MSG_TYPE.PbResChatMsgTypeRoom when chatMsg.ChatInfo?.Room is { } room =>
                await ChatToRoomAsync(role, room, now),
            PB_ENUM_RES_CHAT_MSG_TYPE.PbResChatMsgTypeBattle when chatMsg.ChatInfo?.Battle is { } battle =>
                await ChatToHeroSelectionAsync(role, battle, now),
            _ => new(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid),
        };
    }

    public Task AcceptChatConventionAsync(ulong uid) =>
        friendRepository.SaveAcceptChatConventionAsync(uid, AcceptedChatConvention);

    public async Task CleanFriendChatMsgAsync(ulong uid, ulong friendUid)
    {
        var deleted = await repository.DeleteOfflineAsync(uid, friendUid);
        logger.LogInformation("Uid {Uid} cleaned chat with uid {FriendUid}, {Count} undelivered messages", uid, friendUid, deleted);
    }

    public async Task<GamePacket?> TakeOfflineChatAsync(ulong uid)
    {
        var chats = await repository.TakeOfflineAsync(uid);
        if (chats.Length == 0)
        {
            return null;
        }

        var notify = new pbSCChatMsgNtf
        {
            ChatMsg = { chats.Select(chat => pbCOMDT_CHAT_MSG.Parser.ParseFrom(chat.ChatMsg)) },
            TimeStamp = chats[^1].ChatTime,
        };
        logger.LogInformation("Delivering {Count} offline chat messages to uid {Uid}", chats.Length, uid);
        return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidChatMsgNtf, notify);
    }

    private async Task<ChatOutcome> ChatToFriendAsync(LMNRoleInfo role, pbCOMDT_CHAT_MSG_PRIVATE privateChat, DateTimeOffset now)
    {
        var uid = role.GameUlongUID;
        var destUid = privateChat.DestUid;
        var contentRefusal = ValidateContent(privateChat.ContentType, privateChat.Content, privateChat.TextID, FriendChatTextContent, FriendChatFastContent);
        if (contentRefusal != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return new(contentRefusal);
        }

        if (destUid == uid || await friendRepository.FindFriendAsync(uid, destUid) is null)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrNotFriend);
        }

        var chatMsg = new pbCOMDT_CHAT_MSG
        {
            Type = (int)PB_ENUM_RES_CHAT_MSG_TYPE.PbResChatMsgTypeFriend,
            ChatInfo = new pbCOMDT_CHAT_MSG_UNION
            {
                PrivateChat = new pbCOMDT_CHAT_MSG_PRIVATE
                {
                    From = RoleBrief.BuildChatPlayerInfo(role),
                    DestUid = destUid,
                    ChatTime = now.ToUnixTimeSeconds(),
                    ContentType = privateChat.ContentType,
                    Content = privateChat.ContentType == FriendChatTextContent ? privateChat.Content : string.Empty,
                    TextID = privateChat.ContentType == FriendChatFastContent ? privateChat.TextID : 0,
                },
            },
        };

        var destFriend = await friendRepository.FindFriendAsync(destUid, uid);
        if (destFriend is { BanChatFlag: BannedChat })
        {
            logger.LogInformation("Chat from uid {Uid} to uid {DestUid} dropped: sender is blocked", uid, destUid);
            return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, chatMsg);
        }

        var delivered = await lobbyRoles.PushAsync(destUid, GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidChatMsgNtf,
            new pbSCChatMsgNtf { ChatMsg = { chatMsg }, TimeStamp = now.ToUnixTimeSeconds() }));
        if (!delivered)
        {
            await repository.SaveOfflineAsync(new RoleChatOffline
            {
                DestUid = destUid,
                FromUid = uid,
                ChatTime = now.ToUnixTimeSeconds(),
                ChatMsg = chatMsg.ToByteArray(),
            }, MaxOfflineChatNum);
        }

        logger.LogInformation("Chat from uid {Uid} to uid {DestUid}, {Delivery}", uid, destUid, delivered ? "delivered" : "stored offline");
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, chatMsg);
    }

    private async Task<ChatOutcome> ChatToRoomAsync(LMNRoleInfo role, pbCOMDT_CHAT_MSG_ROOM room, DateTimeOffset now)
    {
        var uid = role.GameUlongUID;
        var contentRefusal = ValidateContent(room.Type, room.Content, room.TextID, RoomChatStr, RoomChatId);
        if (contentRefusal != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return new(contentRefusal);
        }

        if (rooms.FindRoomOf(uid) is not { } matchRoom)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrChatException);
        }

        var chatMsg = new pbCOMDT_CHAT_MSG
        {
            Type = (int)PB_ENUM_RES_CHAT_MSG_TYPE.PbResChatMsgTypeRoom,
            ChatInfo = new pbCOMDT_CHAT_MSG_UNION
            {
                Room = new pbCOMDT_CHAT_MSG_ROOM
                {
                    From = RoleBrief.BuildChatPlayerInfo(role),
                    Type = room.Type,
                    Content = room.Type == RoomChatStr ? room.Content : string.Empty,
                    TextID = room.Type == RoomChatId ? room.TextID : 0,
                },
            },
        };
        var notify = GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidChatMsgNtf,
            new pbSCChatMsgNtf { ChatMsg = { chatMsg }, TimeStamp = now.ToUnixTimeSeconds() });
        ulong[] memberUids = [.. matchRoom.Members.Select(member => member.Uid).Where(memberUid => memberUid != uid)];
        foreach (var memberUid in memberUids)
        {
            await lobbyRoles.PushAsync(memberUid, notify);
        }

        logger.LogInformation("Room chat from uid {Uid} to {Count} room members", uid, memberUids.Length);
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, chatMsg);
    }

    private async Task<ChatOutcome> ChatToHeroSelectionAsync(LMNRoleInfo role, pbCOMDT_CHAT_MSG_BATTLE battle, DateTimeOffset now)
    {
        var uid = role.GameUlongUID;
        var contentRefusal = ValidateContent(
            battle.Type,
            battle.ChatInfo?.StrChat?.Content ?? string.Empty,
            battle.ChatInfo?.IdChat?.TextID ?? 0,
            BattleChatStr,
            BattleChatId);
        if (contentRefusal != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return new(contentRefusal);
        }

        if (desks.FindPickingCampmateUids(uid) is not { } campmateUids)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk);
        }

        var chatMsg = new pbCOMDT_CHAT_MSG
        {
            Type = (int)PB_ENUM_RES_CHAT_MSG_TYPE.PbResChatMsgTypeBattle,
            ChatInfo = new pbCOMDT_CHAT_MSG_UNION
            {
                Battle = new pbCOMDT_CHAT_MSG_BATTLE
                {
                    From = RoleBrief.BuildChatPlayerInfo(role),
                    Type = battle.Type,
                    ChatInfo = battle.Type == BattleChatId
                        ? new pbCOMDT_CHAT_BATTLE_MSG_UNION { IdChat = new pbCOMDT_CHAT_BATTLE_CHAT_ID { TextID = battle.ChatInfo!.IdChat!.TextID } }
                        : new pbCOMDT_CHAT_BATTLE_MSG_UNION { StrChat = new pbCOMDT_CHAT_BATTLE_CHAT_STR { Content = battle.ChatInfo!.StrChat!.Content } },
                },
            },
        };
        var notify = GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidChatMsgNtf,
            new pbSCChatMsgNtf { ChatMsg = { chatMsg }, TimeStamp = now.ToUnixTimeSeconds() });
        foreach (var campmateUid in campmateUids)
        {
            await lobbyRoles.PushAsync(campmateUid, notify);
        }

        logger.LogInformation("Hero selection chat from uid {Uid} to {Count} campmates", uid, campmateUids.Length);
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, chatMsg);
    }

    private static PB_ENUM_RES_ERROR_CODE ValidateContent(int contentType, string content, int textID, int textContentType, int fastContentType)
    {
        if (contentType == fastContentType)
        {
            return textID > 0 ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : PB_ENUM_RES_ERROR_CODE.PbErrFastChatCfgNotMeet;
        }

        if (contentType != textContentType)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        return string.IsNullOrWhiteSpace(content)
            || Encoding.UTF8.GetByteCount(content) > MaxChatContentLen
            || content.Any(character => char.IsControl(character))
            ? PB_ENUM_RES_ERROR_CODE.PbErrInvaildContent
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private async Task DeliverOfflineAsync(ulong uid)
    {
        if (await TakeOfflineChatAsync(uid) is { } offline)
        {
            await lobbyRoles.PushAsync(uid, offline);
        }
    }

    private Task ForgetAsync(ulong uid)
    {
        lastChatTimes.TryRemove(uid, out _);
        return Task.CompletedTask;
    }
}
