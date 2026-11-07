using SpiritUNITE.Databin;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Ranked;

public sealed record RankMedalPlayer(int ObjID, int CampClient, pbCOMDT_POKEMON_BATTLE_STATISTIC Statistic);

public sealed class RankPointMedals
{
    private static readonly Dictionary<PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE, Func<pbCOMDT_POKEMON_BATTLE_STATISTIC, long>> StatOfFirstMedalType = new()
    {
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeDefenceNo1] = statistic => statistic.BaseStatistic?.DamageTank ?? 0,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeAssistNo1] = statistic => statistic.BaseStatistic?.AssistCnt ?? 0,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeKillNo1] = statistic => statistic.BaseStatistic?.KillCnt ?? 0,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeDamageNo1] = statistic => statistic.BaseStatistic?.Damage ?? 0,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeTreatNo1] = statistic => statistic.BaseStatistic?.Cure ?? 0,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeFruitNo1] = statistic => statistic.EatFruitCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeHitVitalNo1] = statistic => statistic.HitVitalCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeMakesleepNo1] = statistic => statistic.MakeSleepCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeMakepalsyNo1] = statistic => statistic.MakePalsyCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeMakejamNo1] = statistic => statistic.MakeJamCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeMakepoisonNo1] = statistic => statistic.MakePoisonCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeMakefrozenNo1] = statistic => statistic.MakeFrozenCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeMakeburnNo1] = statistic => statistic.MakeBurnCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeMonsterNo1] = statistic =>
            statistic.KillMonster?.SelfKilltMonsterDetail.Sum(monster => (long)monster.KillCnt) ?? 0,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeJumpNo1] = statistic =>
            statistic.BaseStatistic?.SceneInteractionInfo?.SpringBoardUseCnt ?? 0,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeFindNo1] = statistic => statistic.EnemyDetectedCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeWeakenNo1] = statistic => statistic.WeakenEmemyCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeMoveNo1] = statistic => statistic.MoveTotalDistance,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeUskillNo1] = statistic => statistic.DamageToHeroUseUSkill,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeExpNo1] = statistic => statistic.TotalExp,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeEnergyNo1] = statistic => statistic.TotalCarryScore,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeGoodsNo1] = statistic => statistic.SummonerSkillUseCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeNormalattackNo1] = statistic => statistic.NormalAttackHitAnyEememyUseCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeSkillattackNo1] = statistic => statistic.SkillHitAnyEememyUseCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeIngrassNo1] = statistic => statistic.InGrassTotalFrameNum,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeIngainNo1] = statistic => statistic.InGainScoreAreaTotalFrameNum,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeStealenergyNo1] = statistic => statistic.BaseStatistic?.WrestScore ?? 0,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeShieldNo1] = statistic => statistic.GetTotalShieldValue,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeConfusionNo1] = statistic => statistic.ConfusionEnemyCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeWithteamNo1] = statistic => statistic.WithTeammateTimeLen,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeKillrigiNo1] = statistic => statistic.KillRegiCnt,
        [PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE.PbEnmRankmatchPointMaskTypeInbattleNo1] = statistic => statistic.InBattleStateFrameNum,
    };

    private readonly IReadOnlyList<(Func<pbCOMDT_POKEMON_BATTLE_STATISTIC, long> Stat, pbRankMatchPointMedal[] Ranks)> medalGroups;
    private readonly int pointMedalMaxNum;

    public RankPointMedals(GameData gameData)
    {
        medalGroups =
        [
            .. gameData.RankMatchPointMedal.Rows
                .GroupBy(medal => medal.MedalGroup)
                .Select(group => group.OrderBy(medal => medal.Priority).ThenBy(medal => medal.MedalID).ToArray())
                .Where(ranks => StatOfFirstMedalType.ContainsKey((PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE)ranks[0].MedalType))
                .Select(ranks => (StatOfFirstMedalType[(PB_ENUM_ENM_RANKMATCH_POINT_MASK_TYPE)ranks[0].MedalType], ranks)),
        ];
        pointMedalMaxNum = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalRankedDivisionPointmedalMaxNum);
    }

    public pbCOMDT_SETTLE_MEDAlPOINT_INFO[] Award(IReadOnlyList<RankMedalPlayer> players, int objID)
    {
        if (players.FirstOrDefault(player => player.ObjID == objID) is not { } self)
        {
            return [];
        }

        RankMedalPlayer[] team = [.. players.Where(player => player.CampClient == self.CampClient)];
        var earned = new List<pbRankMatchPointMedal>();
        foreach (var (stat, ranks) in medalGroups)
        {
            var value = stat(self.Statistic);
            if (value <= 0)
            {
                continue;
            }

            var teamRank = team.Count(player => player.ObjID != objID && stat(player.Statistic) > value);
            if (ranks.ElementAtOrDefault(teamRank) is { } medal)
            {
                earned.Add(medal);
            }
        }

        return
        [
            .. earned
                .OrderByDescending(medal => medal.Score)
                .ThenByDescending(medal => medal.MedalID)
                .Take(pointMedalMaxNum)
                .Select(medal => new pbCOMDT_SETTLE_MEDAlPOINT_INFO { MedalID = medal.MedalID, Score = medal.Score }),
        ];
    }
}
