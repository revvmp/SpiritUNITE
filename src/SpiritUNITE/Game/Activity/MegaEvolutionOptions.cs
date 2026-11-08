namespace SpiritUNITE.Game.Activity;

public sealed class MegaEvolutionOptions
{
    public const string SectionName = "MegaEvolution";

    public int TrainMinComplete { get; init; }

    public int TrainMaxComplete { get; init; }

    public bool IsValid() => TrainMinComplete > 0 && TrainMinComplete <= TrainMaxComplete && TrainMaxComplete <= MegaEvolutionManager.MaxComplete;
}
