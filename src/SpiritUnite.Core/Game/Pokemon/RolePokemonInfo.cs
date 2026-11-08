using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Pokemon;

public sealed record RolePokemonInfo(int PokemonId, long GainTime, int GainChannel, int NewTag, long TimeLimited = RolePokemonInfo.Permanent)
{
    public const int NoNewTag = 0;
    public const int HasNewTag = 1;
    public const long Permanent = 0;

    public pbCOMDT_POKEMON_INFO ToPokemonInfo() => new()
    {
        PokemonId = PokemonId,
        GainTime = GainTime,
        GainChannel = GainChannel,
        NewTag = NewTag,
        TimeLimited = TimeLimited,
    };
}
