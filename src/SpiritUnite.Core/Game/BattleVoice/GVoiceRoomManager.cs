using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUnite.Battle;
using SpiritUnite.Game.Room;
using SpiritUnite.Network.GVoice;
using SpiritUnite.Network.GVoice.Messages;

namespace SpiritUnite.Game.BattleVoice;

public sealed record GVoiceSend(IPEndPoint Target, byte[] Datagram);

public sealed record GVoiceJoinOutcome(uint ErrCode, string ErrMsg, GVoiceRoom? Room, GVoiceMember? Member, IReadOnlyList<GVoiceSend> Pushes)
{
    public static GVoiceJoinOutcome Refused(uint errCode, string errMsg) => new(errCode, errMsg, null, null, []);
}

public sealed class GVoiceRoomManager(RoomManager rooms, DeskManager desks, ILogger<GVoiceRoomManager> logger)
{
    public const uint JoinRefusedNotRoomMember = 1;
    public const uint JoinRefusedRoomFull = 2;
    public const int MaxRooms = 4096;
    public const int MaxMembersPerRoom = 16;
    public const long MemberTimeoutMs = 60_000;
    public const int MaxPrivateInfoLength = 256;
    public const int MaxAppIdentityLength = 128;

    private const uint MemberActionInRoom = 1;
    private const uint MemberActionQuit = 2;
    private const uint MemberActionTimeout = 4;
    private const uint ClientActionJoin = 1;
    private const uint ClientActionQuit = 2;
    private const uint LossRateScale = 10_000;

    private readonly Lock gate = new();
    private readonly Dictionary<string, GVoiceRoom> roomsByName = [];
    private readonly Dictionary<ulong, GVoiceRoom> roomsById = [];

    public GVoiceJoinOutcome Join(string openId, string roomName, uint clientNetType, long nowMs)
    {
        if (!ulong.TryParse(openId, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) || uid == 0
            || !GVoiceRoomName.TryParse(roomName, out var name)
            || !name.AdmitsMember(uid, rooms, desks))
        {
            logger.LogWarning("Refused voice join of openid {OpenId} to room {RoomName}: not a member of that lobby room or desk camp", openId, roomName);
            return GVoiceJoinOutcome.Refused(JoinRefusedNotRoomMember, "not a member of this room");
        }

        lock (gate)
        {
            var pushes = new List<GVoiceSend>();
            foreach (var other in roomsByName.Values.ToArray())
            {
                if (other.Name != roomName && other.FindByUid(uid) is { } stale)
                {
                    logger.LogInformation("Voice member {MemberId} (uid {Uid}) left room {RoomName} to join {NewRoom}", stale.MemberId, uid, other.Name, roomName);
                    pushes.AddRange(RemoveLocked(other, stale, MemberActionQuit));
                }
            }

            if (!roomsByName.TryGetValue(roomName, out var room))
            {
                if (roomsByName.Count >= MaxRooms)
                {
                    logger.LogWarning("Refused voice join of uid {Uid} to room {RoomName}: {MaxRooms} voice rooms are open", uid, roomName, MaxRooms);
                    return new GVoiceJoinOutcome(JoinRefusedRoomFull, "too many voice rooms", null, null, pushes);
                }

                room = new GVoiceRoom(roomName, NewRoomId(), RandomNonZeroUInt64());
                roomsByName[roomName] = room;
                roomsById[room.RoomId] = room;
                logger.LogInformation("Opened voice room {RoomName} as room_id {RoomId}", roomName, room.RoomId);
            }

            var member = room.FindByUid(uid);
            if (member is null)
            {
                if (room.Members.Count >= MaxMembersPerRoom)
                {
                    logger.LogWarning("Refused voice join of uid {Uid} to room {RoomName}: {MaxMembers} members already", uid, roomName, MaxMembersPerRoom);
                    return new GVoiceJoinOutcome(JoinRefusedRoomFull, "voice room is full", null, null, pushes);
                }

                member = room.Add(uid, openId, clientNetType, nowMs);
                logger.LogInformation("Voice join: uid {Uid} is member {MemberId} of room {RoomName}", uid, member.MemberId, roomName);
            }
            else
            {
                member.ClientNetType = clientNetType;
                member.LastHeardMs = nowMs;
            }

            return new GVoiceJoinOutcome(GVoiceReplies.ReplySucc, GVoiceReplies.ReplyOk, room, member, pushes);
        }
    }

