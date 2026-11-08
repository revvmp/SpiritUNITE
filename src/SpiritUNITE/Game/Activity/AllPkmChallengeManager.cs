using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed class AllPkmChallengeManager(
    ActivitySchedule schedule,
    ActivityManager activityManager,
    AllPkmChallengeRepository repository,
    TimeProvider clock,
    ILogger<AllPkmChallengeManager> logger)
{
    private const int Challenged = 1;
    private const int NoPokemon = 0;
    private const int OneWin = 1;
    private const int MaxPkmCount = (int)PB_MACRO_COM_MAX_ALL_PKM_CHALLENGE_NUM.PbComMaxAllPkmChallengeNum;

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
