using SpiritUNITE.Battle;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Room;

namespace SpiritUNITE.Game.Admin;

public sealed record AdminTrainer(ulong Uid, string Name);

public sealed record AdminRoomRow(int RoomShowId, string RoomType, string Map, string State, IReadOnlyList<AdminTrainer> Members, int Bots, long MatchStartedAt);

public sealed record AdminBattleRow(int DeskID, string Map, string Phase, IReadOnlyList<AdminTrainer> Players, int Bots);

public sealed record AdminServerSnapshot(IReadOnlyList<AdminRoomRow> Rooms, IReadOnlyList<AdminBattleRow> Battles);

public sealed class AdminServerState(RoomManager rooms, DeskManager desks, RoleRepository roles, AdminCatalog catalog)
{
    private const string RoomTypePrefix = "PbComRoomType";
    private const string RoomStatePrefix = "PbComRoomsvrRoomState";

    public async Task<AdminServerSnapshot> SnapshotAsync()
    {
        var roomSnapshots = rooms.SnapshotRooms();
        var deskSnapshots = desks.SnapshotDesks();
        var names = await AdminNames.FindAsync(
            roles,
            [.. roomSnapshots.SelectMany(room => room.MemberUids).Concat(deskSnapshots.SelectMany(desk => desk.HumanUids))]);
        return new AdminServerSnapshot(
            [
                .. roomSnapshots
                    .OrderBy(room => room.RoomShowId)
                    .Select(room => new AdminRoomRow(
                        room.RoomShowId,
                        AdminNames.FromEnum(room.RoomType, RoomTypePrefix),
                        MapName(room.MapId),
                        AdminNames.FromEnum(room.State, RoomStatePrefix),
                        [.. room.MemberUids.Select(names.Trainer)],
                        room.BotCount,
                        room.MatchStartedAt)),
            ],
            [
                .. deskSnapshots
                    .OrderBy(desk => desk.DeskID)
                    .Select(desk => new AdminBattleRow(
                        desk.DeskID,
                        MapName(desk.MapId),
                        desk.Phase switch
                        {
                            DeskPhase.Picking => "Picking",
                            DeskPhase.WaitingRelay => "Loading",
                            _ => "In battle",
                        },
                        [.. desk.HumanUids.Select(names.Trainer)],
                        desk.NpcCount)),
            ]);
    }

    private string MapName(int mapId) =>
        catalog.Data.Maps.FirstOrDefault(map => map.Id == mapId)?.Name ?? (mapId == 0 ? string.Empty : $"Map {mapId}");
}
