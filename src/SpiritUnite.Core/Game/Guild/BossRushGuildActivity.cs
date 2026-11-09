using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.Guild;

public sealed class BossRushGuildActivity(
    GameData gameData,
    GuildRepository repository,
    TimeProvider clock,
    ILogger<BossRushGuildActivity> logger)
{
    private const int NoPeriodID = 0;
    private const int UnrecordedSquadScore = 0;

    public pbBossRushActivityBase? FindPeriod()
    {
        var now = UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());
        return gameData.BossRushActivityBase.Rows.FirstOrDefault(period => period.StartTime <= now && now <= period.DisplayEndTime);
    }

    public pbSCBossRushPeriodNtf BuildPeriodNtf() => new() { PeriodID = FindPeriod()?.PeriodID ?? NoPeriodID };

    public pbSCQueryBossRushPassAwardRsp BuildPassAwardRsp() => new()
    {
        Result = new pbResultInfo(),
        AwardInfo = new pbCOMDT_BOSS_RUSH_PASS_AWARD_INFO { PeriodID = FindPeriod()?.PeriodID ?? NoPeriodID },
    };

    public async Task<pbSCQueryBossRushGuildActivityInfoRsp> BuildGuildActivityInfoRspAsync(ulong uid) =>
        await repository.FindMemberAsync(uid) is null
            ? new pbSCQueryBossRushGuildActivityInfoRsp { ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist } }
            : new pbSCQueryBossRushGuildActivityInfoRsp { ResultInfo = new pbResultInfo(), GuildActivityInfo = new pbCOMDT_BOSS_RUSH_GUILD_ACTIVITY_INFO() };

    public async Task<PB_ENUM_RES_ERROR_CODE> ClaimScoreRewardAsync(ulong uid, int rewardGear)
    {
        if (FindPeriod() is not { } period)
        {
            return Refuse(uid, rewardGear, PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        if (await repository.FindMemberAsync(uid) is null)
        {
            return Refuse(uid, rewardGear, PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist);
        }

        if (gameData.BossRushGuildActivityScoreAward.Find(pbBossRushGuildActivityScoreAward.Key(rewardGear, period.RewardID)) is not { } gear)
        {
            return Refuse(uid, rewardGear, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        logger.LogInformation(
            "Boss Rush squad score reward gear {RewardGear} needs squad score {Score}; uid {Uid} has squad score {SquadScore}",
            rewardGear, gear.Score, uid, UnrecordedSquadScore);
        return Refuse(uid, rewardGear, PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
    }

    private PB_ENUM_RES_ERROR_CODE Refuse(ulong uid, int rewardGear, PB_ENUM_RES_ERROR_CODE result)
    {
        logger.LogInformation("Boss Rush squad score reward gear {RewardGear} refused for uid {Uid}: {Result}", rewardGear, uid, result);
        return result;
    }
}
