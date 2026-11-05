using SpiritUNITE.Game.Role;

namespace SpiritUNITE.Game.Mail;

public sealed record MailGrant(
    IReadOnlyDictionary<int, long> Coins,
    IReadOnlyList<RoleItemInfo> Items,
    IReadOnlyList<int> PokemonIds);
