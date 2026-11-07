namespace SpiritUNITE.Game.PokemonAvatar;

public sealed record PasterTimeLimit(int PasterId, long DurationSec, long ExpireTime)
{
    public long ExtendFrom(long currentTimeLimited, long now) => DurationSec > 0
        ? checked(Math.Max(currentTimeLimited, now) + DurationSec)
        : Math.Max(currentTimeLimited, ExpireTime);
}
