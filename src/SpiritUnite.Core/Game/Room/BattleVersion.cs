using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Room;

public static class BattleVersion
{
    public const ulong OfficialSection1 = 307867550744583;

    public static pbCOMDT_BATTLE_VERSION Build() => new() { Section1 = OfficialSection1 };
}
