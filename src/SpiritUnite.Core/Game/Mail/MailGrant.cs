using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Mail;

public sealed record MailGrant(
    IReadOnlyDictionary<int, long> Coins,
    IReadOnlyList<RoleItemInfo> Items,
    IReadOnlyList<int> PokemonIds,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards)
{
    public IReadOnlyList<BagGrant> BagGrants { get; init; } = [];

    public CouponsFlowEntry? CouponsFlow { get; init; }
}
