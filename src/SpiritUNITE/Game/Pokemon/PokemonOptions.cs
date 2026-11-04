namespace SpiritUNITE.Game.Pokemon;

public sealed class PokemonOptions
{
    public const string SectionName = "Pokemon";

    public int[] FreePokemonList { get; init; } = [];

    public bool IsValid() => FreePokemonList.All(pokemonId => pokemonId > 0) && FreePokemonList.Distinct().Count() == FreePokemonList.Length;
}
