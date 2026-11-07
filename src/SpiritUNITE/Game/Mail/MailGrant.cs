using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Mail;

public sealed record MailGrant(
    IReadOnlyDictionary<int, long> Coins,
    IReadOnlyList<RoleItemInfo> Items,
    IReadOnlyList<int> PokemonIds,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards)
{
    public IReadOnlyList<BagGrant> BagGrants { get; init; } = [];
}
