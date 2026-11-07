using SpiritUNITE.Databin;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Guild;

public sealed record GuildActivityPeriod(pbGuildActivity_Base? Row, PB_ENUM_COM_GUILD_ACTIVITY_STATE_TYPE State)
{
    public static readonly GuildActivityPeriod None = new(null, PB_ENUM_COM_GUILD_ACTIVITY_STATE_TYPE.PbComGuildActivityStateNull);

    public int PeriodID => Row?.PeriodID ?? 0;

    public int States => (int)State;

    public bool IsPrepare => State == PB_ENUM_COM_GUILD_ACTIVITY_STATE_TYPE.PbComGuildActivityStatePrepare;

    public bool IsStarting => State == PB_ENUM_COM_GUILD_ACTIVITY_STATE_TYPE.PbComGuildActivityStateStarting;

    public bool ChoosesReward => IsPrepare || IsStarting;

    public static GuildActivityPeriod Find(GameData gameData, long svrCfgTime)
    {
        var rows = gameData.GuildActivityBase.Rows.OrderBy(row => row.PeriodID).ToArray();
        if (rows.FirstOrDefault(row => row.PrepareTime <= svrCfgTime && svrCfgTime <= row.EndTime) is { } current)
        {
            return new GuildActivityPeriod(
                current,
                svrCfgTime < current.StartTime
                    ? PB_ENUM_COM_GUILD_ACTIVITY_STATE_TYPE.PbComGuildActivityStatePrepare
                    : PB_ENUM_COM_GUILD_ACTIVITY_STATE_TYPE.PbComGuildActivityStateStarting);
        }

        return rows.LastOrDefault(row => row.EndTime < svrCfgTime) is { } ended
            ? new GuildActivityPeriod(ended, PB_ENUM_COM_GUILD_ACTIVITY_STATE_TYPE.PbComGuildActivityStateEnd)
            : None;
    }

    public int[] RewardPool(GameData gameData) => Row is null || gameData.GuildActivityRewardPool.Find(Row.RewardPoolID) is not { } pool
        ? []
        : [.. pool.RewardID.Where(rewardID => rewardID > 0 && gameData.GuildActivityScoreReward.Rows.Any(reward => reward.RewardID == rewardID))];
}
