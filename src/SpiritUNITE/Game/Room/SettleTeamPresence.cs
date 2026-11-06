using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed record SettleTeamMember(ulong Uid, int PokemonHeroId, bool IsNpc);

public sealed class SettleTeamPresence(
    RoomManager rooms,
    LobbyRoles lobbyRoles,
    IOptions<RoomOptions> roomOptions,
    TimeProvider clock,
    ILogger<SettleTeamPresence> logger)
{
    public const int Online = 0;
    public const int Offline = 1;
    public const int Ready = 2;

    private readonly RoomOptions options = roomOptions.Value;
    private readonly Lock gate = new();
    private readonly Dictionary<ulong, SettleTeamView> viewsByUid = [];

    public static pbSCRoomSettleOneKeyTeamInfoNtf BuildInfoNtf(IEnumerable<SettleTeamMember> members, IReadOnlySet<ulong> offlineUids) =>
        BuildInfoNtf(members, uid => offlineUids.Contains(uid) ? Offline : Online);

    public void Follow(ulong uid, int settleRoomID, IReadOnlyList<SettleTeamMember> members)
    {
        var view = new SettleTeamView(settleRoomID, members);
        lock (gate)
        {
            viewsByUid[uid] = view;
        }

        var offlineAt = members
            .Where(member => member.IsNpc)
            .Select(member => (member.Uid, Delay: PickOfflineDelay()))
            .OrderBy(entry => entry.Delay)
            .ToArray();
        if (offlineAt.Length > 0)
        {
            _ = PushOfflineAsync(uid, view, offlineAt);
        }
    }

    public async Task MarkAsync(ulong memberUid, int status)
    {
        List<(ulong Uid, pbSCRoomSettleOneKeyTeamInfoNtf Ntf)> pushes = [];
        lock (gate)
        {
            foreach (var (uid, view) in viewsByUid.ToArray())
            {
                if (!view.Statuses.ContainsKey(memberUid))
                {
                    continue;
                }

                if (rooms.FindRoomOf(uid)?.RoomID != view.SettleRoomID)
                {
                    viewsByUid.Remove(uid);
                    continue;
                }

                view.Statuses[memberUid] = status;
                pushes.Add((uid, view.BuildInfoNtf()));
            }
        }

        foreach (var (uid, ntf) in pushes)
        {
            await lobbyRoles.PushAsync(uid, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRoomSettleOneKeyTeamInfoNtf, ntf));
        }
    }

    private static pbSCRoomSettleOneKeyTeamInfoNtf BuildInfoNtf(IEnumerable<SettleTeamMember> members, Func<ulong, int> statusOf) => new()
    {
        MemberList =
        {
            members.Select(member => new pbCSDT_ONE_KEY_TEAM_MEMBER_INFO
            {
                Uid = member.Uid,
                PokemonHeroId = member.PokemonHeroId,
                Status = statusOf(member.Uid),
            }),
        },
    };

    private TimeSpan PickOfflineDelay() =>
        TimeSpan.FromMilliseconds(Random.Shared.NextInt64(
            options.SettleTeamOfflineMinSec * TimeSpan.MillisecondsPerSecond,
            (options.SettleTeamOfflineMaxSec * TimeSpan.MillisecondsPerSecond) + 1));

    private async Task PushOfflineAsync(ulong uid, SettleTeamView view, IReadOnlyList<(ulong Uid, TimeSpan Delay)> offlineAt)
    {
        try
        {
            var waited = TimeSpan.Zero;
            foreach (var (memberUid, delay) in offlineAt)
            {
                await Task.Delay(delay - waited, clock);
                waited = delay;
                pbSCRoomSettleOneKeyTeamInfoNtf ntf;
                lock (gate)
                {
                    if (viewsByUid.GetValueOrDefault(uid) != view || rooms.FindRoomOf(uid)?.RoomID != view.SettleRoomID)
                    {
                        return;
                    }

                    view.Statuses[memberUid] = Offline;
                    ntf = view.BuildInfoNtf();
                }

                await lobbyRoles.PushAsync(uid, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRoomSettleOneKeyTeamInfoNtf, ntf));
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Team presence pushes for uid {Uid} in room {RoomID} failed", uid, view.SettleRoomID);
        }
    }

    private sealed class SettleTeamView(int settleRoomID, IReadOnlyList<SettleTeamMember> members)
    {
        public int SettleRoomID { get; } = settleRoomID;
        public Dictionary<ulong, int> Statuses { get; } = members.ToDictionary(member => member.Uid, _ => Online);

        public pbSCRoomSettleOneKeyTeamInfoNtf BuildInfoNtf() => SettleTeamPresence.BuildInfoNtf(members, uid => Statuses[uid]);
    }
}
