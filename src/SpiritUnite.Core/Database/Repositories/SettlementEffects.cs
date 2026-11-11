using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Wallet;

namespace SpiritUnite.Database.Repositories;

public sealed record SettlementEffectResult<T>(T Value, bool Applied);

public static class SettlementEffects
{
    private static readonly JsonSerializerOptions Serialization = new()
    {
        Converters = { new ProtobufConverterFactory(), new RoleWalletConverter() },
    };

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Serialization);

    public static T Deserialize<T>(byte[] value) => JsonSerializer.Deserialize<T>(value, Serialization)!;

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

        await database.ExecuteAsync(
            "INSERT INTO battle_settlement_effect (settlement_id, game_ulong_uid, effect, output) VALUES (@settlement, @uid, @effect, @output) ON DUPLICATE KEY UPDATE effect=VALUES(effect)",
            new DataParameter("settlement", key.SettlementID),
            new DataParameter("uid", key.GameUlongUID),
            new DataParameter("effect", effect),
            new DataParameter("output", Array.Empty<byte>()));
        var output = await Find(database, key, effect).AsMySql().ForUpdateHint().Select(row => row.Output).SingleAsync();
        if (output.Length > 0)
        {
            return new SettlementEffectResult<T>(Deserialize<T>(output), false);
        }

        if (lockRole)
        {
            await LockRoleAsync(database, key.GameUlongUID);
        }

        await SettlementOwnership.VerifyAsync(database, key.GameUlongUID);
        var result = await mutation();
        await Find(database, key, effect).Set(row => row.Output, Serialize(result)).UpdateAsync();
        return new SettlementEffectResult<T>(result, true);
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
