using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Role;

public static class NewPlayerDefaultGrants
{
    public static IReadOnlyList<pbNewPlayerDefaultRes> RowsOf(GameData gameData, PB_ENUM_RES_RESOURCES_TYPE resourceType) =>
        [
            .. gameData.NewPlayerDefaultRes.Rows
                .Where(row => row.ResourceType == (int)resourceType && row.ResourceNum > 0)
                .OrderBy(row => row.NewplayerRewardId),
        ];

    public static int[] ResourceIdsOf(GameData gameData, PB_ENUM_RES_RESOURCES_TYPE resourceType) =>
        [.. RowsOf(gameData, resourceType).Select(row => row.ResourceId)];
}
