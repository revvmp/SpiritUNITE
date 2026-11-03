using System.Collections.Frozen;

namespace SpiritUNITE.Game.Role;

public static class CreateRoleShowAvatar
{
    public const int EmptyAvatarSlot = 0;

    private const int MaxAvatarSlots = 10;

    private static readonly FrozenSet<int> AvatarIDs =
    [
        1004000111, 1004000211, 1004000311, 1004000411, 1004000511, 1004000611,
        1004000110, 1004000210, 1004000310, 1004000410, 1004000510, 1004000610, 1004000710, 1004000810, 1004000910,
        1004000113, 1004000213, 1004000313, 1004000413, 1004000513, 1004000613,
        1004000114, 1004000105, 1004000108, 1004000109, 1004000107,
        1004000214, 1004000205, 1004000208, 1004000209, 1004000207,
        1004001106, 1004001206, 1004001406,
    ];

    private static readonly FrozenSet<int> DefaultAvatarItemIds =
    [
        1004000001, 1004000002, 1004000003, 1004000004, 1004000005, 1004000006, 1004000007, 1004000110, 1004001306,
    ];

    public static bool IsCreationChoice(IReadOnlyCollection<int> avatarIDList)
    {
        if (avatarIDList.Count > MaxAvatarSlots)
        {
            return false;
        }

        var chosen = avatarIDList.Where(id => id != EmptyAvatarSlot).ToList();
        return chosen.Distinct().Count() == chosen.Count
            && chosen.All(id => AvatarIDs.Contains(id) || DefaultAvatarItemIds.Contains(id));
    }
}
