using SpiritUnite.Databin;

namespace SpiritUnite.Game.Role;

public static class CreateRoleShowAvatar
{
    public const int EmptyAvatarSlot = 0;
    public const int NoSuitSeq = 0;

    private const int MaxAvatarSlots = 10;

    public static bool IsCreationChoice(GameData gameData, IReadOnlyCollection<int> avatarIDList)
    {
        if (avatarIDList.Count > MaxAvatarSlots)
        {
            return false;
        }

        var chosen = avatarIDList.Where(id => id != EmptyAvatarSlot).ToList();
        return chosen.Distinct().Count() == chosen.Count
            && chosen.All(id => gameData.CreateRoleShowAvatar.Contains(id) || gameData.DefaultAvatarItem.Contains(id));
    }

    public static int GetSuitSeq(GameData gameData, int avatarID) =>
        gameData.CreateRoleShowAvatar.Find(avatarID)?.SuitSeq ?? NoSuitSeq;

    public static bool IsSuitItem(GameData gameData, int avatarID) => GetSuitSeq(gameData, avatarID) != NoSuitSeq;

    public static bool IsBodyFeature(GameData gameData, int avatarID) =>
        gameData.CreateRoleShowAvatar.Contains(avatarID) && !IsSuitItem(gameData, avatarID);
}
