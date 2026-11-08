using System.Collections.Concurrent;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Online;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Room;

public sealed class RoomMicTips
{
    private readonly RoomMicTipsRepository repository;
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<ulong, int> showMicTipsCnts = new();

    public RoomMicTips(RoomMicTipsRepository repository, LobbyRoles lobbyRoles, TimeProvider clock)
    {
        this.repository = repository;
        this.clock = clock;
        lobbyRoles.RoleOnline += LoadAsync;
        lobbyRoles.RoleOffline += uid =>
        {
            showMicTipsCnts.TryRemove(uid, out _);
            return Task.CompletedTask;
        };
    }

    public int ShowMicTipsCnt(ulong uid) => showMicTipsCnts.GetValueOrDefault(uid);

    public pbSCRoomMicTipsInfoNtf BuildInfoNtf(ulong uid) => WithShowMicTipsCnt(uid, new pbSCRoomMicTipsInfoNtf());

    public pbSCRoomMicTipsInfoNtf WithShowMicTipsCnt(ulong uid, pbSCRoomMicTipsInfoNtf ntf)
    {
        ntf.ShowMicTipsCnt = ShowMicTipsCnt(uid);
        return ntf;
    }

    public async Task RecordShownAsync(ulong uid) =>
        showMicTipsCnts[uid] = await repository.AddShowMicTipsAsync(uid, clock.GetUtcNow().ToUnixTimeSeconds());

    private async Task LoadAsync(ulong uid) => showMicTipsCnts[uid] = await repository.FindShowMicTipsCntAsync(uid);
}
