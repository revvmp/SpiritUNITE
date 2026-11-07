using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.PokemonAvatar;

public sealed record PasterSprayInfo(int PasterId, int PasterSprayID, long TimeLimited, bool IsWearing)
{
    public bool IsUsable(long now) => TimeLimited > now;

    public pbCOMDT_SPRAYER_INFO ToSprayerInfo(int sprayerBuffId) => new()
    {
        SprayerId = PasterSprayID,
        TimeLimited = TimeLimited,
        SprayerBuffId = sprayerBuffId,
    };
}