    public IReadOnlyList<GVoiceSend> HandleSignal(CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        lock (gate)
        {
            if (!roomsById.TryGetValue(signal.Head.RoomId, out var room)
                || signal.Head.MemberId > ushort.MaxValue
                || room.Find((ushort)signal.Head.MemberId) is not { } member
                || member.OpenId != signal.Head.OpenId)
            {
                logger.LogWarning("Refused voice signal {Cmd} from {Source}: room_id, member_id and open_id do not name a joined member", signal.Cmd, source);
                return [];
            }

            return signal.Cmd switch
            {
                VOIP_CMD.VOIP_CMD_CHECKIN_REQ => CheckIn(room, member, signal, source, nowMs),
                VOIP_CMD.VOIP_CMD_STAT_REQ => Stat(room, member, signal, source, nowMs),
                VOIP_CMD.VOIP_CMD_AUDIO_OPEN_REQ => OpenVoice(room, member, signal, source, nowMs),
                VOIP_CMD.VOIP_CMD_AUDIO_CLOSE_REQ => CloseVoice(room, member, signal, source, nowMs),
                VOIP_CMD.VOIP_CMD_EXIT_REQ => Exit(room, member, signal, source),
                _ => Ignore(member, signal, source, nowMs),
            };
        }
    }

    public IReadOnlyList<GVoiceSend> HandleRtcp(QosMsg message, IPEndPoint source, long nowMs)
    {
        lock (gate)
        {
            if (message.ClientReportReq is { } report)
            {
                return Report(report, source, nowMs);
            }

            if (message.SynchronizeMemInfoRsp is { RoomInfo: { } ackRoom } && FindReadyAt(ackRoom.RoomId, source) is { } member)
            {
                member.LastHeardMs = nowMs;
            }

            return [];
        }
    }

    public IReadOnlyList<GVoiceSend> RelayMedia(CDNVDataHead head, byte[] datagram, IPEndPoint source, long nowMs)
    {
        lock (gate)
        {
            if (!roomsById.TryGetValue(head.RoomId, out var room)
                || room.Find(head.MemberId) is not { IsReady: true } sender
                || !source.Equals(sender.AccessEndPoint))
            {
                logger.LogDebug("Dropped voice media for room_id {RoomId} member {MemberId} from {Source}: not a ready member at that address", head.RoomId, head.MemberId, source);
                return [];
            }

            sender.LastHeardMs = nowMs;
            sender.CountMedia(head.Sequence);
            return [.. room.ReadyMembers
                .Where(member => member != sender && member.IsReceiving && member.AccessEndPoint is not null)
                .Select(member => new GVoiceSend(member.AccessEndPoint!, datagram))];
        }
    }

    public IReadOnlyList<GVoiceSend> ExpireIdleMembers(long nowMs)
    {
        lock (gate)
        {
            var pushes = new List<GVoiceSend>();
            foreach (var room in roomsByName.Values.ToArray())
            {
                foreach (var member in room.Members.Where(member => nowMs - member.LastHeardMs > MemberTimeoutMs).ToArray())
                {
                    logger.LogInformation("Voice member {MemberId} (uid {Uid}) of room {RoomName} timed out after {TimeoutSec} s of silence", member.MemberId, member.Uid, room.Name, MemberTimeoutMs / 1000);
                    pushes.AddRange(RemoveLocked(room, member, MemberActionTimeout));
                }
            }

            return pushes;
        }
    }

    private List<GVoiceSend> CheckIn(GVoiceRoom room, GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        if (!signal.TryReadBody(room.RoomKey, VoipCheckInReq.Parser, out var request))
        {
            logger.LogWarning("Refused voice check-in of member {MemberId} in room {RoomName}: body does not decrypt with the room key", member.MemberId, room.Name);
            return [];
        }

        Heard(member, signal, source, nowMs);
        var clientIp = ClientIpOf(source);
        var clientPort = (uint)source.Port;
        if (request.ClientIp != clientIp || request.ClientPort != clientPort)
        {
            logger.LogInformation("Voice check-in 1 of member {MemberId} in room {RoomName} from {Source}", member.MemberId, room.Name, source);
            return [Signal(room, member, VOIP_CMD.VOIP_CMD_CHECKIN_RSP, new VoipCheckInRsp { ClientIp = clientIp, ClientPort = clientPort })];
        }

        var sends = new List<GVoiceSend> { Signal(room, member, VOIP_CMD.VOIP_CMD_CHECKIN_READY, new VoipCheckInReady()) };
        if (!member.IsReady)
        {
            member.IsReady = true;
            room.Changed();
            logger.LogInformation("Voice check-in 2: member {MemberId} (uid {Uid}) is ready in room {RoomName} with {Count} ready members", member.MemberId, member.Uid, room.Name, room.ReadyMembers.Count());
            sends.AddRange(MemberListToOthers(room, member));
        }

        return sends;
    }

