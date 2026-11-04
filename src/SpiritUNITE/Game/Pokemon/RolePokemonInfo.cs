using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Pokemon;

public sealed record RolePokemonInfo(int PokemonId, long GainTime, int GainChannel, int NewTag)
{
    public const int NoNewTag = 0;
    public const int HasNewTag = 1;

    public pbCOMDT_POKEMON_INFO ToPokemonInfo() => new()
    {
        PokemonId = PokemonId,
        GainTime = GainTime,
        GainChannel = GainChannel,
        NewTag = NewTag,
    };
}
