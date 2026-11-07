using SpiritUNITE.Databin;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Role;

namespace SpiritUNITE.Game.PokemonAvatar;

public static class PasterGrant
{
    public static bool IsPaster(GameData gameData, int resID) => gameData.PokemonAvatarBase.Contains(resID);

    public static CosmeticGrant? Plan(
        GameData gameData,
        BagManager bag,
        LMNRoleInfo role,
        int pasterID,
        int resNum,
        long gainTime,
        IEnumerable<RoleItemInfo> plannedItems) =>
        IsPaster(gameData, pasterID) ? CosmeticGrant.Plan(gameData, bag, role, pasterID, resNum, gainTime, plannedItems) : null;
}
