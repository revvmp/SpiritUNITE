namespace SpiritUNITE.Databin;

public sealed record pbTrainerSeasonExpGift(int SeasonID, int TotalExp)
{
    public static long Key(int seasonID, int totalExp) => ((long)seasonID << 32) | (uint)totalExp;
}
