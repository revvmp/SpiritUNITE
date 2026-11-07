using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Game.Guild;

public sealed record GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE Result, int RewardID);

public sealed class GuildActivityManager(
    GuildRepository repository,
    GameData gameData,
    BagManager bag,
    LobbyRoles lobbyRoles,
    TimeProvider clock,
    ILogger<GuildActivityManager> logger)
{
    private const int NoRewardID = 0;
    private const int Rewarded = 1;
    private const int NoMemberScore = 0;

    private readonly SemaphoreSlim gate = new(1, 1);

    public GuildActivityPeriod CurrentPeriod() =>
        GuildActivityPeriod.Find(gameData, UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow()));

    public async Task<pbCOMDT_GUILD_ACTIVITY_BRIEF_INFO> BuildBriefInfoAsync(ulong guildID)
    {
        var period = CurrentPeriod();
        var activity = await LoadActivityAsync(guildID, period);
        return new pbCOMDT_GUILD_ACTIVITY_BRIEF_INFO
        {
            PeriodID = period.PeriodID,
            RewardID = activity?.RewardID ?? NoRewardID,
            States = period.States,
            GuildScore = activity?.GuildScore ?? 0,
        };
    }

    public async Task<pbCOMDT_GUILD_ACTIVITY_INFO> BuildActivityInfoAsync(ulong uid, ulong guildID)
    {
        var period = CurrentPeriod();
        var activity = await LoadActivityAsync(guildID, period);
        var rewardID = activity?.RewardID ?? NoRewardID;
        var claimedGears = period.PeriodID == 0 ? [] : await repository.GetActivityRewardGearsAsync(uid, period.PeriodID);
        return new pbCOMDT_GUILD_ACTIVITY_INFO
        {
            PeriodID = period.PeriodID,
            Score = NoMemberScore,
            RewardID = rewardID,
            States = period.States,
            ScoreRewardList =
            {
                claimedGears.Select(gear => new pbCOMDT_GUILD_SCORE_REWARD_INFO
                {
                    RewardGear = gear,
                    IsRewarded = Rewarded,
                    DropID = gameData.GuildActivityScoreReward.Find(pbGuildActivity_ScoreReward.Key(rewardID, gear))?.SDropID ?? 0,
                }),
            },
        };
    }

    public async Task<int> FindGuildScoreAsync(ulong guildID)
    {
        var period = CurrentPeriod();
        return period.PeriodID == 0 ? 0 : (await repository.FindActivityAsync(guildID, period.PeriodID))?.GuildScore ?? 0;
    }

    public async Task<GuildActivityRewardChange> SelectRewardAsync(GuildMember member, int rewardID)
    {
        var period = CurrentPeriod();
        if (member.Office != GuildRules.MasterOffice)
        {
            return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrGuildPermissionDeniedNotMaster, NoRewardID);
        }

        if (!period.IsPrepare)
        {
            return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, NoRewardID);
        }

        if (!period.RewardPool(gameData).Contains(rewardID))
        {
            return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, NoRewardID);
        }

        await gate.WaitAsync();
        try
        {
            await repository.SaveActivityRewardAsync(member.GuildID, period.PeriodID, rewardID);
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Guild {GuildID} master {Uid} selected activity reward {RewardID} for period {PeriodID}",
            member.GuildID, member.GameUlongUID, rewardID, period.PeriodID);
        await PushToOtherMembersAsync(member, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidSelectGuildRewardNtf, new pbSCSelectGuildRewardNtf
        {
            ResultInfo = new pbResultInfo(),
            RewardID = rewardID,
        }));
        return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, rewardID);
    }

    public async Task<GuildActivityRewardChange> CancelRewardAsync(GuildMember member, int rewardID)
    {
        var period = CurrentPeriod();
        if (member.Office != GuildRules.MasterOffice)
        {
            return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrGuildPermissionDeniedNotMaster, rewardID);
        }

        if (!period.IsPrepare)
        {
            return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, rewardID);
        }

        await gate.WaitAsync();
        try
        {
            var activity = await repository.FindActivityAsync(member.GuildID, period.PeriodID);
            if (activity is null || activity.RewardID == NoRewardID)
            {
                return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, rewardID);
            }

            if (activity.RewardID != rewardID)
            {
                return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, rewardID);
            }

            await repository.SaveActivityRewardAsync(member.GuildID, period.PeriodID, NoRewardID);
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Guild {GuildID} master {Uid} cancelled activity reward {RewardID} for period {PeriodID}",
            member.GuildID, member.GameUlongUID, rewardID, period.PeriodID);
        await PushToOtherMembersAsync(member, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidCancelGuildRewardNtf, new pbSCCancelGuildRewardNtf
        {
            ResultInfo = new pbResultInfo(),
            RewardID = rewardID,
        }));
        return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, rewardID);
    }

    public async Task<GuildRewardClaim> GetScoreRewardAsync(LMNRoleInfo role, GuildMember member, int rewardGear)
    {
        var period = CurrentPeriod();
        if (gameData.GuildActivityScoreGear.Find(rewardGear) is not { } gear)
        {
            return GuildRewardClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        if (!period.IsStarting)
        {
            return GuildRewardClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var activity = await LoadActivityAsync(member.GuildID, period);
        if (activity is null
            || activity.GuildScore < gear.Score
            || gameData.GuildActivityScoreReward.Find(pbGuildActivity_ScoreReward.Key(activity.RewardID, rewardGear)) is not { } scoreReward)
        {
            return GuildRewardClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var drops = gameData.DropStatic.Rows
            .Where(drop => drop.SDropID == scoreReward.SDropID)
            .OrderBy(drop => drop.SDropSeq)
            .ToArray();
        var uid = member.GameUlongUID;
        var rewardTime = clock.GetUtcNow().ToUnixTimeSeconds();
        if (drops.Length == 0 || GuildRewardGrant.Plan(gameData, bag, role, drops, rewardTime) is not { } grant)
        {
            logger.LogWarning("Guild activity drop {SDropID} for gear {RewardGear} has a reward with no grant path", scoreReward.SDropID, rewardGear);
            return GuildRewardClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var commit = await repository.CommitActivityGearRewardAsync(new RoleGuildActivityReward
        {
            GameUlongUID = uid,
            PeriodID = period.PeriodID,
            RewardGear = rewardGear,
            RewardTime = rewardTime,
        }, grant);
        if (commit.Wallet is null)
        {
            return GuildRewardClaim.Refused(commit.Result);
        }

        GrantItemsInMemory(role, grant);
        logger.LogInformation("Uid {Uid} claimed guild activity gear {RewardGear} of period {PeriodID}: {GrantedCount} rewards granted",
            uid, rewardGear, period.PeriodID, grant.Awards.Count);
        return GuildRewardClaim.Granted(grant, commit);
    }

    public static void GrantItemsInMemory(LMNRoleInfo role, GuildRewardGrant grant)
    {
        foreach (var item in grant.Items)
        {
            role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
        }
    }

    public async Task<GuildActivity?> FindStartedActivityAsync(ulong guildID, GuildActivityPeriod period) =>
        period.IsStarting ? await LoadActivityAsync(guildID, period) : null;

    private async Task<GuildActivity?> LoadActivityAsync(ulong guildID, GuildActivityPeriod period)
    {
        if (period.PeriodID == 0)
        {
            return null;
        }

        var activity = await repository.FindActivityAsync(guildID, period.PeriodID);
        if (!period.IsStarting || activity?.RewardID > NoRewardID)
        {
            return activity;
        }

        var pool = period.RewardPool(gameData);
        if (pool.Length == 0)
        {
            return activity;
        }

        await gate.WaitAsync();
        try
        {
            activity = await repository.FindActivityAsync(guildID, period.PeriodID);
            if (activity?.RewardID > NoRewardID)
            {
                return activity;
            }

            var rewardID = pool[Random.Shared.Next(pool.Length)];
            await repository.SaveActivityRewardAsync(guildID, period.PeriodID, rewardID);
            logger.LogInformation("Guild {GuildID} had no activity reward at period {PeriodID} start; drew reward {RewardID}",
                guildID, period.PeriodID, rewardID);
            return await repository.FindActivityAsync(guildID, period.PeriodID);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task PushToOtherMembersAsync(GuildMember member, GamePacket packet)
    {
        foreach (var other in await repository.GetMembersAsync(member.GuildID))
        {
            if (other.GameUlongUID != member.GameUlongUID)
            {
                await lobbyRoles.PushAsync(other.GameUlongUID, packet);
            }
        }
    }
}
