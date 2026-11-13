using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Wallet;

namespace SpiritUnite.Database.Repositories;

public sealed record SettlementEffectResult<T>(T Value, bool Applied);

public static class SettlementEffects
{
    private const int CompressionThreshold = 1024;

    private const string LockRoleStatement = "SELECT game_ulong_uid FROM role WHERE game_ulong_uid = @uid FOR UPDATE;";

    private const string ClaimReceiptStatements =
        "INSERT INTO battle_settlement_effect (settlement_id, game_ulong_uid, effect, output) VALUES (@settlement, @uid, @effect, '') ON DUPLICATE KEY UPDATE effect = VALUES(effect);"
        + "SELECT output FROM battle_settlement_effect WHERE settlement_id = @settlement AND game_ulong_uid = @uid AND effect = @effect FOR UPDATE;";

    private const string ReadFenceStatement = "SELECT fence FROM battle_settlement_owner WHERE game_ulong_uid = @uid FOR UPDATE;";

    private static readonly JsonSerializerOptions Serialization = new()
    {
        Converters = { new ProtobufConverterFactory(), new RoleWalletConverter() },
    };

    public static byte[] Serialize<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, Serialization);
        if (json.Length < CompressionThreshold)
        {
            return json;
        }

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(json);
        }

        return compressed.ToArray();
    }

    public static T Deserialize<T>(byte[] value)
    {
        if (!IsCompressed(value))
        {
            return JsonSerializer.Deserialize<T>(value, Serialization)!;
        }

        using var gzip = new GZipStream(new MemoryStream(value), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<T>(gzip, Serialization)!;
    }

    private static bool IsCompressed(byte[] value) => value is [0x1F, 0x8B, ..];

    public static async Task<SettlementEffectResult<T>> RunAsync<T>(
        DataConnection database,
        SettlementKey key,
        string effect,
        Func<Task<T>> mutation,
        bool lockRole = true)
    {
        if (database.Transaction is null)
        {
            throw new InvalidOperationException("Settlement effects require an existing transaction");
        }

        var (output, fence) = await ClaimReceiptAsync(database, key, effect, lockRole);
        if (output.Length > 0)
        {
            return new SettlementEffectResult<T>(Deserialize<T>(output), false);
        }

        SettlementOwnership.Verify(key.GameUlongUID, fence);
        var result = await mutation();
        await Find(database, key, effect).Set(row => row.Output, Serialize(result)).UpdateAsync();
        return new SettlementEffectResult<T>(result, true);
    }

    private static async Task<(byte[] Output, long? Fence)> ClaimReceiptAsync(DataConnection database, SettlementKey key, string effect, bool lockRole)
    {
        var readsFence = SettlementOwnership.IsOwned;
        var connection = (MySqlConnection)await database.OpenDbConnectionAsync(CancellationToken.None);
        await using var command = new MySqlCommand(
            (lockRole ? LockRoleStatement : string.Empty) + ClaimReceiptStatements + (readsFence ? ReadFenceStatement : string.Empty),
            connection,
            (MySqlTransaction)database.Transaction!);
        command.Parameters.AddWithValue("@settlement", key.SettlementID);
        command.Parameters.AddWithValue("@uid", key.GameUlongUID);
        command.Parameters.AddWithValue("@effect", effect);
        await using var reader = await command.ExecuteReaderAsync();
        if (lockRole)
        {
            var roles = 0;
            while (await reader.ReadAsync())
            {
                roles++;
            }

            if (roles != 1)
            {
                throw new InvalidDataException("Settlement role no longer exists");
            }

            await reader.NextResultAsync();
        }

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Settlement receipt was not created");
        }

        var output = (byte[])reader.GetValue(0);
        long? fence = readsFence && await reader.NextResultAsync() && await reader.ReadAsync() ? reader.GetInt64(0) : null;
        return (output, fence);
    }

    private static IQueryable<SettlementEffectRecord> Find(DataConnection database, SettlementKey key, string effect) =>
        database.GetTable<SettlementEffectRecord>().Where(row => row.SettlementID == key.SettlementID
            && row.GameUlongUID == key.GameUlongUID && row.Effect == effect);

    public static async Task LockRoleAsync(DataConnection database, ulong uid)
    {
        var roles = await database.GetTable<Role>().Where(row => row.GameUlongUID == uid)
            .AsMySql().ForUpdateHint().Select(row => row.GameUlongUID).ToArrayAsync();
        if (roles.Length != 1)
        {
            throw new InvalidDataException("Settlement role no longer exists");
        }
    }

    private sealed class RoleWalletConverter : JsonConverter<RoleWallet>
    {
        public override RoleWallet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            new(JsonSerializer.Deserialize<KeyValuePair<int, long>[]>(ref reader, options)!);

        public override void Write(Utf8JsonWriter writer, RoleWallet value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value.Balances, options);
    }

    private sealed class ProtobufConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeof(IMessage).IsAssignableFrom(typeToConvert);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(ProtobufConverter<>).MakeGenericType(typeToConvert), nonPublic: true)!;
    }

    private sealed class ProtobufConverter<T> : JsonConverter<T> where T : IMessage, new()
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var message = new T();
            message.MergeFrom(reader.GetBytesFromBase64());
            return message;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteBase64StringValue(value.ToByteArray());
    }
}
