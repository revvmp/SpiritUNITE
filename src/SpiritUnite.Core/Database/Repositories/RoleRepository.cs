using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record RoleName(ulong GameUlongUID, string InGameUsrName);

public sealed class RoleRepository(DataOptions options, TimeProvider clock)
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
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var timeLimitedPokemon = await database.GetTable<RolePokemonTimeLimited>()
            .Where(pokemon => pokemon.GameUlongUID == gameUlongUID && pokemon.TimeLimited > now)
            .ToArrayAsync();
        var items = await database.GetTable<RoleItem>()
            .Where(item => item.GameUlongUID == gameUlongUID && !BagRepository.BagItemTypes.Contains(item.ItemType))
            .ToArrayAsync();
        var pveChallengeStars = await database.GetTable<RolePveChallengeNode>()
            .Where(node => node.GameUlongUID == gameUlongUID)
            .ToArrayAsync();
        var heavenTrainerRecords = await database.GetTable<RoleChallengeTrainerRecord>()
            .Where(record => record.GameUlongUID == gameUlongUID)
            .ToArrayAsync();
        return ToLMNRoleInfo(
            role,
            pokemon.Select(ToRolePokemonInfo),
            items.Select(ToRoleItemInfo),
            pveChallengeStars.Select(node => new RolePveChallengeStar(node.StageID, node.NodeID, node.DifficultyLevel, node.StarNum)),
            heavenTrainerRecords.Select(record => new ChallengeTrainerRecord(record.MapId, record.LevelType, record.ChallengeCnt)),
            timeLimitedPokemon.Select(ToRolePokemonInfo));
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
        await SaveTimeLimitedPokemonNewTagsAsync(database, role);
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

    public async Task<RoleName[]> SearchNamesAsync(string text, int limit)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<Role>()
            .Where(role => role.InGameUsrName.Contains(text))
            .OrderBy(role => role.GameUlongUID)
            .Take(limit)
            .Select(role => new RoleName(role.GameUlongUID, role.InGameUsrName))
            .ToArrayAsync();
    }

    public async Task<RoleName[]> FindNamesAsync(IReadOnlyCollection<ulong> gameUlongUIDs)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<Role>()
            .Where(role => gameUlongUIDs.Contains(role.GameUlongUID))
            .Select(role => new RoleName(role.GameUlongUID, role.InGameUsrName))
            .ToArrayAsync();
    }

    public async Task<bool> RenameAsync(ulong gameUlongUID, string inGameUsrName)
    {
        await using var database = new DataConnection(options);
        try
        {
            return await database.GetTable<Role>()
                .Where(role => role.GameUlongUID == gameUlongUID)
                .Set(role => role.InGameUsrName, inGameUsrName)
                .UpdateAsync() == 1;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task<ulong[]> ListUidsAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<Role>().Select(role => role.GameUlongUID).ToArrayAsync();
    }

    public async Task<int> CountAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<Role>().CountAsync();
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

    private static async Task SaveTimeLimitedPokemonNewTagsAsync(DataConnection database, LMNRoleInfo role)
    {
        foreach (var pokemon in role.GetTimeLimitedPokemon())
        {
            await database.GetTable<RolePokemonTimeLimited>()
                .Where(saved => saved.GameUlongUID == role.GameUlongUID && saved.PokemonId == pokemon.PokemonId && saved.NewTag != pokemon.NewTag)
                .Set(saved => saved.NewTag, pokemon.NewTag)
                .UpdateAsync();
        }
    }

    private static RolePokemonInfo ToRolePokemonInfo(RolePokemonTimeLimited pokemon) =>
        new(pokemon.PokemonId, pokemon.GainTime, pokemon.GainChannel, pokemon.NewTag, pokemon.TimeLimited);

    internal static RolePokemonTimeLimited ToTimeLimitedEntity(ulong gameUlongUID, RolePokemonInfo pokemon) => new()
    {
        GameUlongUID = gameUlongUID,
        PokemonId = pokemon.PokemonId,
        GainTime = pokemon.GainTime,
        GainChannel = pokemon.GainChannel,
        NewTag = pokemon.NewTag,
        TimeLimited = pokemon.TimeLimited,
    };

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

    private static LMNRoleInfo ToLMNRoleInfo(
        Role role,
        IEnumerable<RolePokemonInfo> pokemon,
        IEnumerable<RoleItemInfo> items,
        IEnumerable<RolePveChallengeStar> pveChallengeStars,
        IEnumerable<ChallengeTrainerRecord> heavenTrainerRecords,
        IEnumerable<RolePokemonInfo> timeLimitedPokemon) => new(
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
        role.FirstPokemonId,
        pveChallengeStars,
        heavenTrainerRecords,
        timeLimitedPokemon);

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
