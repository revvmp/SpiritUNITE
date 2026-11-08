namespace SpiritUNITE.Game.MSDKNotice;

public sealed class MSDKNoticeList
{
    public const int MaxNoticeCount = 100;

    public List<MSDKNoticeInfo> NoticeInfoList { get; init; } = [];

    public bool IsValid() =>
        NoticeInfoList.Count <= MaxNoticeCount &&
        NoticeInfoList.All(notice => notice.IsValid()) &&
        NoticeInfoList.DistinctBy(notice => notice.NoticeId).Count() == NoticeInfoList.Count;
}
