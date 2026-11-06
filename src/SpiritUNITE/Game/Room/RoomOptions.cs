using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed class RoomMapCanUseType
{
    public int CanUseType { get; init; }
    public List<int> MapIds { get; init; } = [];
}

public sealed class RoomOptions
{
    public const string SectionName = "Room";
    public const int CanUseMap = 1;

    public List<RoomMapCanUseType> MapCanUseTypes { get; init; } = [];
    public List<int> HiddenMapIds { get; init; } = [];

    public bool IsValid()
    {
        var mapIds = MapCanUseTypes.SelectMany(entry => entry.MapIds).Concat(HiddenMapIds).ToList();
        return MapCanUseTypes.All(entry => entry.CanUseType > CanUseMap)
            && mapIds.All(mapId => mapId > 0)
            && mapIds.Distinct().Count() == mapIds.Count;
    }

    public bool IsHidden(int mapId) => HiddenMapIds.Contains(mapId);

    public int CanUseTypeOf(int mapId) =>
        MapCanUseTypes.FirstOrDefault(entry => entry.MapIds.Contains(mapId))?.CanUseType ?? CanUseMap;
}

public sealed class GVoiceOptions
{
    public const string SectionName = "GVoice";

    public int IdcTag { get; init; } = 2;
    public string GVoiceHost { get; init; } = "udp://us.voice.gcloudcs.com";
    public int GVoicePort { get; init; } = 8700;

    public bool IsValid() => IdcTag >= 0 && GVoicePort is >= 0 and <= ushort.MaxValue && GVoiceHost is not null;

    public pbCOMDT_GVOICE_INFO BuildGVoiceInfo() => new()
    {
        IdcTag = IdcTag,
        GVoiceHost = GVoiceHost,
        GVoicePort = GVoicePort,
    };
}