    private List<GVoiceSend> Stat(GVoiceRoom room, GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        if (!signal.TryReadBody(room.RoomKey, VoipStatReq.Parser, out var request))
        {
            logger.LogWarning("Refused voice stat of member {MemberId} in room {RoomName}: body does not decrypt with the room key", member.MemberId, room.Name);
            return [];
        }

        Heard(member, signal, source, nowMs);
        return [Signal(room, member, VOIP_CMD.VOIP_CMD_STAT_RSP, new VoipStatRsp { UpRate = member.TakeUpLossRate(LossRateScale) })];
    }

    private List<GVoiceSend> OpenVoice(GVoiceRoom room, GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        if (!signal.TryReadBody(room.RoomKey, OpenVoiceReq.Parser, out var request))
        {
            logger.LogWarning("Refused voice open of member {MemberId} in room {RoomName}: body does not decrypt with the room key", member.MemberId, room.Name);
            return [];
        }

        Heard(member, signal, source, nowMs);
        if (!member.IsReceiving)
        {
            logger.LogInformation("Voice member {MemberId} of room {RoomName} opened its speaker", member.MemberId, room.Name);
        }

        member.IsReceiving = true;
        return [Signal(room, member, VOIP_CMD.VOIP_CMD_AUDIO_OPEN_RSP, new OpenVoiceRsp { ClientIp = request.ClientIp, ClientPort = request.ClientPort })];
    }

    private List<GVoiceSend> CloseVoice(GVoiceRoom room, GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        if (!signal.TryReadBody(room.RoomKey, CloseVoiceReq.Parser, out var request))
        {
            logger.LogWarning("Refused voice close of member {MemberId} in room {RoomName}: body does not decrypt with the room key", member.MemberId, room.Name);
            return [];
        }

        Heard(member, signal, source, nowMs);
        logger.LogInformation("Voice member {MemberId} of room {RoomName} closed its speaker", member.MemberId, room.Name);
        member.IsReceiving = false;
        return [Signal(room, member, VOIP_CMD.VOIP_CMD_AUDIO_CLOSE_RSP, new CloseVoiceRsp { ClientIp = request.ClientIp, ClientPort = request.ClientPort })];
    }

    private List<GVoiceSend> Exit(GVoiceRoom room, GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source)
    {
        if (!source.Equals(member.AccessEndPoint) && member.IsReady)
        {
            logger.LogWarning("Refused voice exit of member {MemberId} in room {RoomName} from {Source}: not its access address", member.MemberId, room.Name, source);
            return [];
        }

        logger.LogInformation("Voice member {MemberId} (uid {Uid}) exited room {RoomName}", member.MemberId, member.Uid, room.Name);
        return RemoveLocked(room, member, MemberActionQuit);
    }

    private List<GVoiceSend> Ignore(GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        if (source.Equals(member.AccessEndPoint))
        {
            member.LastHeardMs = nowMs;
        }

        logger.LogDebug("Voice signal {Cmd} from member {MemberId} has no reply", signal.Cmd, member.MemberId);
        return [];
    }

    private List<GVoiceSend> Report(ClientReportReq report, IPEndPoint source, long nowMs)
    {
        if (report.RoomInfo is not { } roomInfo
            || report.MemberInfo is not { } memberInfo
            || FindReadyAt(roomInfo.RoomId, source) is not { } member
            || memberInfo.MemberId != member.MemberId
            || memberInfo.OpenId.ToStringUtf8() != member.OpenId
            || !roomsById.TryGetValue(roomInfo.RoomId, out var room))
        {
            logger.LogWarning("Refused voice member report from {Source}: it does not name a ready member at that address", source);
            return [];
        }

        member.LastHeardMs = nowMs;
        if (memberInfo.Action == ClientActionQuit)
        {
            logger.LogInformation("Voice member {MemberId} (uid {Uid}) reported leaving room {RoomName}", member.MemberId, member.Uid, room.Name);
            return RemoveLocked(room, member, MemberActionQuit);
        }

        if (roomInfo.AppIdentity.Length <= MaxAppIdentityLength)
        {
            member.AppIdentity = roomInfo.AppIdentity;
        }

        var privateInfo = memberInfo.HasClientPrivateInfo && memberInfo.ClientPrivateInfo.Length <= MaxPrivateInfoLength
            ? memberInfo.ClientPrivateInfo
            : member.PrivateInfo;
        var changed = !privateInfo.Equals(member.PrivateInfo);
        if (changed)
        {
            member.PrivateInfo = privateInfo;
            room.Changed();
            logger.LogInformation("Voice member {MemberId} of room {RoomName} reported status {Status}", member.MemberId, room.Name, StatusOf(privateInfo));
        }

        var sends = new List<GVoiceSend> { MemberList(room, member, []) };
        if (changed || memberInfo.Action == ClientActionJoin)
        {
            sends.AddRange(MemberListToOthers(room, member));
        }

        return sends;
    }

