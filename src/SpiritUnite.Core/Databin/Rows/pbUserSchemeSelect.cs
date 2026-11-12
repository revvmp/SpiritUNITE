namespace SpiritUnite.Databin;

public sealed record pbUserSchemeSelect(
    int SchemeId,
    int BusinessType,
    IReadOnlyList<int> UID,
    IReadOnlyList<int> NationID,
    long RegisterStartTime = 0,
    long RegisterStartTimeStamp = 0,
    long RegisterEndTime = 0,
    long RegisterEndTimeStamp = 0)
{
    public static long Key(int businessType, int schemeId) => ((long)businessType << 32) | (uint)schemeId;
}
