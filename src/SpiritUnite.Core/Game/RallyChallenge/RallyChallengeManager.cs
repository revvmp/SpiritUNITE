using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.Role;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.RallyChallenge;

public sealed record RallyChallengeOutcome(PB_ENUM_RES_ERROR_CODE Result, GamePacket[] BeforeRsp)
{
    public static RallyChallengeOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed class RallyChallengeManager(
    GameData gameData,
    RallyChallengeRepository repository,
    ActivityManager activityManager,
    ActivityRewardNotices rewardNotices,
    MailManager mail,
    LobbyRoles lobbyRoles,
    TimeProvider clock,
    ILogger<RallyChallengeManager> logger)
{
    private const int QuickBattleMapMode = (int)PB_ENUM_RES_MATCHMAPMODE.PbMatchMapModeRace;
    private const int QuickBattleMapSubMode = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRelaxNormal;
    private const int QuickBattleGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeCatch;
    private const int Started = 1;
    private const int FirstRewardIndex = 1;
    private const int MaxBattleCount = (int)PB_MACRO_COM_MAX_UNITE_CHALLENGE_BATTLE_COUNT.PbComMaxUniteChallengeBattleCount;
    private const int MaxRewardCount = (int)PB_MACRO_COM_MAX_UNITE_CHALLENGE_REWARD_COUNT.PbComMaxUniteChallengeRewardCount;
    private const PB_ENUM_ENM_RESOURCE_CHG_REASON RewardReason = PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonUniteChallenge;
    private const int UnclaimedRewardMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeUniteChallengeAward;
    private const string UnclaimedRewardMailSubject = "OpsEvents_Email_Subject_11443_001";
    private const string UnclaimedRewardMailContent = "OpsEvents_Email_Content_11444_001";

    public static bool IsChallengeBattle(pbScene_Map? scene) =>
        scene is { MapMode: QuickBattleMapMode, MapSubMode: QuickBattleMapSubMode, GameType: QuickBattleGameType };

    public pbUniteChallenge? FindOpenConf()
    {
        var now = UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());
        return gameData.UniteChallenge.Rows.FirstOrDefault(conf => conf.OpenTime <= now && now < conf.CloseTime);
    }

    public DateTimeOffset? FindNextCloseTime(DateTimeOffset after) =>
        gameData.UniteChallenge.Rows
            .GroupBy(conf => conf.PeriodID)
            .Select(period => DateTimeOffset.FromUnixTimeSeconds(FindPeriodEndTime(period.First())))
            .Where(periodEnd => periodEnd > after)
            .Order()
            .Cast<DateTimeOffset?>()
            .FirstOrDefault();

    public async Task PushCloseNtfAsync()
    {
        var onlineUids = lobbyRoles.OnlineUids;
        foreach (var uid in onlineUids)
        {
            await lobbyRoles.PushAsync(uid, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUniteChallengeCloseNtf, new pbSCUniteChallengeCloseNtf()));
            await MailUnclaimedRewardsAsync(uid, pushMailNew: true);
        }

