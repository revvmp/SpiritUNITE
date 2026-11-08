namespace SpiritUnite.Databin;

public sealed record pbMatchTrainer(int MapId, int DifficultyLevel = 0, int NeedCnt = 0)
{
    public static long Key(int mapId, int difficultyLevel) => ((long)difficultyLevel << 32) | (uint)mapId;
}
