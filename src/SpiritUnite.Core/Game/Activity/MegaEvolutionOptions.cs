namespace SpiritUnite.Game.Activity;

public sealed class MegaEvolutionOptions
{
    public const string SectionName = "MegaEvolution";

    public int TrainMinComplete { get; init; } = 1;

    public int TrainMaxComplete { get; init; } = 10;

    public bool IsValid() => TrainMinComplete > 0 && TrainMinComplete <= TrainMaxComplete && TrainMaxComplete <= MegaEvolutionManager.MaxComplete;
}
