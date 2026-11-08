using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Room;

public static class RoomMaps
{
    private const int ListedMap = 1;

    public static PB_ENUM_RES_MATCHMAPSUBMODE DefaultSubModeOf(int mapMode) => (PB_ENUM_RES_MATCHMAPMODE)mapMode switch
    {
        PB_ENUM_RES_MATCHMAPMODE.PbMatchMapModeRace => PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceNormal,
        PB_ENUM_RES_MATCHMAPMODE.PbMatchMapModeRelax => PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRelaxNormal,
        PB_ENUM_RES_MATCHMAPMODE.PbMatchMapModeRank => PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank,
        PB_ENUM_RES_MATCHMAPMODE.PbMatchMapModeLegend => PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeLegend,
        _ => PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeInvalid,
    };

    public static bool IsListed(GameData gameData, int mapId) =>
        gameData.SceneMap.Find(mapId) is { ShowInList: ListedMap };

    public static pbScene_Map? FindDefaultMap(GameData gameData, int mapSubMode, IReadOnlySet<int> usableMaps) =>
        ListedMapsOf(gameData, mapSubMode, usableMaps)
            .OrderByDescending(scene => scene.SortPriority)
            .ThenBy(scene => scene.SceneId)
            .FirstOrDefault();

    public static pbCOMDT_ROOMMAP_INFO BuildCanUseMap(GameData gameData, int mapSubMode, IReadOnlySet<int> usableMaps)
    {
        var mapInfo = new pbCOMDT_ROOMMAP_INFO();
        mapInfo.MapList.AddRange(ListedMapsOf(gameData, mapSubMode, usableMaps).Select(scene => scene.SceneId));
        return mapInfo;
    }

    private static IEnumerable<pbScene_Map> ListedMapsOf(GameData gameData, int mapSubMode, IReadOnlySet<int> usableMaps) =>
        gameData.SceneMap.Rows.Where(scene =>
            scene.MapSubMode == mapSubMode && scene.ShowInList == ListedMap && usableMaps.Contains(scene.SceneId));
}
