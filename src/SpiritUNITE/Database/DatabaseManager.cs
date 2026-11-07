using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using MySqlConnector;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database;

public static class DatabaseManager
{
    public static async Task<DataOptions> ConnectAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Database:ConnectionString is missing; copy appsettings.example.json to appsettings.json");
        }

        var options = new DataOptions().UseMySql(connectionString, MySqlVersion.MariaDB10, MySqlProvider.MySqlConnector);
        await EnsureReadyAsync(options);
        return options;
    }

    private static async Task EnsureReadyAsync(DataOptions options)
    {
        try
        {
            await using var database = new DataConnection(options);
            await database.GetTable<Role>().FirstOrDefaultAsync();
            await database.GetTable<RolePokemon>().AnyAsync();
            await database.GetTable<RolePokemonTimeLimited>().AnyAsync();
            await database.GetTable<RoleEeveeChallenge>().AnyAsync();
            await database.GetTable<RoleItem>().FirstOrDefaultAsync();
            await database.GetTable<RoleCoin>().FirstOrDefaultAsync();
            await database.GetTable<RoleShopBuy>().FirstOrDefaultAsync();
            await database.GetTable<Account>().FirstOrDefaultAsync();
            await database.GetTable<AccountToken>().AnyAsync();
            await database.GetTable<RoleLoginDay>().AnyAsync();
            await database.GetTable<RoleActivityTaskReward>().AnyAsync();
            await database.GetTable<RoleSocial>().AnyAsync();
            await database.GetTable<RoleFriend>().AnyAsync();
            await database.GetTable<RoleFriendRequest>().AnyAsync();
            await database.GetTable<RoleFriendBlack>().AnyAsync();
            await database.GetTable<RoleChatOffline>().AnyAsync();
            await database.GetTable<RoleMail>().FirstOrDefaultAsync();
            await database.GetTable<RoleProfileStyle>().AnyAsync();
            await database.GetTable<RoleCardFaceWear>().AnyAsync();
            await database.GetTable<RoleSetting>().FirstOrDefaultAsync();
            await database.GetTable<RolePokemonUsed>().FirstOrDefaultAsync();
            await database.GetTable<RolePveChallengeNode>().AnyAsync();
            await database.GetTable<RoleChallengeTrainerRecord>().FirstOrDefaultAsync();
            await database.GetTable<RoleNewbieChallenge>().AnyAsync();
            await database.GetTable<RoleNewbieChallengeTask>().AnyAsync();
            await database.GetTable<RoleTaskProcess>().AnyAsync();
            await database.GetTable<RoleFightHistory>().AnyAsync();
            await database.GetTable<RoleEnergy>().AnyAsync();
            await database.GetTable<RoleRanked>().FirstOrDefaultAsync();
            await database.GetTable<RoleRankedSeason>().FirstOrDefaultAsync();
            await database.GetTable<RoleRankedSeasonTaskAward>().FirstOrDefaultAsync();
            await database.GetTable<RoleWarmBattle>().AnyAsync();
        }
        catch (MySqlException exception)
        {
            throw new InvalidOperationException(
                $"Database is not ready: {exception.Message} Check appsettings.json and run sql/spiritunite.sql from the repository root.",
                exception);
        }
    }
}
