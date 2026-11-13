using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Activity;

public sealed record ShowSerialRewardClaim(
    PB_ENUM_RES_ERROR_CODE Result,
    pbCOMDT_SHOW_SERIAL_REWARD_ACTIVITY_INFO ActivityInfo,
    ActivityRewardGroup? Rewards);

public sealed class ShowSerialRewardManager(
    GameData gameData,
    ActivitySchedule schedule,
    ActivityManager activityManager,
    ActivityRepository activityRepository,
    ShowSerialRewardRepository repository,
    RoleRepository roleRepository,
    MailManager mail,
    TimeProvider clock,
    ILogger<ShowSerialRewardManager> logger)
{
    private const int FirstRewardSeq = 1;
    private const int NoLoginDay = 0;
    private const int NotGet = 0;
    private const int CanGet = 1;
    private const int Rewarded = 2;
    private const int Enabled = 1;
    private const int UnclaimedRewardMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeShowSerialRewardActivityReward;
    private const string UnclaimedRewardMailSubject = "OpsEvents_Mail_Title_22569_001";
    private const string UnclaimedRewardMailContent = "OpsEvents_Mail_Desc_22570_001";
    private static readonly IReadOnlySet<int> NothingRewarded = new HashSet<int>();

    public async Task<pbSCShowSerialRewardActivityStatusNtf> BuildStatusNtfAsync(LMNRoleInfo role)
    {
        var now = clock.GetUtcNow();
        var list = new pbCSDT_SHOW_SERIAL_REWARD_ACTIVITY_LIST();
        foreach (var activity in FindOpenActivities(now))
        {
            list.ActivityInfo.Add(new pbCSDT_SHOW_SERIAL_REWARD_ACTIVITY_INFO
            {
                ActivityID = activity.Activity.Row.ActivityID,
                ActivityInfo = (await EvaluateAsync(role.GameUlongUID, activity, now)).ToActivityInfo(),
            });
        }

        return new pbSCShowSerialRewardActivityStatusNtf { ShowSerialRewardActivityList = list };
    }

    public async Task<pbSCActivityLoginShowNtf> BuildLoginShowNtfAsync(LMNRoleInfo role)
    {
        var now = clock.GetUtcNow();
        var loginShow = new pbSCActivityLoginShowNtf();
        foreach (var activity in FindOpenActivities(now))
        {
            if ((await EvaluateAsync(role.GameUlongUID, activity, now)).Statuses.ContainsValue(CanGet))
            {
                loginShow.LoginShowActivityList.Add(activity.Activity.Row.ActivityID);
            }
        }

        return loginShow;
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_SHOW_SERIAL_REWARD_ACTIVITY_INFO ActivityInfo)> BuildStatusAsync(LMNRoleInfo role, int activityID)
    {
        var now = clock.GetUtcNow();
        if (FindOpenActivity(activityID, now) is not { } activity)
        {
            logger.LogInformation("Serial reward status of activity {ActivityID} refused for uid {Uid}: not open", activityID, role.GameUlongUID);
            return (PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, EmptyActivityInfo());
        }

        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, (await EvaluateAsync(role.GameUlongUID, activity, now)).ToActivityInfo());
    }

    public async Task<ShowSerialRewardClaim> ClaimAsync(LMNRoleInfo role, int activityID, int rewardSeq)
    {
        var uid = role.GameUlongUID;
        var now = clock.GetUtcNow();
        if (FindOpenActivity(activityID, now) is not { } activity)
        {
            logger.LogInformation("Serial reward {RewardSeq} of activity {ActivityID} refused for uid {Uid}: not open", rewardSeq, activityID, uid);
            return new ShowSerialRewardClaim(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, EmptyActivityInfo(), null);
        }

        var before = await EvaluateAsync(uid, activity, now);
        var refusal = gameData.ShowSerialRewardActivity.Find(pbShowSerialRewardActivity.Key(activityID, rewardSeq)) is null
            ? PB_ENUM_RES_ERROR_CODE.PbErrActivityDataErr
            : before.Statuses.GetValueOrDefault(rewardSeq, NotGet) switch
            {
                CanGet => PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
                Rewarded => PB_ENUM_RES_ERROR_CODE.PbErrRewardAlreadyReceived,
                _ => PB_ENUM_RES_ERROR_CODE.PbErrRewardUnlock,
            };
        if (refusal != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Serial reward {RewardSeq} of activity {ActivityID} refused for uid {Uid}: {Result}", rewardSeq, activityID, uid, refusal);
            return new ShowSerialRewardClaim(refusal, before.ToActivityInfo(), null);
        }

        var dropID = gameData.ShowSerialRewardActivity.Find(pbShowSerialRewardActivity.Key(activityID, rewardSeq))!.DropID;
        var rewardTime = now.ToUnixTimeSeconds();
        if (activityManager.PlanDropGrant(role, dropID, rewardTime) is not { } grant)
        {
            logger.LogWarning("Serial reward {RewardSeq} of activity {ActivityID} refused for uid {Uid}: drop {DropID} has no grant path", rewardSeq, activityID, uid, dropID);
            return new ShowSerialRewardClaim(PB_ENUM_RES_ERROR_CODE.PbErrActivityDataErr, before.ToActivityInfo(), null);
        }

        var commit = await repository.CommitRewardAsync(uid, activityID, rewardSeq, new ActivityRewardChange(grant.Items, grant.PokemonIds, grant.Change, []), rewardTime);
        if (commit.Wallet is not { } wallet)
        {
            logger.LogInformation("Serial reward {RewardSeq} of activity {ActivityID} refused for uid {Uid}: {Result}", rewardSeq, activityID, uid, commit.Result);
            return new ShowSerialRewardClaim(commit.Result, (await EvaluateAsync(uid, activity, now)).ToActivityInfo(), null);
        }

        ActivityManager.GrantToRole(role, grant, rewardTime, PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonShowSerialRewardActivityReward);
        logger.LogInformation("Serial reward {RewardSeq} of activity {ActivityID} claimed by uid {Uid}: drop {DropID}", rewardSeq, activityID, uid, dropID);
        var rewards = new ActivityRewardGroup(
            wallet,
            [.. grant.Change.CoinGrants.Keys],
            commit.SpecialCoinsBefore,
            commit.ChangedItems,
            grant.PokemonIds,
            grant.Awards)
        {
            Items = grant.Items,
        };
        return new ShowSerialRewardClaim(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, (await EvaluateAsync(uid, activity, now)).ToActivityInfo(), rewards);
    }

    public async Task MailUnclaimedRewardsAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        var now = clock.GetUtcNow();
        SerialRewardActivity[] mailed =
        [
            .. schedule.FindEnded(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeShowSerialReward, now)
                .Select(FindConfigured)
                .OfType<SerialRewardActivity>()
                .Where(activity => activity.Config.ActivityFinishSendMail == Enabled),
        ];
        if (mailed.Length == 0)
        {
            return;
        }

        var registeredAt = DateTimeOffset.FromUnixTimeSeconds(await roleRepository.FindCreateTimeAsync(uid));
        SerialRewardActivity[] owing = [.. mailed.Where(activity => activity.Activity.ValidEnd > registeredAt)];
        if (owing.Length == 0)
        {
            return;
        }

        var firstDay = DateOnly.FromDateTime(owing.Min(activity => activity.Activity.ValidStart).UtcDateTime);
        var loginDays = await activityRepository.FindLoginDaysAsync(uid, firstDay);
        var rewardedByActivity = await repository.FindRewardedSeqsAsync(uid, [.. owing.Select(activity => activity.Activity.Row.ActivityID)]);
        foreach (var ended in owing)
        {
            var activityID = ended.Activity.Row.ActivityID;
            var atEnd = Evaluate(ended, ended.Activity.ValidEnd, loginDays, rewardedByActivity.GetValueOrDefault(activityID, NothingRewarded));
            int[] unclaimed = [.. atEnd.Statuses.Where(status => status.Value == CanGet).Select(status => status.Key).Order()];
            if (atEnd.LoginDayNum == NoLoginDay || unclaimed.Length == 0)
            {
                continue;
            }

            var mails = mail.BuildDropAttachments(unclaimed.Select(seq => gameData.ShowSerialRewardActivity.Find(pbShowSerialRewardActivity.Key(activityID, seq))!.DropID))
                .Chunk(MailBox.MaxMailAccess)
                .Select(chunk => mail.CreateSystemMail(uid, new SystemMail(
                    Subject: UnclaimedRewardMailSubject,
                    Content: UnclaimedRewardMailContent,
                    Attachments: chunk,
                    MailID: UnclaimedRewardMailID)))
                .ToArray();
            if (await repository.CommitUnclaimedMailAsync(uid, activityID, unclaimed, mails, now.ToUnixTimeSeconds()))
            {
                logger.LogInformation(
                    "Uid {Uid} was mailed {RewardCount} unclaimed serial rewards of activity {ActivityID}",
                    uid, unclaimed.Length, activityID);
            }
        }
    }

    private IEnumerable<SerialRewardActivity> FindOpenActivities(DateTimeOffset now) =>
        schedule.FindActive(now)
            .Where(activity => activity.Row.ActivityType == (int)PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeShowSerialReward)
            .Select(FindConfigured)
            .OfType<SerialRewardActivity>()
            .OrderBy(activity => activity.Activity.Row.ActivityID);

    private SerialRewardActivity? FindOpenActivity(int activityID, DateTimeOffset now) =>
        FindOpenActivities(now).FirstOrDefault(activity => activity.Activity.Row.ActivityID == activityID);

    private SerialRewardActivity? FindConfigured(ScheduledActivity activity)
    {
        if (gameData.ShowSerialRewardActivityConfig.Find(activity.Row.ActivityID) is not { } config)
        {
            return null;
        }

        var rewardCount = 0;
        while (gameData.ShowSerialRewardActivity.Contains(pbShowSerialRewardActivity.Key(activity.Row.ActivityID, rewardCount + FirstRewardSeq)))
        {
            rewardCount++;
        }

        return rewardCount > 0 ? new SerialRewardActivity(activity, config, rewardCount) : null;
    }

    private async Task<SerialRewardState> EvaluateAsync(ulong uid, SerialRewardActivity activity, DateTimeOffset now)
    {
        var firstDay = DateOnly.FromDateTime(activity.Activity.ValidStart.UtcDateTime);
        var loginDays = await activityRepository.FindLoginDaysAsync(uid, firstDay);
        var rewarded = await repository.FindRewardedSeqsAsync(uid, activity.Activity.Row.ActivityID);
        return Evaluate(activity, now, loginDays, rewarded);
    }

    private static SerialRewardState Evaluate(SerialRewardActivity activity, DateTimeOffset now, IReadOnlyCollection<DateOnly> loginDays, IReadOnlySet<int> rewarded)
    {
        var firstDay = DateOnly.FromDateTime(activity.Activity.ValidStart.UtcDateTime);
        var lastDay = DateOnly.FromDateTime((now < activity.Activity.ValidEnd ? now : activity.Activity.ValidEnd).UtcDateTime);
        var loginDayNum = loginDays.Count(day => day >= firstDay && day <= lastDay);
        var unlockedCount = loginDayNum == NoLoginDay
            ? NoLoginDay
            : Math.Min(activity.RewardCount, activity.Config.CrossDayType == (int)PB_ENUM_RES_ACTIVITY_CROSS_DAY_TYPE.PbResActivityCrossDayTypeStartTime
                ? lastDay.DayNumber - firstDay.DayNumber + 1
                : loginDayNum);
        var statuses = new SortedDictionary<int, int>();
        for (var seq = FirstRewardSeq; seq <= unlockedCount; seq++)
        {
            statuses[seq] = rewarded.Contains(seq) ? Rewarded
                : seq == unlockedCount || activity.Config.SkipRewardCanReceive == Enabled ? CanGet
                : NotGet;
        }

        return new SerialRewardState(loginDayNum, statuses);
    }

    private static pbCOMDT_SHOW_SERIAL_REWARD_ACTIVITY_INFO EmptyActivityInfo() => new() { RewardInfo = new pbCOMDT_SHOW_SERIAL_REWARD_ACTIVITY_REWARD_LIST() };

    private sealed record SerialRewardActivity(ScheduledActivity Activity, pbShowSerialRewardActivityConfig Config, int RewardCount);

    private sealed record SerialRewardState(int LoginDayNum, SortedDictionary<int, int> Statuses)
    {
        public pbCOMDT_SHOW_SERIAL_REWARD_ACTIVITY_INFO ToActivityInfo() => new()
        {
            RewardInfo = new pbCOMDT_SHOW_SERIAL_REWARD_ACTIVITY_REWARD_LIST
            {
                RewardList =
                {
                    Statuses.Select(status => new pbCOMDT_SHOW_SERIAL_REWARD_ACTIVITY_REWARD_INFO { RewardSeq = status.Key, RewardStatus = status.Value }),
                },
            },
            LoginDayNum = LoginDayNum,
        };
    }
}
