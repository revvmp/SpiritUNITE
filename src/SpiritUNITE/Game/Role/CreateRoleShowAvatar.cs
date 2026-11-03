using SpiritUNITE.Game.Data;

namespace SpiritUNITE.Game.Role;

public static class CreateRoleShowAvatar
{
    public const int EmptyAvatarSlot = 0;

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
}
