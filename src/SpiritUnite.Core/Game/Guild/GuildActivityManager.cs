using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.Role;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.Guild;

public sealed record GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE Result, int RewardID)
{
    public IReadOnlyList<GamePacket> RedDots { get; init; } = [];
}

public sealed class GuildActivityManager(
    GuildRepository repository,
    GameData gameData,
    BagManager bag,
    MailManager mail,
    LobbyRoles lobbyRoles,
    TimeProvider clock,
    ILogger<GuildActivityManager> logger)
{
    private const int NoRewardID = 0;
    private const int Rewarded = 1;
    private const int NoMemberScore = 0;
    private const int MasterSelectAwardRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdGuildMasterSelectAward;
    private const int MasterSelectAwardRedDotBadgeNum = 1;
    private const int UnclaimedGearMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeGuildActivity;
    private const string UnclaimedGearMailSubject = "Guild_Notice_Email_Subject_6172_001";
    private const string UnclaimedGearMailContent = "System_Email_Content_Reward_7346_001";

    private readonly SemaphoreSlim gate = new(1, 1);

    public GuildActivityPeriod CurrentPeriod() =>
        GuildActivityPeriod.Find(gameData, UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow()));

    public IReadOnlyList<GamePacket> BuildCreatedGuildRedDots() =>
        CurrentPeriod().IsPrepare ? [BuildMasterSelectAwardRedDot()] : [];

    private static GamePacket BuildMasterSelectAwardRedDot() =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = MasterSelectAwardRedDotID, RedDotBadgeNum = MasterSelectAwardRedDotBadgeNum } },
            },
        });

    private static GamePacket BuildMasterSelectAwardRedDotClear() =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = MasterSelectAwardRedDotID });

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
        return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, rewardID)
        {
            RedDots = [BuildMasterSelectAwardRedDotClear()],
        };
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
        return new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, rewardID)
        {
            RedDots = [BuildMasterSelectAwardRedDot()],
        };
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

    public Task PushUnclaimedGearMailsAsync(IReadOnlyList<RoleMail> mails) => mail.PushNewMailsAsync(mails);

    public async Task<IReadOnlyList<RoleMail>> MailUnclaimedGearsAsync(GuildMember member)
    {
        var mailed = new List<RoleMail>();
        var now = clock.GetUtcNow();
        var svrCfgTime = UTCTimeUtil.ServerTime2CfgTime(now);
        var endedPeriods = gameData.GuildActivityBase.Rows
            .Where(row => row.EndTime < svrCfgTime && member.JoinTime <= UTCTimeUtil.CfgTime2ServerUnixTime(row.EndTime))
            .Select(row => row.PeriodID)
            .ToArray();
        if (endedPeriods.Length == 0)
        {
            return mailed;
        }

        foreach (var activity in await repository.GetScoredActivitiesAsync(member.GuildID, endedPeriods))
        {
            var claimed = await repository.GetActivityRewardGearsAsync(member.GameUlongUID, activity.PeriodID);
            var gears = gameData.GuildActivityScoreGear.Rows
                .Where(gear => gear.Score <= activity.GuildScore && !claimed.Contains(gear.GearID))
                .Select(gear => (gear.GearID, Reward: gameData.GuildActivityScoreReward.Find(pbGuildActivity_ScoreReward.Key(activity.RewardID, gear.GearID))))
                .Where(gear => gear.Reward is not null)
                .OrderBy(gear => gear.GearID)
                .ToArray();
            if (gears.Length == 0)
            {
                continue;
            }

            var attachments = mail.BuildDropAttachments(gears.Select(gear => gear.Reward!.SDropID));
            var mails = attachments.Chunk(MailBox.MaxMailAccess)
                .Select(chunk => mail.CreateSystemMail(member.GameUlongUID, new SystemMail(
                    Subject: UnclaimedGearMailSubject,
                    Content: UnclaimedGearMailContent,
                    Attachments: chunk,
                    MailID: UnclaimedGearMailID)))
                .ToArray();
            var rewards = gears.Select(gear => new RoleGuildActivityReward
            {
                GameUlongUID = member.GameUlongUID,
                PeriodID = activity.PeriodID,
                RewardGear = gear.GearID,
                RewardTime = now.ToUnixTimeSeconds(),
            }).ToArray();
            if (!await repository.CommitUnclaimedGearMailAsync(rewards, mails))
            {
                continue;
            }

            logger.LogInformation("Uid {Uid} was mailed {GearCount} unclaimed gears of guild {GuildID} activity period {PeriodID}",
                member.GameUlongUID, gears.Length, member.GuildID, activity.PeriodID);
            mailed.AddRange(mails);
        }

        return mailed;
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
