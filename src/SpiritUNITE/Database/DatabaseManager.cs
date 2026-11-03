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
            await database.GetTable<Role>().AnyAsync();
        }
        catch (MySqlException exception)
        {
            throw new InvalidOperationException(
                $"Database is not ready: {exception.Message} Check appsettings.json and run Database/SpiritUNITE.sql.",
                exception);
        }
    }
}