        logger.LogInformation("Unite Challenge close pushed to {OnlineCount} online roles", onlineUids.Count);
    }

    public async Task MailUnclaimedRewardsAsync(ulong uid, bool pushMailNew)
    {
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        foreach (var periodId in await repository.FindClaimablePeriodsAsync(uid))
        {
            if (FindLastConf(periodId) is not { } conf || FindPeriodEndTime(conf) > now)
            {
                continue;
            }

            var rewardIndexes = await repository.FindClaimableRewardIndexesAsync(uid, periodId);
            var attachments = mail.BuildDropAttachments(rewardIndexes
                .Where(rewardIndex => rewardIndex >= FirstRewardIndex && rewardIndex <= conf.WinReward.Length)
                .Select(rewardIndex => conf.WinReward[rewardIndex - 1].DropId));
            var mails = attachments.Chunk(MailBox.MaxMailAccess)
                .Select(chunk => mail.CreateSystemMail(uid, new SystemMail(
                    Subject: UnclaimedRewardMailSubject,
                    Content: UnclaimedRewardMailContent,
                    Attachments: chunk,
                    MailID: UnclaimedRewardMailID)))
                .ToArray();
            if (!await repository.CommitUnclaimedRewardMailAsync(uid, periodId, rewardIndexes, mails, now))
            {
                continue;
            }

            logger.LogInformation("Uid {Uid} was mailed {RewardCount} unclaimed Unite Challenge rewards of period {PeriodID}",
                uid, rewardIndexes.Length, periodId);
            if (pushMailNew)
            {
                await mail.PushNewMailsAsync(mails);
            }
        }
    }

    public async Task FillAcntLoginRspAsync(ulong uid, pbSCAcntLoginRsp rsp)
    {
        if (FindOpenConf() is not { } conf)
        {
            return;
        }

        var state = await repository.FindAsync(uid, conf.PeriodID);
        rsp.IsUniteChallengeStart = Started;
        rsp.IsUniteChallengeClickStart = state.IsClickStart;
        rsp.UniteChallengeBattleInfo = BuildBattleInfo(state);
    }

    public async Task<pbSCGetUniteChallengeInfoRsp> BuildInfoRspAsync(ulong uid)
    {
        if (FindOpenConf() is not { } conf)
        {
            logger.LogInformation("Unite Challenge info refused for uid {Uid}: no challenge is open", uid);
            return new pbSCGetUniteChallengeInfoRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeNotOpen) };
        }

        var state = await repository.FindAsync(uid, conf.PeriodID);
        return new pbSCGetUniteChallengeInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            BattleInfo = BuildBattleInfo(state),
            RewardInfo = BuildRewardInfo(state),
            ConfId = conf.ID,
            IsClickStart = state.IsClickStart,
            PeriodEndTime = FindPeriodEndTime(conf),
        };
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> StartAsync(ulong uid)
    {
        if (FindOpenConf() is not { } conf)
        {
            return Refuse("start", uid, PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeNotOpen);
        }

        if (!await repository.StartAsync(uid, conf.PeriodID, clock.GetUtcNow().ToUnixTimeSeconds()))
        {
            return Refuse("start", uid, PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeRepeatedStart);
        }

        logger.LogInformation("Unite Challenge period {PeriodID} started by uid {Uid}", conf.PeriodID, uid);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ResetAsync(ulong uid)
    {
        if (FindOpenConf() is not { } conf)
        {
            return Refuse("reset", uid, PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeNotOpen);
        }

        var state = await repository.FindAsync(uid, conf.PeriodID);
        if (state.IsClickStart != Started)
        {
            return Refuse("reset", uid, PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeNotOpen);
        }

        if (!await repository.ResetAsync(uid, conf.PeriodID))
        {
            return Refuse("reset", uid, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        logger.LogInformation("Unite Challenge period {PeriodID} round reset by uid {Uid}", conf.PeriodID, uid);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<RallyChallengeOutcome> ClaimRewardAsync(LMNRoleInfo role, int rewardIndex)
    {
        var uid = role.GameUlongUID;
        if (FindOpenConf() is not { } conf)
        {
            return RallyChallengeOutcome.Refused(Refuse("reward", uid, PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeNotOpen));
        }

        var rewards = conf.WinReward;
        if (rewardIndex < FirstRewardIndex || rewardIndex > Math.Min(rewards.Length, MaxRewardCount))
        {
            return RallyChallengeOutcome.Refused(Refuse("reward", uid, PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeRewardNotReach));
        }

        var state = await repository.FindAsync(uid, conf.PeriodID);
        var status = rewardIndex <= state.RewardStatusList.Length ? state.RewardStatusList[rewardIndex - 1] : (int)PB_ENUM_ENM_UNITE_CHALLENGE_REWARD_STATUS.PbEnmUniteChallengeRewardStatusNotReach;
        if (status == (int)PB_ENUM_ENM_UNITE_CHALLENGE_REWARD_STATUS.PbEnmUniteChallengeRewardStatusReceived)
        {
            return RallyChallengeOutcome.Refused(Refuse("reward", uid, PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeRewardReceived));
        }

        if (status != (int)PB_ENUM_ENM_UNITE_CHALLENGE_REWARD_STATUS.PbEnmUniteChallengeRewardStatusCanReceive)
        {
            return RallyChallengeOutcome.Refused(Refuse("reward", uid, PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeRewardNotReach));
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var dropId = rewards[rewardIndex - 1].DropId;
        if (activityManager.PlanDropGrant(role, dropId, now) is not { } grant)
        {
            return RallyChallengeOutcome.Refused(Refuse("reward", uid, PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeConfNotExist));
        }

        var change = ActivityDropRewards.ToBagChange(grant, RewardReason, now, new BagChange());
        var commit = await repository.CommitRewardAsync(uid, conf.PeriodID, rewardIndex, change, now);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return RallyChallengeOutcome.Refused(Refuse("reward", uid, commit.Result));
        }

        ActivityManager.GrantToRole(role, grant, now, RewardReason);
        logger.LogInformation(
            "Unite Challenge reward {RewardIndex} of period {PeriodID} (drop {DropId}) claimed by uid {Uid}",
            rewardIndex, conf.PeriodID, dropId, uid);
        var rewardGroup = ActivityDropRewards.ToRewardGroup(grant, change, commit);
        return new RallyChallengeOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
        [
            .. await rewardNotices.BuildAsync(role, rewardGroup),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = (int)RewardReason,
                ResourcesList = { grant.Awards },
            }),
        ]);
    }

    public async Task<RallyChallengeSettlementPlan?> PrepareSettlementAsync(ulong uid, bool won, DateTimeOffset now)
    {
        var configTime = UTCTimeUtil.ServerTime2CfgTime(now);
        var conf = gameData.UniteChallenge.Rows.FirstOrDefault(row => row.OpenTime <= configTime && configTime < row.CloseTime);
        if (conf is null)
        {
            return null;
        }

        var state = await repository.FindAsync(uid, conf.PeriodID);
        return state.IsClickStart != Started || state.BattleStatusList.Length >= MaxBattleCount ? null
            : new RallyChallengeSettlementPlan(conf.PeriodID,
                (int)(won ? PB_ENUM_ENM_UNITE_CHALLENGE_BATTLE_STATUS.PbEnmUniteChallengeBattleStatusWin : PB_ENUM_ENM_UNITE_CHALLENGE_BATTLE_STATUS.PbEnmUniteChallengeBattleStatusLose),
                MaxBattleCount, Math.Min(conf.WinReward.Length, MaxRewardCount), state.RoundCnt);
    }

    public async Task<GamePacket[]> ApplySettlementAsync(SettlementKey key, RallyChallengeSettlementPlan plan)
    {
        var state = await repository.RecordBattleAsync(key.GameUlongUID, plan.PeriodID, plan.BattleStatus, plan.MaxBattleCount, plan.RewardCount, key, plan.RoundCnt);
        if (state is null)
        {
            logger.LogInformation(
                "Unite Challenge period {PeriodID} round {RoundCnt} of uid {Uid} took no battle from settlement {SettlementID}: the round was full, reset or not started",
                plan.PeriodID, plan.RoundCnt, key.GameUlongUID, key.SettlementID);
            return [];
        }

        var update = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUniteChallengeInfoUpdateNtf, new pbSCUniteChallengeInfoUpdateNtf
        {
            BattleInfo = BuildBattleInfo(state),
            RewardInfo = BuildRewardInfo(state),
        });
        return state.BattleStatusList.Length < plan.MaxBattleCount ? [update]
            : [update, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUniteChallengeRoundEndNtf, new pbSCUniteChallengeRoundEndNtf { LastBattleInfo = BuildBattleInfo(state) })];
    }

    public async Task<GamePacket[]> RecordBattleAsync(ulong uid, bool won)
    {
        if (FindOpenConf() is not { } conf)
        {
            return [];
        }

        var battleStatus = won
            ? PB_ENUM_ENM_UNITE_CHALLENGE_BATTLE_STATUS.PbEnmUniteChallengeBattleStatusWin
            : PB_ENUM_ENM_UNITE_CHALLENGE_BATTLE_STATUS.PbEnmUniteChallengeBattleStatusLose;
        var rewardCount = Math.Min(conf.WinReward.Length, MaxRewardCount);
        if (await repository.RecordBattleAsync(uid, conf.PeriodID, (int)battleStatus, MaxBattleCount, rewardCount) is not { } state)
        {
            return [];
        }

        logger.LogInformation(
            "Unite Challenge period {PeriodID} recorded battle {BattleCount} ({BattleStatus}) for uid {Uid}",
            conf.PeriodID, state.BattleStatusList.Length, battleStatus, uid);
        var update = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUniteChallengeInfoUpdateNtf, new pbSCUniteChallengeInfoUpdateNtf
        {
            BattleInfo = BuildBattleInfo(state),
            RewardInfo = BuildRewardInfo(state),
        });
        return state.BattleStatusList.Length < MaxBattleCount
            ? [update]
            :
            [
                update,
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUniteChallengeRoundEndNtf, new pbSCUniteChallengeRoundEndNtf
                {
                    LastBattleInfo = BuildBattleInfo(state),
                }),
            ];
    }

    private pbUniteChallenge? FindLastConf(int periodId) =>
        gameData.UniteChallenge.Rows.Where(row => row.PeriodID == periodId).MaxBy(row => row.CloseTime);

    private long FindPeriodEndTime(pbUniteChallenge conf) =>
        UTCTimeUtil.CfgTime2ServerUnixTime(gameData.UniteChallenge.Rows
            .Where(row => row.PeriodID == conf.PeriodID)
            .Max(row => row.CloseTime));

    private static pbCOMDT_UNITE_CHALLENGE_BATTLE_INFO BuildBattleInfo(UniteChallengeState state) =>
        new() { BattleStatusList = { state.BattleStatusList } };

    private static pbCOMDT_UNITE_CHALLENGE_REWARD_INFO BuildRewardInfo(UniteChallengeState state) =>
        new() { RewardStatusList = { state.RewardStatusList } };

    private static pbResultInfo Result(PB_ENUM_RES_ERROR_CODE result) => new() { ResultId = (int)result };

    private PB_ENUM_RES_ERROR_CODE Refuse(string action, ulong uid, PB_ENUM_RES_ERROR_CODE result)
    {
        logger.LogInformation("Unite Challenge {Action} refused for uid {Uid}: {Result}", action, uid, result);
        return result;
    }
}

public sealed record RallyChallengeSettlementPlan(int PeriodID, int BattleStatus, int MaxBattleCount, int RewardCount, int RoundCnt);
