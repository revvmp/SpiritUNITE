using SpiritUnite.Game.Lobby;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Tasks;

public static class TaskRedDots
{
    private static readonly PB_ENUM_RES_RED_DOT_ID[] ActivityTaskInfoClears =
    [
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdPokemonDailyActivityReward,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdJubileeCakeReward,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdJubileeCollection,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdPokemonTrainerTask,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdMastreeTask,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdWatchAndWinbigLuckyGift,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdSkyTrainerTask,
    ];

    public static GamePacket[] BuildTaskInfoRedDots(PB_ENUM_ENM_CLIENT_TASKMODULE moduleType, IReadOnlyCollection<pbCOMDT_TASK_INFO> tasks) =>
        moduleType switch
        {
            PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleActivity =>
            [
                BuildCount(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdActivityReward, CountComplete(tasks)),
                .. ActivityTaskInfoClears.Select(BuildClear),
            ],
            PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleRankedMatch => [BuildClear(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdRankedMatchReward)],
            PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleNewbiechallenge =>
                [BuildCount(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewbieChallengeReward, CountComplete(tasks))],
            PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleAchievement =>
                [BuildCount(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdAchievement, CountComplete(tasks))],
            PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleCreditpoint =>
                [BuildCount(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdCreditPointReward, CountComplete(tasks))],
            _ => [],
        };

    public static GamePacket[] BuildBattleProgressRedDots(int activityCompleteCountBefore, int activityCompleteCountAfter) =>
    [
        BuildClear(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdSnowTurntableDraw),
        .. Enumerable.Range(activityCompleteCountBefore + 1, Math.Max(0, activityCompleteCountAfter - activityCompleteCountBefore))
            .Select(completeCount => BuildCount(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdActivityReward, completeCount)),
    ];

    public static GamePacket BuildCount(PB_ENUM_RES_RED_DOT_ID redDotID, int redDotBadgeNum)
    {
        var (msgID, body) = LobbyNotices.BuildRedDotCountNtf(redDotID, redDotBadgeNum);
        return GamePacket.Create(msgID, body);
    }

    private static GamePacket BuildClear(PB_ENUM_RES_RED_DOT_ID redDotID) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, LobbyNotices.BuildRedDotClearNtf(redDotID));

    private static int CountComplete(IEnumerable<pbCOMDT_TASK_INFO> tasks) =>
        tasks.Count(task => task.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);
}
