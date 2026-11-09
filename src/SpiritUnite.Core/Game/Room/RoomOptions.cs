using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Room;

public sealed class RoomMapCanUseType
{
    public int CanUseType { get; init; }
    public List<int> MapIds { get; init; } = [];
}

public sealed class RoomOptions
{
    public const string SectionName = "Room";
    public const int CanUseMap = 1;

    private const int MinManMachineSearchSec = 1;
    private const int MaxManMachineSearchSec = 3600;

    private static readonly int[] OfficialHiddenMapIds = [92001, 92002, 92003, 92004];

    public List<RoomMapCanUseType> MapCanUseTypes { get; init; } = [];
    public List<int>? HiddenMapIds { get; init; }
    public int ManMachineSearchSec { get; init; } = 60;
    public int TeamMMRScore { get; init; } = 1343;
    public int SettleTeamOfflineMinSec { get; init; } = 14;
    public int SettleTeamOfflineMaxSec { get; init; } = 52;

    public IReadOnlyList<int> HiddenMapIdsOrOfficial => HiddenMapIds ?? (IReadOnlyList<int>)OfficialHiddenMapIds;

    public bool IsValid()
    {
        var mapIds = MapCanUseTypes.SelectMany(entry => entry.MapIds).Concat(HiddenMapIdsOrOfficial).ToList();
        return ManMachineSearchSec is >= MinManMachineSearchSec and <= MaxManMachineSearchSec
            && TeamMMRScore >= 0
            && SettleTeamOfflineMinSec >= 0
            && SettleTeamOfflineMaxSec >= SettleTeamOfflineMinSec
            && SettleTeamOfflineMaxSec <= MaxManMachineSearchSec
            && MapCanUseTypes.All(entry => entry.CanUseType > CanUseMap)
            && mapIds.All(mapId => mapId > 0)
            && mapIds.Distinct().Count() == mapIds.Count;
    }

    public static bool IsListedCanUseType(int canUseType) =>
        canUseType is CanUseMap or SceneMapOpening.LimitedTimeMapNotOpen;

    public bool IsHidden(int mapId) => HiddenMapIdsOrOfficial.Contains(mapId);

    public int CanUseTypeOf(GameData gameData, int mapId, DateTimeOffset now) =>
        HostCanUseTypeOf(mapId) ?? SceneMapOpening.CanUseTypeOf(gameData, mapId, now);

    public int? HostCanUseTypeOf(int mapId) =>
        MapCanUseTypes.FirstOrDefault(entry => entry.MapIds.Contains(mapId))?.CanUseType;
}

public sealed class GVoiceOptions
{
    public const int OfficialIdcTag = 2;
    public const string GVoiceHostScheme = "udp://";

    public int IdcTag => OfficialIdcTag;
    public string GVoiceHost { get; private set; } = string.Empty;
    public int GVoicePort { get; private set; }

    public void UseVoiceServer(string advertisedHost, int voicePort)
    {
        GVoiceHost = GVoiceHostScheme + advertisedHost;
        GVoicePort = voicePort;
    }

    public pbCOMDT_GVOICE_INFO BuildGVoiceInfo() => new()
    {
        IdcTag = IdcTag,
        GVoiceHost = GVoiceHost,
        GVoicePort = GVoicePort,
    };
}
