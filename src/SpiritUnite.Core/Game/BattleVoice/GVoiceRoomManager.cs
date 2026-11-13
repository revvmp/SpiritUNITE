using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUnite.Network.GVoice;
using SpiritUnite.Network.GVoice.Messages;
using SpiritUnite.Online;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.BattleVoice;

public sealed record GVoiceSend(IPEndPoint Target, byte[] Datagram);

public sealed record GVoiceJoinOutcome(uint ErrCode, string ErrMsg, GVoiceRoom? Room, GVoiceMember? Member, IReadOnlyList<GVoiceSend> Pushes, bool IsAnswered = true)
{
    public static GVoiceJoinOutcome Refused(uint errCode, string errMsg) => new(errCode, errMsg, null, null, []);

    public static GVoiceJoinOutcome Unanswered(uint errCode, string errMsg) => new(errCode, errMsg, null, null, [], IsAnswered: false);
}

public sealed class GVoiceRoomManager
{
    public const uint JoinRefusedNotRoomMember = 1;
    public const uint JoinRefusedRoomFull = 2;
    public const uint JoinUnansweredLiveElsewhere = 3;
    public const int MaxRooms = 4096;
    public const int MaxMembersPerRoom = 16;
    public const long MemberTimeoutMs = 60_000;
    public const long ClientStatPeriodMs = 3_000;
    public const long LiveEndpointQuietMs = 7_000;
    public const int MaxPrivateInfoLength = 256;
    public const int MaxAppIdentityLength = 128;

    private const uint MemberActionInRoom = 1;
    private const uint MemberActionQuit = 2;
    private const uint MemberActionTimeout = 4;
    private const uint ClientActionJoin = 1;
    private const uint ClientActionQuit = 2;
    private const uint LossRateScale = 10_000;

    private const int RefusalLogBurst = 5;
    private const long RefusalLogRefillMs = 10_000;
    private const int AllRefusalLogsBurst = 20;
    private const long AllRefusalLogsRefillMs = 50;
    private const int AnySource = 0;
    private const long PerUidLogRefillMs = 60_000;
    private const int MaxTrackedSources = 16_384;
    private const int IPv6RateLimitPrefixBytes = 8;

    private readonly Lock gate = new();
    private readonly Dictionary<string, GVoiceRoom> roomsByName = [];
    private readonly Dictionary<ulong, GVoiceRoom> roomsById = [];
    private readonly List<GVoiceSend> pendingSends = [];
    private readonly KeyedTokenBuckets<IPAddress> refusalLogs = new(RefusalLogBurst, RefusalLogRefillMs, MaxTrackedSources);
    private readonly KeyedTokenBuckets<int> allRefusalLogs = new(AllRefusalLogsBurst, AllRefusalLogsRefillMs, 1);
    private readonly KeyedTokenBuckets<ulong> lobbyAddressMismatchLogs = new(1, PerUidLogRefillMs, MaxTrackedSources);
    private readonly KeyedTokenBuckets<ulong> liveElsewhereLogs = new(1, PerUidLogRefillMs, MaxTrackedSources);
    private readonly KeyedTokenBuckets<ulong> takeoverLogs = new(1, PerUidLogRefillMs, MaxTrackedSources);
    private readonly BattleVoiceManager battleVoice;
    private readonly LobbyRoles lobbyRoles;
    private readonly ILogger<GVoiceRoomManager> logger;
    private long suppressedRefusalLogs;

    public GVoiceRoomManager(BattleVoiceManager battleVoice, LobbyRoles lobbyRoles, ILogger<GVoiceRoomManager> logger)
    {
        this.battleVoice = battleVoice;
        this.lobbyRoles = lobbyRoles;
        this.logger = logger;
        battleVoice.GvoiceRoomRevoked += Revoke;
    }

    public static IPAddress SourceAddressOf(IPEndPoint source) =>
        source.Address.IsIPv4MappedToIPv6 ? source.Address.MapToIPv4() : source.Address;

    public static IPAddress RateLimitKeyOf(IPEndPoint source)
    {
        var address = SourceAddressOf(source);
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address;
        }

