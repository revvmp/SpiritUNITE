namespace SpiritUnite.Game.MSDKNotice;

public sealed class MSDKNoticeInfo
{
    public const int MaximumTitleLength = 200;
    public const int MaximumContentLength = 8000;
    public const int MaximumGroupLength = 40;

    public int NoticeId { get; init; }
    public int NoticeType { get; init; }
    public string NoticeGroup { get; init; } = string.Empty;
    public DateTimeOffset? BeginTime { get; init; }
    public DateTimeOffset? EndTime { get; init; }
    public int Order { get; init; }
    public string NoticeTitle { get; init; } = string.Empty;
    public string NoticeContent { get; init; } = string.Empty;

    public int BeginTimestamp => BeginTime is { } begin ? (int)begin.ToUnixTimeSeconds() : 0;
    public int EndTimestamp => EndTime is { } end ? (int)end.ToUnixTimeSeconds() : int.MaxValue;

    public bool IsValid() =>
        NoticeId > 0 &&
        NoticeType >= 0 &&
        !string.IsNullOrWhiteSpace(NoticeGroup) &&
        NoticeGroup.Length <= MaximumGroupLength &&
        NoticeTitle.Length <= MaximumTitleLength &&
        NoticeContent.Length <= MaximumContentLength &&
        FitsTimestamp(BeginTime) &&
        FitsTimestamp(EndTime) &&
        BeginTimestamp < EndTimestamp;

    private static bool FitsTimestamp(DateTimeOffset? time) =>
        time is not { } value || value.ToUnixTimeSeconds() is >= 0 and <= int.MaxValue;
}
