namespace SpiritUnite.Databin;

public sealed record pbReportText(int ReportCategory, int ReportReason)
{
    public static long Key(int reportCategory, int reportReason) => ((long)reportCategory << 32) | (uint)reportReason;
}