    private GVoiceMember? FindReadyAt(ulong roomId, IPEndPoint source) =>
        roomsById.TryGetValue(roomId, out var room)
            ? room.ReadyMembers.FirstOrDefault(member => source.Equals(member.AccessEndPoint))
            : null;

    private List<GVoiceSend> RemoveLocked(GVoiceRoom room, GVoiceMember member, uint action)
    {
        var wasReady = member.IsReady;
        room.Remove(member);
        if (room.Members.Count == 0)
        {
            roomsByName.Remove(room.Name);
            roomsById.Remove(room.RoomId);
            logger.LogInformation("Closed voice room {RoomName}", room.Name);
            return [];
        }

        if (!wasReady)
        {
            return [];
        }

        room.Changed();
        var leaving = new SynMemberInfo
        {
            OpenId = ByteString.CopyFromUtf8(member.OpenId),
            MemberId = member.MemberId,
            Action = action,
        };
        return [.. room.ReadyMembers.Where(other => other.AccessEndPoint is not null).Select(other => MemberList(room, other, [leaving]))];
    }

    private static IEnumerable<GVoiceSend> MemberListToOthers(GVoiceRoom room, GVoiceMember changed) =>
        room.ReadyMembers
            .Where(other => other != changed && other.AccessEndPoint is not null)
            .Select(other => MemberList(room, other, []))
            .ToArray();

    private static GVoiceSend MemberList(GVoiceRoom room, GVoiceMember recipient, IReadOnlyList<SynMemberInfo> leaving)
    {
        var sync = new SynchronizeMemInfoReq
        {
            RoomInfo = new RoomInfo
            {
                AppIdentity = recipient.AppIdentity,
                RoomName = ByteString.CopyFromUtf8(room.Name),
                RoomId = room.RoomId,
            },
            Version = room.Version,
        };
        foreach (var member in room.ReadyMembers)
        {
            var info = new SynMemberInfo
            {
                OpenId = ByteString.CopyFromUtf8(member.OpenId),
                MemberId = member.MemberId,
                Action = MemberActionInRoom,
            };
            if (!member.PrivateInfo.IsEmpty)
            {
                info.ClientPrivateInfo = member.PrivateInfo;
            }

            sync.MemberInfos.Add(info);
        }

        sync.MemberInfos.Add(leaving);
        return new GVoiceSend(recipient.AccessEndPoint!, CDNVProtoRtcp.Build(new QosMsg { SynchronizeMemInfoReq = sync }));
    }

    private static GVoiceSend Signal(GVoiceRoom room, GVoiceMember recipient, VOIP_CMD cmd, IMessage body)
    {
        var head = new VoipClient2AccessHead
        {
            Cmd = (uint)cmd,
            ProtoType = CDNVProtoSignal.DaVoipProtoType,
            RoomId = room.RoomId,
            MemberId = recipient.MemberId,
            OpenId = recipient.OpenId,
            ClientNetType = recipient.ClientNetType,
            SvrStat = 0,
        };
        return new GVoiceSend(recipient.AccessEndPoint!, CDNVProtoSignal.Build(head, body, room.RoomKey));
    }

    private static void Heard(GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        member.AccessEndPoint = source;
        member.LastHeardMs = nowMs;
        if (signal.Head.HasClientNetType)
        {
            member.ClientNetType = signal.Head.ClientNetType;
        }
    }

    private static uint ClientIpOf(IPEndPoint source)
    {
        var address = source.Address.IsIPv4MappedToIPv6 ? source.Address.MapToIPv4() : source.Address;
        return address.AddressFamily == AddressFamily.InterNetwork
            ? BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes())
            : 0;
    }

    private static string StatusOf(ByteString privateInfo)
    {
        try
        {
            var info = ClientMemberInfo.Parser.ParseFrom(privateInfo);
            return info.HasStatus ? $"0x{info.Status:x}" : "none";
        }
        catch (InvalidProtocolBufferException)
        {
            return "unreadable";
        }
    }

    private ulong NewRoomId()
    {
        ulong roomId;
        do
        {
            roomId = RandomNonZeroUInt64();
        }
        while (roomsById.ContainsKey(roomId));

        return roomId;
    }

    private static ulong RandomNonZeroUInt64()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        ulong value;
        do
        {
            RandomNumberGenerator.Fill(bytes);
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }
        while (value == 0);

        return value;
    }
}
