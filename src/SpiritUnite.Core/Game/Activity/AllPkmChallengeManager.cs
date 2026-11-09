using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Activity;

public sealed class AllPkmChallengeManager(
    ActivitySchedule schedule,
    ActivityManager activityManager,
    AllPkmChallengeRepository repository,
    ActivityRepository activityRepository,
    MailManager mail,
    TimeProvider clock,
    ILogger<AllPkmChallengeManager> logger)
{
    private const int Challenged = 1;
    private const int NoPokemon = 0;
    private const int OneWin = 1;
    private const int MaxPkmCount = (int)PB_MACRO_COM_MAX_ALL_PKM_CHALLENGE_NUM.PbComMaxAllPkmChallengeNum;
    private const int UnclaimedRewardMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeAllPkmChallengeAward;
    private const string UnclaimedRewardMailSubject = "OpsEvents_Event_Email_Subject_10351_001";
    private const string UnclaimedRewardMailContent = "OpsEvents_Event_Email_Content_10352_001";

    public async Task MailUnclaimedRewardsAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        var now = clock.GetUtcNow();
        foreach (var activity in schedule.FindEnded(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeAllPkmChallenge, now))
        {
            if ((await repository.FindAsync(uid, activity.Row.ActivityID)).Length == 0)
            {
                continue;
            }

            var unclaimed = await activityManager.FindUnclaimedTasksAtEndAsync(role, activity);
            if (unclaimed.Length == 0)
            {
                continue;
            }

            var rewardTime = now.ToUnixTimeSeconds();
            var mails = mail.BuildDropAttachments(unclaimed.Select(task => task.SDropID))
                .Chunk(MailBox.MaxMailAccess)
                .Select(chunk => mail.CreateSystemMail(uid, new SystemMail(
                    Subject: UnclaimedRewardMailSubject,
                    Content: UnclaimedRewardMailContent,
                    Attachments: chunk,
                    MailID: UnclaimedRewardMailID)))
                .ToArray();
            ActivityTaskReward[] rewards = [.. unclaimed.Select(task => new ActivityTaskReward(task.TaskId, task.PeriodStart, rewardTime))];
            if (await activityRepository.CommitUnclaimedTaskMailAsync(uid, rewards, mails))
            {
                logger.LogInformation(
                    "Uid {Uid} was mailed {TaskCount} unclaimed All-Pokemon Challenge {ActivityID} task rewards",
                    uid, unclaimed.Length, activity.Row.ActivityID);
            }
        }
    }

    public async Task<pbSCAllPkmChallengeStatusRsp> BuildStatusRspAsync(ulong uid)
    {
        if (FindOpenActivity() is not { } activity)
        {
            logger.LogInformation("All-Pokemon Challenge status refused for uid {Uid}: no challenge is open", uid);
            return new pbSCAllPkmChallengeStatusRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrAllPkmChallengeActivityNotExit },
            };
        }

        var listed = await repository.FindAsync(uid, activity.Row.ActivityID);
        return new pbSCAllPkmChallengeStatusRsp
        {
            ResultInfo = new pbResultInfo(),
            PkmChallengedNum = listed.Length,
            PkmChallengeList = { listed.Select(ToDetail) },
        };
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> PlayAsync(ulong uid, IReadOnlyCollection<int> pkmPlayList)
    {
        if (FindOpenActivity() is not { } activity)
        {
            logger.LogInformation("All-Pokemon Challenge play refused for uid {Uid}: no challenge is open", uid);
            return PB_ENUM_RES_ERROR_CODE.PbErrAllPkmChallengeActivityNotExit;
        }

        var listedIds = (await repository.FindAsync(uid, activity.Row.ActivityID)).Select(pkm => pkm.PkmID).ToHashSet();
        int[] played = [.. pkmPlayList.Where(listedIds.Contains).Distinct()];
        if (played.Length > 0)
        {
            await repository.MarkPlayedAsync(uid, activity.Row.ActivityID, played);
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<GamePacket[]> RecordBattleAsync(LMNRoleInfo role, bool won, int pokemonId)
    {
        if (!won || pokemonId == NoPokemon || FindOpenActivity() is not { } activity)
        {
            return [];
        }

        var groupId = activity.Row.ActivityParam;
        var increments = activityManager.PlanTaskGroupIncrements(PB_ENUM_RES_CONDITION_ID.PbResConditionIdUseSpecPokemonWin, groupId, OneWin);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (await repository.RecordWinAsync(role.GameUlongUID, activity.Row.ActivityID, pokemonId, MaxPkmCount, increments, now) is not { } listed)
        {
            return [];
        }

        logger.LogInformation(
            "All-Pokemon Challenge {ActivityID} recorded a win with Pokemon {PokemonId} for uid {Uid}: {Count} Pokemon",
            activity.Row.ActivityID, pokemonId, role.GameUlongUID, listed.Length);
        return
        [
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAllPkmChallengeStatusNtf, new pbSCAllPkmChallengeStatusNtf
            {
                ResultInfo = new pbResultInfo(),
                PkmChallengedNum = listed.Length,
                PkmChallengeList = { listed.Select(ToDetail) },
            }),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, new pbSCUpdateTaskInfoNtf
            {
                TaskInfoList = { await activityManager.BuildTaskInfoListAsync(role, [.. increments.Select(increment => increment.TaskId)]) },
            }),
        ];
    }

    private static pbCOMDT_ALL_PKM_CHALLENGE_PKM_DETAIL ToDetail(AllPkmChallengePkm pkm) => new()
    {
        PkmID = pkm.PkmID,
        ChallengeStatus = Challenged,
        IsPlayed = pkm.IsPlayed,
    };

    private ScheduledActivity? FindOpenActivity() =>
        schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeAllPkmChallenge, clock.GetUtcNow());
}
