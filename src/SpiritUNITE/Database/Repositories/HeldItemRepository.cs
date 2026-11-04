using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed class HeldItemRepository(DataOptions options)
{
    public async Task<IReadOnlySet<int>> LoadClearedSlotRedDotsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var slots = await database.GetTable<RoleHeldItemSlot>()
            .Where(slot => slot.GameUlongUID == gameUlongUID)
            .Select(slot => slot.HeldItemSlotIndex)
            .ToArrayAsync();
        return slots.ToHashSet();
    }

    public async Task ClearSlotRedDotAsync(ulong gameUlongUID, int heldItemSlotIndex)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(new RoleHeldItemSlot
            {
                GameUlongUID = gameUlongUID,
                HeldItemSlotIndex = heldItemSlotIndex,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
        }
    }
}
