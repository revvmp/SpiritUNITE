using SpiritUnite.Game.Pokemon;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Profile;

public sealed record ProfileStatistics(pbCOMDT_STATISTIC_INFO StatisticInfo, pbCOMDT_STATISTIC_INFO SeasonStatisticInfo)
{
    public static void Record(pbCOMDT_STATISTIC_INFO statistics, UsedPokemonBattle battle, pbCOMDT_FIGHT_HISTORY_RECORD record, ulong uid)
    {
        statistics.TotalBattleCnt = Add(statistics.TotalBattleCnt, 1);
        statistics.TotalWinCnt = Add(statistics.TotalWinCnt, battle.Won ? 1 : 0);
        statistics.TotalMVPCnt = Add(statistics.TotalMVPCnt, battle.IsMvp ? 1 : 0);
        var mode = statistics.MapModeInfo.FirstOrDefault(info => info.MapMode == record.MapMode && info.SubMapMode == record.MapSubMode);
        if (mode is null)
        {
            mode = new pbCOMDT_MAP_MODE_STATISTIC_INFO { MapMode = record.MapMode, SubMapMode = record.MapSubMode };
            statistics.MapModeInfo.Add(mode);
        }

        var player = record.PlayerInfo.FirstOrDefault(info => info.PlayerUid == uid);
        var caught = player?.PlayerFightData?.CatchFightData;
        mode.TotalBattleCnt = Add(mode.TotalBattleCnt, 1);
        mode.TotalWinCnt = Add(mode.TotalWinCnt, battle.Won ? 1 : 0);
        mode.TotalMVPCnt = Add(mode.TotalMVPCnt, battle.IsMvp ? 1 : 0);
        mode.TotalScore = Add(mode.TotalScore, Math.Max(0, caught?.Score ?? 0));
        mode.TotalKill = Add(mode.TotalKill, Math.Max(0, caught?.Kill ?? 0));
        mode.ContinuousWin = battle.Won ? Add(mode.ContinuousWin, 1) : 0;
        mode.ContinuousLose = battle.Won ? 0 : Add(mode.ContinuousLose, 1);
        var now = battle.BattleTime * 1000;
        if (mode.ContinuousWin > mode.MaxContinuousWin)
        {
            mode.MaxContinuousWin = mode.ContinuousWin;
            mode.MaxConWinLastUpdTime = now;
        }

        mode.TotalScoreLastUpdTime = now;
    }

    private static int Add(int value, int amount) => (int)Math.Min(int.MaxValue, (long)value + amount);
}
