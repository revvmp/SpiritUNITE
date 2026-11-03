using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Data;

public sealed class GameData
{
    public const string DirectoryName = "Data";

    private const string FileExtension = ".json";
    private const int DefaultLobbyBgFlag = 1;

    private static readonly JsonParser MessageParser = new(JsonParser.Settings.Default);

    private static readonly JsonSerializerOptions RecordOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string directory;

    private GameData(string directory)
    {
        this.directory = directory;
        CreateRoleShowAvatar = LoadMessages<pbCreateRoleShowAvatar>(row => row.AvatarID);
        DefaultAvatarItem = LoadMessages<pbDefaultAvatarItem>(row => row.AvatarItemId);
        RoleImage = LoadMessages<pbRoleImage>(row => row.ID);
        NewbieRewardHero = LoadMessages<pbNewbie_RewardHero>(row => row.HeroId);
        LobbyBg = LoadRecords<pbLobbyBg>(row => row.LobbyBgID);
        StarRoadSite = LoadRecords<pbStarRoadSite>(row => row.SiteID);
        ActivityBase = LoadMessages<pbActivity_Base>(row => row.ActivityID);
        TaskBase = LoadMessages<pbTask_Base>(row => row.TaskID);
        DropStatic = LoadMessages<pbDrop_Static>(row => DropStaticKey(row.SDropID, row.SDropSeq));
        Shop = LoadMessages<pbShop>(row => row.ShopID);
        AvatarItem = LoadMessages<pbAvatarItem>(row => row.AvatarItemId);
        AvatarSuit = LoadMessages<pbAvatarSuit>(row => row.AvatarSuitId);
        OutSideItemBase = LoadMessages<pbOutSideItem_Base>(row => row.OutSideItemId);
        RankedMatchSeason = LoadMessages<pbRankedMatchSeason>(row => row.SeasonId);
        DefaultLobbyBgId = FindDefaultLobbyBgId(LobbyBg);
    }

    public DatabinTable<pbCreateRoleShowAvatar> CreateRoleShowAvatar { get; }
    public DatabinTable<pbDefaultAvatarItem> DefaultAvatarItem { get; }
    public DatabinTable<pbRoleImage> RoleImage { get; }
    public DatabinTable<pbNewbie_RewardHero> NewbieRewardHero { get; }
    public DatabinTable<pbLobbyBg> LobbyBg { get; }
    public DatabinTable<pbStarRoadSite> StarRoadSite { get; }
    public DatabinTable<pbActivity_Base> ActivityBase { get; }
    public DatabinTable<pbTask_Base> TaskBase { get; }
    public DatabinTable<pbDrop_Static> DropStatic { get; }
    public DatabinTable<pbShop> Shop { get; }
    public DatabinTable<pbAvatarItem> AvatarItem { get; }
    public DatabinTable<pbAvatarSuit> AvatarSuit { get; }
    public DatabinTable<pbOutSideItem_Base> OutSideItemBase { get; }
    public DatabinTable<pbRankedMatchSeason> RankedMatchSeason { get; }
    public int DefaultLobbyBgId { get; }

    public static GameData Load(string directory) => new(directory);

    public static long DropStaticKey(int sDropID, int sDropSeq) => ((long)sDropID << 32) | (uint)sDropSeq;

    private DatabinTable<TRow> LoadMessages<TRow>(Func<TRow, long> keySelector)
        where TRow : class, IMessage<TRow>, new()
    {
        var tableName = typeof(TRow).Name;
        var rows = ReadFile(tableName, json =>
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("the file is not a JSON array of rows");
            }

            return document.RootElement.EnumerateArray()
                .Select(row => MessageParser.Parse<TRow>(row.GetRawText()))
                .ToArray();
        });
        return new DatabinTable<TRow>(tableName, rows, keySelector);
    }

    private DatabinTable<TRow> LoadRecords<TRow>(Func<TRow, long> keySelector)
        where TRow : class
    {
        var tableName = typeof(TRow).Name;
        var rows = ReadFile(tableName, json =>
        {
            var parsed = JsonSerializer.Deserialize<TRow[]>(json, RecordOptions)
                ?? throw new InvalidDataException("the file is not a JSON array of rows");
            return parsed.Contains(null) ? throw new InvalidDataException("a row is null") : parsed;
        });
        return new DatabinTable<TRow>(tableName, rows, keySelector);
    }

    private TRow[] ReadFile<TRow>(string tableName, Func<string, TRow[]> parse)
    {
        var path = Path.Combine(directory, tableName + FileExtension);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"Game data file {path} cannot be read: {exception.Message} Rebuild so the Data folder is copied to the output.",
                exception);
        }

        try
        {
            return parse(json);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException
            or InvalidProtocolBufferException or InvalidJsonException)
        {
            throw new InvalidDataException(
                $"Game data file {path} is invalid: {exception.Message} Regenerate it with scripts/convert_databins.py.",
                exception);
        }
    }

    private static int FindDefaultLobbyBgId(DatabinTable<pbLobbyBg> lobbyBg)
    {
        var defaults = lobbyBg.Rows.Where(row => row.IsDefault == DefaultLobbyBgFlag).ToArray();
        return defaults.Length == 1
            ? defaults[0].LobbyBgID
            : throw new InvalidDataException(
                $"Game data table {lobbyBg.TableName} has {defaults.Length} default backgrounds; expected exactly one");
    }
}
