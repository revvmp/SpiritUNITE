namespace SpiritUnite.Game.Activity;

public sealed class MegaEvolutionOptions
{
    public const string SectionName = "MegaEvolution";

    private static readonly MegaEvolutionTrainResultOptions[] OfficialTrainResults =
    [
        new() { ActivityID = 19582, TrainResults = [1, 2, 3] },
        new() { ActivityID = 19583, TrainResults = [5, 10, 15, 20] },
        new() { ActivityID = 20296, TrainResults = [1, 2, 3, 4] },
        new() { ActivityID = 21167, TrainResults = [8, 10, 12, 16] },
        new() { ActivityID = 21301, TrainResults = [15, 20, 25] },
        new() { ActivityID = 23112, TrainResults = [8, 10, 12, 16] },
        new() { ActivityID = 23114, TrainResults = [1, 2, 3] },
        new() { ActivityID = 23211, TrainResults = [8, 10, 12, 16] },
    ];

    private static readonly int[] OfficialFallbackTrainResults = [8, 10, 12, 16];

    public MegaEvolutionTrainResultOptions[]? TrainResults { get; init; }

    public int[]? FallbackTrainResults { get; init; }

    public IReadOnlyList<MegaEvolutionTrainResultOptions> TrainResultsOrOfficial => TrainResults ?? OfficialTrainResults;

    public IReadOnlyList<int> FallbackTrainResultsOrOfficial => FallbackTrainResults ?? OfficialFallbackTrainResults;

    public IReadOnlyList<int>? FindTrainResults(int activityID) =>
        TrainResultsOrOfficial.FirstOrDefault(entry => entry.ActivityID == activityID)?.TrainResults;

    public bool IsValid() =>
        IsValidResults(FallbackTrainResultsOrOfficial)
        && TrainResultsOrOfficial.All(entry => entry.ActivityID > 0 && IsValidResults(entry.TrainResults))
        && TrainResultsOrOfficial.DistinctBy(entry => entry.ActivityID).Count() == TrainResultsOrOfficial.Count;

    private static bool IsValidResults(IReadOnlyList<int> results) =>
        results.Count > 0 && results.All(result => result > 0 && result <= MegaEvolutionManager.MaxComplete);
}

public sealed class MegaEvolutionTrainResultOptions
{
    public int ActivityID { get; init; }

    public int[] TrainResults { get; init; } = [];
}
