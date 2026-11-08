namespace SpiritUnite.Databin;

public sealed record pbTrainerExpGift(
    int SchemeId,
    int TotalExp,
    int Level = 0,
    int AwardType = 0,
    int AwardParam = 0,
    int IsBigAward = 0,
    int BigAwardParam = 0,
    IReadOnlyList<int>? AwardItemParam = null)
{
    public static long Key(int schemeId, int totalExp) => ((long)schemeId << 32) | (uint)totalExp;
}
