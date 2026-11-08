using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Game.Activity;

public sealed class ActivityProgressPushes(ActivityRewardNotices rewardNotices)
{
    public async Task<GamePacket[]> BuildAsync(LMNRoleInfo role, ActivityBattleProgress progress)
    {
        var packets = new List<GamePacket>();
        if (progress.AutoClaim is { } autoClaim)
        {
            packets.Add(TaskUpdates.BuildUpdateTaskInfoNtf(autoClaim.UpdatedTasks));
            packets.AddRange(await rewardNotices.BuildAsync(role, autoClaim.Rewards));
            if (autoClaim.ChainedTasks.Length > 0)
            {
                packets.Add(TaskUpdates.BuildUpdateTaskInfoNtf(autoClaim.ChainedTasks));
                packets.AddRange(await rewardNotices.BuildAsync(role, autoClaim.ChainedRewards));
            }
        }

        if (progress.UpdatedTasks.Length > 0)
        {
            packets.Add(TaskUpdates.BuildUpdateTaskInfoNtf(progress.UpdatedTasks));
        }

        packets.AddRange(new BattleTaskUpdate(progress.UpdatedTasks, progress.CompleteCountBefore, progress.CompleteCountAfter, progress.AutoClaim)
            .BuildProgressRedDots());
        return [.. packets];
    }
}
