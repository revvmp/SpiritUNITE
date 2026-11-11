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
    private static readonly AsyncLocal<SettlementOwnership?> Current = new();
    private readonly MySqlConnection connection;
    private readonly ulong uid;
    private readonly long fence;

    private SettlementOwnership(MySqlConnection connection, ulong uid, long fence)
    {
        this.connection = connection;
        this.uid = uid;
        this.fence = fence;
    }

    public static async Task<SettlementOwnership?> TryAcquireAsync(DataOptions options, ulong uid)
    {
        var connectionString = options.ConnectionOptions.ConnectionString
            ?? throw new InvalidOperationException("Settlement ownership requires a configured database connection string");
        var settings = new MySqlConnectionStringBuilder(connectionString) { Pooling = false };
        var connection = new MySqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync();
            var name = "settlement:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{settings.Database}:{uid}")))[..48];
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT GET_LOCK(@name,0)";
            command.Parameters.AddWithValue("@name", name);
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1)
            {
                await connection.DisposeAsync();
                return null;
            }

            await using var database = new DataConnection(options);
            await using var transaction = await database.BeginTransactionAsync();
            await SettlementEffects.LockRoleAsync(database, uid);
            try
            {
                await database.InsertAsync(new SettlementOwnerRecord { GameUlongUID = uid });
            }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
            }

            await database.GetTable<SettlementOwnerRecord>().Where(row => row.GameUlongUID == uid)
                .Set(row => row.Fence, row => row.Fence + 1).UpdateAsync();
            var fence = await database.GetTable<SettlementOwnerRecord>().Where(row => row.GameUlongUID == uid)
                .Select(row => row.Fence).SingleAsync();
            await transaction.CommitAsync();
            return new SettlementOwnership(connection, uid, fence);
        }
        catch
        {
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

        if (owner.uid != uid)
        {
            throw new InvalidOperationException("Settlement execution attempted to mutate another owner");
        }

        var fence = await database.GetTable<SettlementOwnerRecord>().Where(row => row.GameUlongUID == uid)
            .AsMySql().ForUpdateHint().Select(row => row.Fence).SingleAsync();
        if (fence != owner.fence || owner.connection.State != System.Data.ConnectionState.Open)
        {
            throw new SettlementClaimLostException(new SettlementKey(string.Empty, uid));
        }
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();

    private sealed class Scope(SettlementOwnership? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
