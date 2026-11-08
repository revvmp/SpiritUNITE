using System.Net;
using Google.Protobuf;

namespace SpiritUnite.Game.BattleVoice;

public sealed class GVoiceMember(ulong uid, string openId, ushort memberId, uint clientNetType, long joinedAtMs)
{
    public ulong Uid { get; } = uid;
    public string OpenId { get; } = openId;
    public ushort MemberId { get; } = memberId;
    public uint ClientNetType { get; set; } = clientNetType;
    public IPEndPoint? AccessEndPoint { get; set; }
    public bool IsReady { get; set; }
    public bool IsReceiving { get; set; } = true;
    public long LastHeardMs { get; set; } = joinedAtMs;
    public ByteString AppIdentity { get; set; } = ByteString.Empty;
    public ByteString PrivateInfo { get; set; } = ByteString.Empty;
    public uint? FirstSequenceSinceStat { get; set; }
    public uint LastSequence { get; set; }
    public uint ReceivedSinceStat { get; set; }

    public uint TakeUpLossRate(uint lossRateScale)
    {
        var received = ReceivedSinceStat;
        var expected = FirstSequenceSinceStat is { } first ? LastSequence - first + 1 : 0;
        FirstSequenceSinceStat = null;
        ReceivedSinceStat = 0;
        return expected == 0 || received >= expected ? 0 : (uint)((ulong)(expected - received) * lossRateScale / expected);
    }

    public void CountMedia(uint sequence)
    {
        FirstSequenceSinceStat ??= sequence;
        if (unchecked((int)(sequence - LastSequence)) > 0 || ReceivedSinceStat == 0)
        {
            LastSequence = sequence;
        }

        ReceivedSinceStat++;
    }
}

public sealed class GVoiceRoom(string name, ulong roomId, ulong roomKey)
{
    private const ushort FirstMemberId = 1;

    private readonly Dictionary<ushort, GVoiceMember> members = [];

    public string Name { get; } = name;
    public ulong RoomId { get; } = roomId;
    public ulong RoomKey { get; } = roomKey;
    public uint Version { get; private set; }
    public IReadOnlyCollection<GVoiceMember> Members => members.Values;
    public IEnumerable<GVoiceMember> ReadyMembers => members.Values.Where(member => member.IsReady);

    public GVoiceMember? Find(ushort memberId) => members.GetValueOrDefault(memberId);

    public GVoiceMember? FindByUid(ulong uid) => members.Values.FirstOrDefault(member => member.Uid == uid);

    public GVoiceMember Add(ulong uid, string openId, uint clientNetType, long nowMs)
    {
        var memberId = FirstMemberId;
        while (members.ContainsKey(memberId))
        {
            memberId++;
        }

        var member = new GVoiceMember(uid, openId, memberId, clientNetType, nowMs);
        members[memberId] = member;
        return member;
    }

    public void Remove(GVoiceMember member) => members.Remove(member.MemberId);

    public void Changed() => Version++;
}
