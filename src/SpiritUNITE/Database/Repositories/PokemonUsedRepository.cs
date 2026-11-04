using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed class PokemonUsedRepository(DataOptions options)
{
    public async Task<IReadOnlyList<UsedPokemonInfo>> LoadAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RolePokemonUsed>()
            .Where(used => used.GameUlongUID == gameUlongUID)
            .OrderBy(used => used.PokemonId)
            .ToArrayAsync();
        return [.. rows.Select(ToUsedPokemonInfo)];
    }

    public async Task<UsedPokemonInfo> FindAsync(ulong gameUlongUID, int pokemonId)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RolePokemonUsed>()
            .FirstOrDefaultAsync(used => used.GameUlongUID == gameUlongUID && used.PokemonId == pokemonId);
        return row is null ? UsedPokemonInfo.Unused(pokemonId) : ToUsedPokemonInfo(row);
    }

    public async Task SaveAsync(ulong gameUlongUID, UsedPokemonInfo used)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(NewRow(gameUlongUID, used));
    }

    public async Task<bool> CreateAsync(ulong gameUlongUID, int pokemonId)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(NewRow(gameUlongUID, UsedPokemonInfo.Unused(pokemonId)));
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task<UsedPokemonInfo> AddPokemonPointAsync(ulong gameUlongUID, int pokemonId, int point)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var emptyHeldItemDesign = UsedPokemonInfo.Unused(pokemonId).BuildHeldItemDesignInfo().ToByteArray();
        await database.GetTable<RolePokemonUsed>().InsertOrUpdateAsync(
            () => new RolePokemonUsed
            {
                GameUlongUID = gameUlongUID,
                PokemonId = pokemonId,
                HeldItemDesignInfo = emptyHeldItemDesign,
                PokemonTotalPoint = point,
                PokemonSeasonPoint = point,
            },
            saved => new RolePokemonUsed
            {
                PokemonTotalPoint = saved.PokemonTotalPoint + point,
                PokemonSeasonPoint = saved.PokemonSeasonPoint + point,
            });
        var row = await database.GetTable<RolePokemonUsed>()
            .FirstAsync(used => used.GameUlongUID == gameUlongUID && used.PokemonId == pokemonId);
        await transaction.CommitAsync();
        return ToUsedPokemonInfo(row);
    }

    private static UsedPokemonInfo ToUsedPokemonInfo(RolePokemonUsed row) => new(
        row.PokemonId,
        [.. pbCOMDT_HELDITEM_DESIGN_INFO.Parser.ParseFrom(row.HeldItemDesignInfo).HeldItemID],
        row.LastCarryTrainerItemID,
        row.LastSchedueId,
        row.BattleNotUseBadge,
        row.WearPasterID,
        row.PokemonTotalPoint,
        row.PokemonSeasonPoint);

    private static RolePokemonUsed NewRow(ulong gameUlongUID, UsedPokemonInfo used) => new()
    {
        GameUlongUID = gameUlongUID,
        PokemonId = used.PokemonId,
        HeldItemDesignInfo = used.BuildHeldItemDesignInfo().ToByteArray(),
        LastCarryTrainerItemID = used.LastCarryTrainerItemID,
        LastSchedueId = used.LastSchedueId,
        BattleNotUseBadge = used.BattleNotUseBadge,
        WearPasterID = used.WearPasterID,
        PokemonTotalPoint = used.PokemonTotalPoint,
        PokemonSeasonPoint = used.PokemonSeasonPoint,
    };
}
