using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;

namespace SpiritUnite.Game.MSDKNotice;

public sealed class NoticeManager(ServerContentRepository repository)
{
    private const long NoTime = 0;

    public async Task<MSDKNoticeList> ListAsync() => new()
    {
        NoticeInfoList = [.. (await repository.ListNoticesAsync()).Select(ToNoticeInfo)],
    };

    public async Task<bool> ReplaceAsync(MSDKNoticeList notices)
    {
        if (!notices.IsValid())
        {
            return false;
        }

        await repository.ReplaceNoticesAsync([.. notices.NoticeInfoList.Select(ToRow)]);
        return true;
    }

    private static MSDKNoticeInfo ToNoticeInfo(MsdkNotice row) => new()
    {
        NoticeId = row.NoticeId,
        NoticeType = row.NoticeType,
        NoticeGroup = row.NoticeGroup,
        BeginTime = row.BeginTime == NoTime ? null : DateTimeOffset.FromUnixTimeSeconds(row.BeginTime),
        EndTime = row.EndTime == NoTime ? null : DateTimeOffset.FromUnixTimeSeconds(row.EndTime),
        Order = row.Order,
        NoticeTitle = row.NoticeTitle,
        NoticeContent = row.NoticeContent,
    };

    private static MsdkNotice ToRow(MSDKNoticeInfo notice) => new()
    {
        NoticeId = notice.NoticeId,
        NoticeType = notice.NoticeType,
        NoticeGroup = notice.NoticeGroup,
        BeginTime = notice.BeginTime?.ToUnixTimeSeconds() ?? NoTime,
        EndTime = notice.EndTime?.ToUnixTimeSeconds() ?? NoTime,
        Order = notice.Order,
        NoticeTitle = notice.NoticeTitle,
        NoticeContent = notice.NoticeContent,
    };
}
