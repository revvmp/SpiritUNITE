using System.Collections.Frozen;
using System.Text.Json;

namespace SpiritUNITE.Databin;

public sealed class GameNames
{
    public const string PokemonGroup = "Pokemon";
    public const string OutSideItemGroup = "OutSideItem";
    public const string AvatarItemGroup = "AvatarItem";
    public const string PokemonAvatarGroup = "PokemonAvatar";
    public const string HeadIconGroup = "HeadIcon";
    public const string HeadFrameGroup = "HeadFrame";
    public const string LobbyBgGroup = "LobbyBg";
    public const string SceneMapGroup = "SceneMap";
    public const string BadgeGroup = "Badge";

    public static readonly string FilePath = Path.Combine("Databin", "Names", "en.json");

    private readonly FrozenDictionary<string, FrozenDictionary<int, string>> groups;

    private GameNames(FrozenDictionary<string, FrozenDictionary<int, string>> groups) => this.groups = groups;

    public static GameNames Load(string path)
    {
        if (!File.Exists(path))
        {
            return new GameNames(FrozenDictionary<string, FrozenDictionary<int, string>>.Empty);
        }

        var file = JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, string>>>(File.ReadAllText(path)) ?? [];
        return new GameNames(file.ToFrozenDictionary(group => group.Key, group => group.Value.ToFrozenDictionary()));
    }

    public string? Find(string group, int id) =>
        groups.TryGetValue(group, out var names) && names.TryGetValue(id, out var name) ? name : null;
}