        var bytes = address.GetAddressBytes();
        bytes.AsSpan(IPv6RateLimitPrefixBytes).Clear();
        return new IPAddress(bytes);
    }

    public bool ShouldLogRefusal(IPEndPoint source, long nowMs)
    {
        if (refusalLogs.TryTake(RateLimitKeyOf(source), nowMs) && allRefusalLogs.TryTake(AnySource, nowMs))
        {
            return true;
        }

        Interlocked.Increment(ref suppressedRefusalLogs);
        return false;
    }

    public long TakeSuppressedRefusalLogs() => Interlocked.Exchange(ref suppressedRefusalLogs, 0);

    public void ForgetQuietSources(long nowMs)
    {
        refusalLogs.ForgetFull(nowMs);
        lobbyAddressMismatchLogs.ForgetFull(nowMs);
        liveElsewhereLogs.ForgetFull(nowMs);
        takeoverLogs.ForgetFull(nowMs);
    }

    public IReadOnlyList<GVoiceSend> TakePendingSends()
    {
        lock (gate)
        {
            GVoiceSend[] sends = [.. pendingSends];
            pendingSends.Clear();
            return sends;
        }
    }

    public GVoiceJoinOutcome Join(string openId, string roomName, uint clientNetType, IPEndPoint source, long nowMs)
    {
        if (!ulong.TryParse(openId, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) || uid == 0
            || !GVoiceRoomName.TryParse(roomName, out var name))
        {
            LogUnparsedJoin(openId, roomName, source, nowMs);
            return GVoiceJoinOutcome.Refused(JoinRefusedNotRoomMember, "not a member of this room");
        }

        var canonicalName = name.ToString();
        var sourceAddress = SourceAddressOf(source);
        lock (gate)
        {
            if (!battleVoice.IsGvoiceRoomEntitled(uid, name))
            {
                LogNotEntitled(uid, canonicalName, source, nowMs);
                return GVoiceJoinOutcome.Refused(JoinRefusedNotRoomMember, "not a member of this room");
            }

            roomsByName.TryGetValue(canonicalName, out var room);
            var lobbyAddress = lobbyRoles.RemoteAddress(uid);
            var fromLobbyAddress = sourceAddress.Equals(lobbyAddress);
            if (room?.FindByUid(uid) is { } existing && !existing.JoinAddress.Equals(sourceAddress))
            {
                var isLive = existing.AccessEndPoint is not null && nowMs - existing.LastHeardMs < LiveEndpointQuietMs;
                if (isLive && !fromLobbyAddress)
                {
                    LogLiveElsewhere(uid, canonicalName, existing, source, nowMs);
                    return GVoiceJoinOutcome.Unanswered(JoinUnansweredLiveElsewhere, "member is live at another address");
                }

                if (isLive)
                {
                    LogEvictedElsewhere(uid, canonicalName, existing, source, nowMs);
                }

                Rebind(existing, sourceAddress);
            }

            if (!fromLobbyAddress)
            {
                LogLobbyAddressMismatch(uid, source, lobbyAddress, nowMs);
            }

            var pushes = new List<GVoiceSend>();
            foreach (var other in roomsByName.Values.ToArray())
            {
                if (other.Name != canonicalName && other.FindByUid(uid) is { } stale)
                {
                    logger.LogInformation("Voice member {MemberId} (uid {Uid}) left room {RoomName} to join {NewRoom}", stale.MemberId, uid, other.Name, canonicalName);
                    pushes.AddRange(RemoveLocked(other, stale, MemberActionQuit));
                }
            }

            if (room is null)
            {
                if (roomsByName.Count >= MaxRooms)
                {
                    logger.LogWarning("Refused voice join of uid {Uid} to room {RoomName}: {MaxRooms} voice rooms are open", uid, canonicalName, MaxRooms);
                    return new GVoiceJoinOutcome(JoinRefusedRoomFull, "too many voice rooms", null, null, pushes);
                }

                room = new GVoiceRoom(canonicalName, NewRoomId(), RandomNonZeroUInt64());
                roomsByName[canonicalName] = room;
                roomsById[room.RoomId] = room;
                logger.LogInformation("Opened voice room {RoomName} as room_id {RoomId}", canonicalName, room.RoomId);
            }

            var member = room.FindByUid(uid);
            if (member is null)
            {
                if (room.Members.Count >= MaxMembersPerRoom)
                {
                    logger.LogWarning("Refused voice join of uid {Uid} to room {RoomName}: {MaxMembers} members already", uid, canonicalName, MaxMembersPerRoom);
                    return new GVoiceJoinOutcome(JoinRefusedRoomFull, "voice room is full", null, null, pushes);
                }

                member = room.Add(uid, openId, clientNetType, sourceAddress, nowMs);
                logger.LogInformation("Voice join: uid {Uid} is member {MemberId} of room {RoomName}", uid, member.MemberId, canonicalName);
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
                || member.OpenId != signal.Head.OpenId
                || !MaySignalFrom(member, source))
            {
                if (ShouldLogRefusal(source, nowMs))
                {
                    logger.LogWarning("Refused voice signal {Cmd} from {Source}: room_id, member_id and open_id do not name a member joined from that address", signal.Cmd, source);
                }

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
                .Where(member => head.Audience.Length == 0 || head.Audience.Contains(member.MemberId))
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

    private void Revoke(ulong uid, GVoiceRoomName name)
    {
        lock (gate)
        {
            if (!roomsByName.TryGetValue(name.ToString(), out var room) || room.FindByUid(uid) is not { } member)
            {
                return;
            }

            logger.LogInformation("Voice member {MemberId} (uid {Uid}) of room {RoomName} lost its lobby entitlement and was removed", member.MemberId, uid, room.Name);
            pendingSends.AddRange(RemoveLocked(room, member, MemberActionQuit));
        }
    }

    private void LogUnparsedJoin(string openId, string roomName, IPEndPoint source, long nowMs)
    {
        if (ShouldLogRefusal(source, nowMs))
        {
            logger.LogWarning("Refused voice join from {Source}: openid ({OpenIdLength} chars) is not a uid or room name ({RoomNameLength} chars) is not a voice room", source, openId.Length, roomName.Length);
        }
    }

    private void LogNotEntitled(ulong uid, string canonicalName, IPEndPoint source, long nowMs)
    {
        if (ShouldLogRefusal(source, nowMs))
        {
            logger.LogWarning("Refused voice join of uid {Uid} to room {RoomName} from {Source}: not entitled to that lobby room or desk camp voice room", uid, canonicalName, source);
        }
    }

    private void LogLiveElsewhere(ulong uid, string canonicalName, GVoiceMember live, IPEndPoint source, long nowMs)
    {
        if (liveElsewhereLogs.TryTake(uid, nowMs))
        {
            logger.LogWarning("Left a voice join of uid {Uid} to room {RoomName} from {Source} unanswered: member {MemberId} is live from {LiveAddress}, heard {QuietMs} ms ago, and the join is not from the lobby address", uid, canonicalName, source, live.MemberId, live.JoinAddress, nowMs - live.LastHeardMs);
        }
    }

    private void LogEvictedElsewhere(ulong uid, string canonicalName, GVoiceMember live, IPEndPoint source, long nowMs)
    {
        if (takeoverLogs.TryTake(uid, nowMs))
        {
            logger.LogWarning("Voice join of uid {Uid} to room {RoomName} from its lobby address {Source} took member {MemberId} over from {LiveAddress}, heard {QuietMs} ms ago", uid, canonicalName, source, live.MemberId, live.JoinAddress, nowMs - live.LastHeardMs);
        }
    }

    private void LogLobbyAddressMismatch(ulong uid, IPEndPoint source, IPAddress? lobbyAddress, long nowMs)
    {
        if (lobbyAddress is not null && lobbyAddressMismatchLogs.TryTake(uid, nowMs))
        {
            logger.LogInformation("Voice join of uid {Uid} comes from {Source} while its lobby connection is from {LobbyAddress}; admitted", uid, source, lobbyAddress);
        }
    }

    private bool MaySignalFrom(GVoiceMember member, IPEndPoint source)
    {
        var sourceAddress = SourceAddressOf(source);
        return member.JoinAddress.Equals(sourceAddress) || sourceAddress.Equals(lobbyRoles.RemoteAddress(member.Uid));
    }

    private static void Rebind(GVoiceMember member, IPAddress joinAddress)
    {
        member.JoinAddress = joinAddress;
        member.AccessEndPoint = null;
    }

    private List<GVoiceSend> CheckIn(GVoiceRoom room, GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        if (!signal.TryReadBody(room.RoomKey, VoipCheckInReq.Parser, out var request))
        {
            logger.LogWarning("Refused voice check-in of member {MemberId} in room {RoomName}: body does not decrypt with the room key", member.MemberId, room.Name);
            return [];
        }

        Heard(room, member, signal, source, nowMs);
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

        Heard(room, member, signal, source, nowMs);
        return [Signal(room, member, VOIP_CMD.VOIP_CMD_STAT_RSP, new VoipStatRsp { UpRate = member.TakeUpLossRate(LossRateScale) })];
    }

    private List<GVoiceSend> OpenVoice(GVoiceRoom room, GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        if (!signal.TryReadBody(room.RoomKey, OpenVoiceReq.Parser, out var request))
        {
            logger.LogWarning("Refused voice open of member {MemberId} in room {RoomName}: body does not decrypt with the room key", member.MemberId, room.Name);
            return [];
        }

        Heard(room, member, signal, source, nowMs);
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

        Heard(room, member, signal, source, nowMs);
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
            if (ShouldLogRefusal(source, nowMs))
            {
                logger.LogWarning("Refused voice member report from {Source}: it does not name a ready member at that address", source);
            }

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

    private void Heard(GVoiceRoom room, GVoiceMember member, CDNVProtoSignal signal, IPEndPoint source, long nowMs)
    {
        var sourceAddress = SourceAddressOf(source);
        if (!member.JoinAddress.Equals(sourceAddress))
        {
            logger.LogInformation("Voice member {MemberId} (uid {Uid}) of room {RoomName} moved from {OldAddress} to its lobby address {Source}", member.MemberId, member.Uid, room.Name, member.JoinAddress, source);
            member.JoinAddress = sourceAddress;
        }

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
