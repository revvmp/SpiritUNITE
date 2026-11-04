namespace SpiritUNITE.Game.Notice;

public sealed class NoticeOptions
{
    public const string SectionName = "MSDKNotice";

    public List<MSDKNoticeInfo> NoticeInfoList { get; init; } = [];

    public bool IsValid() =>
        NoticeInfoList.All(notice => notice.IsValid()) &&
        NoticeInfoList.DistinctBy(notice => notice.NoticeId).Count() == NoticeInfoList.Count;
}
