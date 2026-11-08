using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpiritUnite.Battle;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.HeldItem;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Match;
using SpiritUnite.Game.MSDKNotice;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Room;
using SpiritUnite.Game.ServerContent;
using SpiritUnite.Game.Wallet;

namespace SpiritUnite.Game.Admin;

public sealed record AdminSettingsSection(string Name, string Group, string Title, string Description, JsonObject Schema, JsonObject Values, bool RestartNeeded);

public sealed record AdminSettingsSave(AdminSettingsSection? Section, string? Error);

public sealed class AdminSettings(
    IServiceProvider services,
    IConfiguration configuration,
    NoticeManager notices,
    ServerLists serverLists,
    LobbyBroadcast lobbyBroadcast)
{
    public const string GameSettingsFileName = "gamesettings.json";
    private const string OfficialSuffix = "OrOfficial";
    private const string IsValidMethod = "IsValid";
    private const string ValidateMethod = "Validate";
    private const string ContentGroup = "Content";
    private const string RulesGroup = "Rules";
    private const string NoticesSection = "MSDKNotice";
    private const string ListsSection = "ServerLists";
    private const string LongTextSuffix = "Content";
    private const string TemporarySuffix = ".tmp";

    private static readonly SettingsSection[] Sections =
    [
        new(NoticesSection, "Notices", "Notices players see on the login screen", typeof(MSDKNoticeList), HasOfficialValues: false, IsContent: true),
        new(ListsSection, "Rotation", "Free Pokemon, subscription perks and new sale items", typeof(ServerListContent), HasOfficialValues: false, IsContent: true),
        new(LobbyOptions.SectionName, "Lobby", "Lobby features and schemes", typeof(LobbyOptions)),
        new(WalletOptions.SectionName, "Wallet", "What new trainers start with", typeof(WalletOptions), HasOfficialValues: false),
        new(MatchOptions.SectionName, "Matchmaking", "Bot fill and warm-up matches", typeof(MatchOptions)),
        new(RoomOptions.SectionName, "Rooms", "Map availability and room timings", typeof(RoomOptions)),
        new(RankedOptions.SectionName, "Ranked", "Ranked points, season reset and bot values", typeof(RankedOptions)),
        new(BattleSettlementOptions.SectionName, "Battle rewards", "Trainer exp and energy after a battle", typeof(BattleSettlementOptions)),
        new(HeroPickOptions.SectionName, "Hero pick", "Which Pokemon bots can pick", typeof(HeroPickOptions)),
        new(HeldItemOptions.SectionName, "Held items", "Items given when held items unlock", typeof(HeldItemOptions)),
        new(MegaEvolutionOptions.SectionName, "Mega Evolution", "Mega Evolution event training", typeof(MegaEvolutionOptions)),
    ];

    private static readonly JsonSerializerOptions FileFormat = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly SemaphoreSlim fileGate = new(1, 1);

    public static string GameSettingsPath => Path.Combine(AppContext.BaseDirectory, GameSettingsFileName);

    public async Task<AdminSettingsSection[]> ListAsync()
    {
        var file = await ReadFileAsync();
        var sections = new List<AdminSettingsSection>();
        foreach (var section in Sections)
        {
            sections.Add(section.IsContent ? DescribeContent(section, await LoadContentAsync(section.Name)) : Describe(section, file));
        }

        return [.. sections];
    }

    public async Task<AdminSettingsSave> SaveAsync(string sectionName, JsonObject values)
    {
        if (Sections.FirstOrDefault(section => section.Name == sectionName) is not { } section)
        {
            return new AdminSettingsSave(null, "Unknown section");
        }

        return section.IsContent ? await SaveContentAsync(section, values) : await SaveFileSectionAsync(section, values);
    }

    private async Task<AdminSettingsSave> SaveContentAsync(SettingsSection section, JsonObject values)
    {
        if (Bind(section.Name, section.Type, values) is not { } bound)
        {
            return new AdminSettingsSave(null, "Some values have the wrong type");
        }

        var newSaleBefore = serverLists.Current.NewSaleShopIDs;
        var saved = bound switch
        {
            MSDKNoticeList noticeList => await notices.ReplaceAsync(noticeList),
            ServerListContent lists => await serverLists.ReplaceAsync(lists),
            _ => false,
        };
        if (!saved)
        {
            return new AdminSettingsSave(null, "Some values are not allowed or not in the game data");
        }

        if (bound is ServerListContent changedLists && !changedLists.NewSaleShopIDs.SequenceEqual(newSaleBefore))
        {
            await lobbyBroadcast.PushShopNewSaleAsync();
        }

        return new AdminSettingsSave(DescribeContent(section, await LoadContentAsync(section.Name)), null);
    }

    private async Task<AdminSettingsSave> SaveFileSectionAsync(SettingsSection section, JsonObject values)
    {
        AdminSettingsSection described;
        await fileGate.WaitAsync();
        try
        {
            var file = await ReadFileAsync();
            var saved = Overrides(section.Type, file[section.Name] as JsonObject ?? [], values);
            if (Bind(section.Name, section.Type, saved) is not { } bound)
            {
                return new AdminSettingsSave(null, "Some values have the wrong type");
            }

            if (ValidationFailure(bound, section.Type) is { } failure)
            {
                return new AdminSettingsSave(null, failure);
            }

            if (saved.Count == 0)
            {
                file.Remove(section.Name);
            }
            else
            {
                file[section.Name] = saved;
            }

            await WriteFileAsync(file);
            ReloadGameSettings();
            described = Describe(section, file);
        }
        finally
        {
            fileGate.Release();
        }

        if (section.Type == typeof(LobbyOptions))
        {
            await lobbyBroadcast.PushRefreshAsync();
        }

        return new AdminSettingsSave(described, null);
    }

    private async Task<object> LoadContentAsync(string sectionName) => sectionName switch
    {
        NoticesSection => await notices.ListAsync(),
        _ => serverLists.Current,
    };

    private static AdminSettingsSection DescribeContent(SettingsSection section, object content) => new(
        section.Name,
        ContentGroup,
        section.Title,
        section.Description,
        new JsonObject { ["kind"] = "object", ["fields"] = FieldsOf(section.Type, withOfficial: false) },
        ToNode(content, section.Type) as JsonObject ?? [],
        RestartNeeded: false);

    private void ReloadGameSettings()
    {
        if (configuration is not IConfigurationRoot root)
        {
            return;
        }

        foreach (var provider in root.Providers.OfType<JsonConfigurationProvider>().Where(provider => provider.Source.Path == GameSettingsFileName))
        {
            provider.Load();
        }
    }

    private string? ValidationFailure(object bound, Type type)
    {
        if (!IsValid(bound, type))
        {
            return "Some values are not allowed";
        }

        var validatorType = typeof(IValidateOptions<>).MakeGenericType(type);
        var validate = validatorType.GetMethod(ValidateMethod);
        foreach (var validator in services.GetServices(validatorType))
        {
            if (validate?.Invoke(validator, [Options.DefaultName, bound]) is ValidateOptionsResult { Failed: true } result)
            {
                return string.Join(". ", result.Failures ?? ["Some values are not allowed"]);
            }
        }

        return null;
    }

    private static JsonObject Overrides(Type type, JsonObject current, JsonObject values)
    {
        var defaults = ToNode(Activator.CreateInstance(type), type) as JsonObject ?? [];
        var saved = new JsonObject();
        foreach (var property in BindableProperties(type))
        {
            if (values.TryGetPropertyValue(property.Name, out var value))
            {
                if (current.ContainsKey(property.Name) || !JsonNode.DeepEquals(value, defaults[property.Name]))
                {
                    saved[property.Name] = value?.DeepClone();
                }
            }
            else if (current[property.Name] is { } kept)
            {
                saved[property.Name] = kept.DeepClone();
            }
        }

        return saved;
    }

    private AdminSettingsSection Describe(SettingsSection section, JsonObject file)
    {
        var fileValue = Bind(section.Name, section.Type, file[section.Name] as JsonObject ?? []) ?? Activator.CreateInstance(section.Type);
        var values = ToNode(fileValue, section.Type) as JsonObject ?? [];
        return new AdminSettingsSection(
            section.Name,
            RulesGroup,
            section.Title,
            section.Description,
            new JsonObject { ["kind"] = "object", ["fields"] = FieldsOf(section.Type, section.HasOfficialValues) },
            values,
            !JsonNode.DeepEquals(values, ToNode(LiveValue(section.Type), section.Type)));
    }

    private object? LiveValue(Type type)
    {
        var options = services.GetRequiredService(typeof(IOptionsMonitor<>).MakeGenericType(type));
        return options.GetType().GetProperty(nameof(IOptionsMonitor<object>.CurrentValue))?.GetValue(options);
    }

    private static object? Bind(string sectionName, Type type, JsonObject sectionValues)
    {
        try
        {
            var document = new JsonObject { [sectionName] = sectionValues.DeepClone() };
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(document.ToJsonString()));
            var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
            return configuration.GetSection(sectionName).Get(type) ?? Activator.CreateInstance(type);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static bool IsValid(object options, Type type) =>
        type.GetMethod(IsValidMethod, Type.EmptyTypes) is not { } isValid || isValid.Invoke(options, null) is true;

    private static async Task<JsonObject> ReadFileAsync()
    {
        if (!File.Exists(GameSettingsPath))
        {
            return [];
        }

        await using var stream = File.OpenRead(GameSettingsPath);
        return await JsonNode.ParseAsync(stream) as JsonObject ?? [];
    }

    private static async Task WriteFileAsync(JsonObject file)
    {
        var temporaryPath = GameSettingsPath + TemporarySuffix;
        await File.WriteAllTextAsync(temporaryPath, file.ToJsonString(FileFormat) + Environment.NewLine);
        File.Move(temporaryPath, GameSettingsPath, overwrite: true);
    }

    private static PropertyInfo[] BindableProperties(Type type) =>
    [
        .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0),
    ];

    private static object? EffectiveValue(object owner, PropertyInfo property) =>
        property.GetValue(owner) ?? owner.GetType().GetProperty(property.Name + OfficialSuffix)?.GetValue(owner);

    private static JsonNode? ToNode(object? value, Type declaredType) => value switch
    {
        null => null,
        string text => JsonValue.Create(text),
        bool flag => JsonValue.Create(flag),
        DateTimeOffset time => JsonValue.Create(time.ToString("o", CultureInfo.InvariantCulture)),
        DateTime time => JsonValue.Create(time.ToString("o", CultureInfo.InvariantCulture)),
        Enum => JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        _ when IsNumber(value.GetType()) => JsonValue.Create(Convert.ToDecimal(value, CultureInfo.InvariantCulture)),
        IDictionary dictionary => DictionaryNode(dictionary),
        IEnumerable items => new JsonArray([.. items.Cast<object?>().Select(item => ToNode(item, item?.GetType() ?? ElementTypeOf(declaredType) ?? typeof(object)))]),
        _ => ObjectNode(value),
    };

    private static JsonObject DictionaryNode(IDictionary dictionary)
    {
        var node = new JsonObject();
        foreach (DictionaryEntry entry in dictionary)
        {
            node[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = ToNode(entry.Value, entry.Value?.GetType() ?? typeof(object));
        }

        return node;
    }

    private static JsonObject ObjectNode(object value)
    {
        var node = new JsonObject();
        foreach (var property in BindableProperties(value.GetType()))
        {
            node[property.Name] = ToNode(EffectiveValue(value, property), property.PropertyType);
        }

        return node;
    }

    private static JsonObject SchemaOf(Type declaredType, string? propertyName = null)
    {
        var underlying = Nullable.GetUnderlyingType(declaredType);
        var type = underlying ?? declaredType;
        if (SimpleKind(type, propertyName) is { } kind)
        {
            return new JsonObject { ["kind"] = kind, ["nullable"] = underlying is not null };
        }

        if (typeof(IDictionary).IsAssignableFrom(type))
        {
            return new JsonObject { ["kind"] = "json" };
        }

        if (ElementTypeOf(type) is { } elementType)
        {
            return SimpleKind(Nullable.GetUnderlyingType(elementType) ?? elementType, null) is { } itemKind
                ? new JsonObject { ["kind"] = "list", ["item"] = itemKind }
                : new JsonObject
                {
                    ["kind"] = "objects",
                    ["fields"] = FieldsOf(elementType, withOfficial: false),
                    ["template"] = ToNode(Activator.CreateInstance(elementType), elementType),
                };
        }

        return new JsonObject { ["kind"] = "object", ["fields"] = FieldsOf(type, withOfficial: false) };
    }

    private static JsonArray FieldsOf(Type type, bool withOfficial)
    {
        var official = withOfficial ? ToNode(Activator.CreateInstance(type), type) as JsonObject : null;
        return
        [
            .. BindableProperties(type).Select(property => (JsonNode)new JsonObject
            {
                ["name"] = property.Name,
                ["label"] = AdminSettingsText.Find(type, property.Name)?.Label,
                ["help"] = AdminSettingsText.Find(type, property.Name)?.Help,
                ["official"] = official?[property.Name]?.DeepClone(),
                ["schema"] = SchemaOf(property.PropertyType, property.Name),
            }),
        ];
    }

    private static string? SimpleKind(Type type, string? propertyName) =>
        type == typeof(bool) ? "bool"
        : type == typeof(string) ? (propertyName?.EndsWith(LongTextSuffix, StringComparison.Ordinal) == true ? "longtext" : "text")
        : type == typeof(DateTimeOffset) || type == typeof(DateTime) ? "time"
        : type.IsEnum || IsNumber(type) ? "number"
        : null;

    private static bool IsNumber(Type type) => Type.GetTypeCode(type) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
        or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal;

    private static Type? ElementTypeOf(Type type) =>
        type == typeof(string) ? null
        : type.IsArray ? type.GetElementType()
        : type.GetInterfaces().Append(type)
            .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];

    private sealed record SettingsSection(string Name, string Title, string Description, Type Type, bool HasOfficialValues = true, bool IsContent = false);
}
