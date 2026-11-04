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

public sealed class RoleRepository(DataOptions options)
{
    private const int OneRow = 1;

    public async Task<LMNRoleInfo?> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var role = await database.GetTable<Role>().FirstOrDefaultAsync(role => role.GameUlongUID == gameUlongUID);
        if (role is null)
        {
            return null;
        }

        var pokemon = await database.GetTable<RolePokemon>()
            .Where(pokemon => pokemon.GameUlongUID == gameUlongUID)
            .ToArrayAsync();
        var items = await database.GetTable<RoleItem>()
            .Where(item => item.GameUlongUID == gameUlongUID && !BagRepository.BagItemTypes.Contains(item.ItemType))
            .ToArrayAsync();
        return ToLMNRoleInfo(role, pokemon.Select(ToRolePokemonInfo), items.Select(ToRoleItemInfo));
    }

    public async Task SaveAsync(LMNRoleInfo role)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (await database.UpdateAsync(ToEntity(role)) != OneRow)
        {
            throw new InvalidOperationException("Cannot save a role that does not exist");
        }

        await SavePokemonAsync(database, role);
        await SaveItemsAsync(database, role);
        await transaction.CommitAsync();
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> RegisterAsync(ulong gameUlongUID, string userName)
    {
        await using var database = new DataConnection(options);
        if (await database.GetTable<Role>().AnyAsync(role => role.GameUlongUID == gameUlongUID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrRegisterNotNeed;
        }

        try
        {
            await database.InsertAsync(new Role
            {
                GameUlongUID = gameUlongUID,
                InGameUsrName = userName,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return await database.GetTable<Role>().AnyAsync(role => role.GameUlongUID == gameUlongUID)
                ? PB_ENUM_RES_ERROR_CODE.PbErrRegisterNotNeed
                : PB_ENUM_RES_ERROR_CODE.PbErrRegisterNameDup;
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<long> FindCreateTimeAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.ExecuteAsync<long>(
            "SELECT UNIX_TIMESTAMP(created_at) FROM role WHERE game_ulong_uid = @gameUlongUID",
            new DataParameter("gameUlongUID", gameUlongUID, DataType.UInt64));
    }

    private static async Task SavePokemonAsync(DataConnection database, LMNRoleInfo role)
    {
        var savedPokemon = (await database.GetTable<RolePokemon>()
                .Where(pokemon => pokemon.GameUlongUID == role.GameUlongUID)
                .ToArrayAsync())
            .Select(ToRolePokemonInfo)
            .ToDictionary(pokemon => pokemon.PokemonId);
        foreach (var pokemon in role.GetOwnedPokemon())
        {
            if (!savedPokemon.TryGetValue(pokemon.PokemonId, out var saved))
            {
                await database.InsertAsync(ToEntity(role.GameUlongUID, pokemon));
            }
            else if (saved != pokemon)
            {
                await database.UpdateAsync(ToEntity(role.GameUlongUID, pokemon));
            }
        }
    }

    private static RolePokemonInfo ToRolePokemonInfo(RolePokemon pokemon) =>
        new(pokemon.PokemonId, pokemon.GainTime, pokemon.GainChannel, pokemon.NewTag);

    internal static RolePokemon ToEntity(ulong gameUlongUID, RolePokemonInfo pokemon) => new()
    {
        GameUlongUID = gameUlongUID,
        PokemonId = pokemon.PokemonId,
        GainTime = pokemon.GainTime,
        GainChannel = pokemon.GainChannel,
        NewTag = pokemon.NewTag,
    };

    private static async Task SaveItemsAsync(DataConnection database, LMNRoleInfo role)
    {
        var savedItems = (await database.GetTable<RoleItem>()
                .Where(item => item.GameUlongUID == role.GameUlongUID && !BagRepository.BagItemTypes.Contains(item.ItemType))
                .ToArrayAsync())
            .Select(ToRoleItemInfo)
            .ToDictionary(item => (item.ItemType, item.ItemId));
        foreach (var item in role.GetOwnedItems())
        {
            if (!savedItems.TryGetValue((item.ItemType, item.ItemId), out var saved))
            {
                await database.InsertAsync(ToEntity(role.GameUlongUID, item));
            }
            else if (saved != item)
            {
                await database.UpdateAsync(ToEntity(role.GameUlongUID, item));
            }
        }
    }

    private static LMNRoleInfo ToLMNRoleInfo(Role role, IEnumerable<RolePokemonInfo> pokemon, IEnumerable<RoleItemInfo> items) => new(
        role.GameUlongUID,
        role.InGameUsrName,
        role.Gender,
        pbCOMDT_ROLE_AVATAR_INFO.Parser.ParseFrom(role.RoleAvatarInfo),
        pbCOMDT_NEWBIE_STATE_INFO.Parser.ParseFrom(role.NewbieState).BitsDetail,
        pokemon,
        items,
        role.FollowPokemonId,
        role.UsingLobbyBgId,
        role.RoleTotalExp,
        role.RoleHistoryMaxTotalExp,
        role.FirstPokemonId);

    private static RoleItemInfo ToRoleItemInfo(RoleItem item) => new(
        Enum.IsDefined((RoleItemType)item.ItemType)
            ? (RoleItemType)item.ItemType
            : throw new InvalidDataException($"role_item has unknown item_type {item.ItemType}"),
        item.ItemId,
        item.GainTime,
        item.TimeLimited,
        item.NewTag);

    internal static RoleItem ToEntity(ulong gameUlongUID, RoleItemInfo item) => new()
    {
        GameUlongUID = gameUlongUID,
        ItemType = (int)item.ItemType,
        ItemId = item.ItemId,
        GainTime = item.GainTime,
        TimeLimited = item.TimeLimited,
        NewTag = item.NewTag,
    };

    private static Role ToEntity(LMNRoleInfo role) => new()
    {
        GameUlongUID = role.GameUlongUID,
        InGameUsrName = role.InGameUsrName,
        Gender = role.Gender,
        RoleTotalExp = role.RoleTotalExp,
        RoleHistoryMaxTotalExp = role.RoleHistoryMaxTotalExp,
        RoleAvatarInfo = role.RoleAvatarInfo.ToByteArray(),
        NewbieState = role.BuildNewbieState().ToByteArray(),
        FollowPokemonId = role.FollowPokemonId,
        UsingLobbyBgId = role.UsingLobbyBgId,
        FirstPokemonId = role.FirstPokemonId,
    };
}
