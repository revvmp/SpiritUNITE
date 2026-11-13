using System.Data;
using System.Security.Cryptography;
using System.Text;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using MySqlConnector;
using SpiritUnite.Database.Models;

namespace SpiritUnite.Database.Repositories;

public sealed class SettlementOwnership : IAsyncDisposable
{
    private const string OwnerPoolName = "spiritunite-settlement-owner";

    private const string AdvanceFenceStatements =
        "SELECT game_ulong_uid FROM role WHERE game_ulong_uid = @uid FOR UPDATE;"
        + "INSERT INTO battle_settlement_owner (game_ulong_uid, fence) VALUES (@uid, 1) ON DUPLICATE KEY UPDATE fence = fence + 1;"
        + "SELECT fence FROM battle_settlement_owner WHERE game_ulong_uid = @uid;";

    private static readonly AsyncLocal<SettlementOwnership?> Current = new();
    private readonly MySqlConnection connection;
    private readonly string lockName;
    private readonly ulong uid;
    private readonly long fence;

    private SettlementOwnership(MySqlConnection connection, string lockName, ulong uid, long fence)
    {
        this.connection = connection;
        this.lockName = lockName;
        this.uid = uid;
        this.fence = fence;
    }

    public static bool IsOwned => Current.Value is not null;

    public static async Task<(SettlementOwnership? Ownership, T? Claimed)> TryAcquireAsync<T>(
        DataOptions options,
        ulong uid,
        Func<DataConnection, Task<T>> claimUnderFence)
    {
        var connectionString = options.ConnectionOptions.ConnectionString
            ?? throw new InvalidOperationException("Settlement ownership requires a configured database connection string");
        var settings = new MySqlConnectionStringBuilder(connectionString) { ApplicationName = OwnerPoolName, ConnectionReset = true };
        var lockName = "settlement:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{settings.Database}:{uid}")))[..48];
        var connection = new MySqlConnection(settings.ConnectionString);
        var locked = false;
        try
        {
            await connection.OpenAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT GET_LOCK(@name,0)";
                command.Parameters.AddWithValue("@name", lockName);
                locked = Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
            }

            if (!locked)
            {
                await connection.DisposeAsync();
                return (null, default);
            }

            await using var database = new DataConnection(options);
            await using var transaction = await database.BeginTransactionAsync();
            var fence = await AdvanceFenceAsync(database, uid);
            var claimed = await claimUnderFence(database);
            await transaction.CommitAsync();
            return (new SettlementOwnership(connection, lockName, uid, fence), claimed);
        }
        catch
        {
            if (locked)
            {
                await ReleaseAsync(connection, lockName);
            }

            await connection.DisposeAsync();
            throw;
        }
    }

    public IDisposable Enter()
    {
        var previous = Current.Value;
        Current.Value = this;
        return new Scope(previous);
    }

    public static async Task VerifyAsync(DataConnection database, ulong uid)
    {
        if (Current.Value is not { } owner)
        {
            return;
        }

        RequireOwnerOf(owner, uid);
        var fence = await database.GetTable<SettlementOwnerRecord>().Where(row => row.GameUlongUID == uid)
            .AsMySql().ForUpdateHint().Select(row => row.Fence).SingleAsync();
        Verify(uid, fence);
    }

    public static void Verify(ulong uid, long? storedFence)
    {
        if (Current.Value is not { } owner)
        {
            return;
        }

        RequireOwnerOf(owner, uid);
        if (storedFence is not { } fence)
        {
            throw new InvalidOperationException("Settlement owner row is missing");
        }

        if (fence != owner.fence || owner.connection.State != ConnectionState.Open)
        {
            throw new SettlementClaimLostException(new SettlementKey(string.Empty, uid));
        }
    }

    private static void RequireOwnerOf(SettlementOwnership owner, ulong uid)
    {
        if (owner.uid != uid)
        {
            throw new InvalidOperationException("Settlement execution attempted to mutate another owner");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync(connection, lockName);
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception)
        {
            MySqlConnection.ClearPool(connection);
        }
    }

    private static async Task<long> AdvanceFenceAsync(DataConnection database, ulong uid)
    {
        var connection = (MySqlConnection)await database.OpenDbConnectionAsync(CancellationToken.None);
        await using var command = new MySqlCommand(AdvanceFenceStatements, connection, (MySqlTransaction)database.Transaction!);
        command.Parameters.AddWithValue("@uid", uid);
        await using var reader = await command.ExecuteReaderAsync();
        var roles = 0;
        while (await reader.ReadAsync())
        {
            roles++;
        }

        if (roles != 1)
        {
            throw new InvalidDataException("Settlement role no longer exists");
        }

        if (!await reader.NextResultAsync() || !await reader.ReadAsync())
        {
            throw new InvalidOperationException("Settlement owner row is missing");
        }

        return reader.GetInt64(0);
    }

    private static async Task ReleaseAsync(MySqlConnection connection, string lockName)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DO RELEASE_LOCK(@name)";
            command.Parameters.AddWithValue("@name", lockName);
            await command.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            MySqlConnection.ClearPool(connection);
        }
    }

    private sealed class Scope(SettlementOwnership? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
