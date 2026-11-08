namespace SpiritUnite.Databin;

public sealed record pbHeldItemSystemConfig(int Id, IReadOnlyList<int> LevelStages, int MaxLevel = 0);
