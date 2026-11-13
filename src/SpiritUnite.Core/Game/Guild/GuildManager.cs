using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Role;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using GuildEntity = SpiritUnite.Database.Models.Guild;

namespace SpiritUnite.Game.Guild;

public sealed record GuildMembership(GuildEntity Guild, GuildMember Member);

public sealed record GuildOutcome<TResponse>(TResponse Response, IReadOnlyList<GamePacket> Notices)
{
    public IReadOnlyList<GamePacket> Leading { get; init; } = [];

    public static implicit operator GuildOutcome<TResponse>(TResponse response) => new(response, []);
}

public sealed class GuildManager(
    GuildRepository repository,
    GuildInfoBuilder builder,
    GuildComments comments,
    GuildActivityManager activities,
    GuildActivityTasks activityTasks,
    RoleInfoStore roles,
    GameData gameData,
    LobbyRoles lobbyRoles,
    TimeProvider clock,
    ILogger<GuildManager> logger)
{
    private const int HasGuild = 1;
    private const int NoGuild = 0;
    private const int All = 1;
    private const int AllSucceeded = 1;
    private const int NotAllSucceeded = 0;
    private const int InGuild = 1;
    private const int NotInGuild = 0;
    private const int NewGuildJoinRequestRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewGuildJoinRequest;

    private static readonly PB_ENUM_RES_RED_DOT_ID[] RedDotsClearedOnQuit =
    [
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewGuildJoinRequest,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdGuildComment,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdGuildMasterSelectAward,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdGuildScoreRewardUnlock,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdGuildActivityDailyFresh,
        PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdGuildActivityTeam,
    ];

    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<GuildMembership?> FindMembershipAsync(ulong uid)
    {
        if (await repository.FindMemberAsync(uid) is not { } member)
        {
            return null;
        }

        if (await repository.FindGuildAsync(member.GuildID) is not { } guild)
        {
            await repository.RemoveMemberAsync(uid);
            return null;
        }

        return new GuildMembership(guild, member);
    }

    public async Task<pbCOMDT_ACNT_GUILD_INFO> BuildAcntGuildInfoAsync(ulong uid)
    {
        if (await FindMembershipAsync(uid) is not { } membership)
        {
            return GuildInfoBuilder.BuildAcntGuildInfo(null, null);
        }

        await activities.MailUnclaimedGearsAsync(membership.Member);
        return GuildInfoBuilder.BuildAcntGuildInfo(membership.Guild, membership.Member);
    }

    public async Task<pbCOMDT_ACNT_GUILD_INFO> FindAcntGuildInfoAsync(ulong uid) =>
        await FindMembershipAsync(uid) is { } membership
            ? GuildInfoBuilder.BuildAcntGuildInfo(membership.Guild, membership.Member)
            : GuildInfoBuilder.BuildAcntGuildInfo(null, null);

    public async Task<(string GuildName, int GuildOffice)> FindProfileGuildAsync(ulong uid) =>
        await FindMembershipAsync(uid) is { } membership
            ? (membership.Guild.GuildName, membership.Member.Office)
            : ("", (int)PB_ENUM_ENM_GUILD_OFFICE.PbEnmGuildOfficeNone);

    public async Task<GuildOutcome<pbSCGuildCreateRsp>> CreateAsync(LMNRoleInfo role, pbCOMDT_CREATE_GUILD_REQ_INFO? createInfo)
    {
        var uid = role.GameUlongUID;
        var refused = new pbSCGuildCreateRsp { ResultInfo = new pbResultInfo() };
        if (createInfo is null)
        {
            refused.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            return refused;
        }

        var labels = createInfo.LabelList.ToArray();
        var result = ValidateSetting(createInfo.Notice, labels, createInfo.JoinType);
        if (!GuildRules.IsValidName(createInfo.GuildName))
        {
            result = PB_ENUM_RES_ERROR_CODE.PbErrGuildNameIllegal;
        }

        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            refused.ResultInfo.ResultId = (int)result;
            return refused;
        }

        GuildEntity guild;
        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not null)
            {
                refused.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildHasGuild;
                return refused;
            }

            guild = new GuildEntity
            {
                GuildName = createInfo.GuildName,
                Notice = createInfo.Notice,
                JoinType = createInfo.JoinType,
                BuildTime = Now(),
            };
            GuildRules.SetLabels(guild, labels);
            if (await repository.CreateGuildAsync(guild, uid, GuildRules.MasterOffice) is not { } guildID)
            {
                refused.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildNameExist;
                return refused;
            }

            guild.GuildID = guildID;
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Uid {Uid} created guild {GuildID} {GuildName}", uid, guild.GuildID, guild.GuildName);
        var member = new GuildMember { GameUlongUID = uid, GuildID = guild.GuildID, Office = GuildRules.MasterOffice, JoinTime = guild.BuildTime };
        return new GuildOutcome<pbSCGuildCreateRsp>(
            new pbSCGuildCreateRsp
            {
                ResultInfo = new pbResultInfo(),
                GuildInfo = GuildInfoBuilder.BuildCreatedGuildInfo(guild, role),
            },
            [BuildInfoChgNtf(guild, member), .. activities.BuildCreatedGuildRedDots()]);
    }

    public async Task<GuildOutcome<pbSCGuildQueryRsp>> QueryAsync(ulong uid)
    {
        if (await FindMembershipAsync(uid) is not { } membership)
        {
            return new pbSCGuildQueryRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist), HasGuild = NoGuild };
        }

        var guildID = membership.Guild.GuildID;
        await activities.PushUnclaimedGearMailsAsync(await activities.MailUnclaimedGearsAsync(membership.Member));
        var guildInfo = await BuildGuildInfoAsync(membership.Guild);
        var response = EmptyChildren.Fill(new pbSCGuildQueryRsp
        {
            ResultInfo = new pbResultInfo(),
            HasGuild = HasGuild,
            GuildActivityInfo = await activities.BuildActivityInfoAsync(uid, guildID),
            DailyUniqTaskInfo = await activityTasks.BuildDailyUniqTaskInfoAsync(uid),
        });
        response.GuildInfo = guildInfo;
        return new GuildOutcome<pbSCGuildQueryRsp>(response, [])
        {
            Leading = [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf
            {
                RedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdGuildActivityTeam,
            })],
        };
    }

    public async Task<pbSCGuildMemberListRsp?> MemberListAsync(ulong uid)
    {
        if (await FindMembershipAsync(uid) is not { } membership)
        {
            return null;
        }

        return new pbSCGuildMemberListRsp
        {
            ResultInfo = new pbResultInfo(),
            MemberList = { await builder.BuildMemberInfosAsync(await LoadMembersAsync(membership.Guild.GuildID)) },
        };
    }

    public async Task<pbSCGuildSearchRsp> SearchAsync(string shortId, string name)
    {
        GuildEntity? guild = null;
        if (shortId.Length > 0)
        {
            if (GuildRules.TryParseShortId(shortId, out var guildID))
            {
                guild = await repository.FindGuildAsync(guildID);
            }
        }
        else if (name.Length > 0 && GuildRules.IsValidName(name))
        {
            guild = await repository.FindGuildByNameAsync(name);
        }

        if (guild is null)
        {
            return new pbSCGuildSearchRsp { ResultInfo = new pbResultInfo() };
        }

        var members = await LoadMembersAsync(guild.GuildID);
        var master = await FindMasterRoleAsync(members);
        return new pbSCGuildSearchRsp
        {
            ResultInfo = new pbResultInfo(),
            GuildInfo = GuildInfoBuilder.BuildBriefInfo(guild, master),
            GuildExtraInfo = await BuildExtraInfoAsync(guild, members),
        };
    }

    public async Task<pbSCGuildListSearchRsp> ListSearchAsync(ulong uid, IReadOnlyList<int> labels)
    {
        if (!GuildRules.AreValidLabels(gameData, labels))
        {
            return new pbSCGuildListSearchRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildLabelNumIllegal) };
        }

        var history = await repository.GetRoleGuildAsync(uid);
        history.HistoryLabel1 = labels.ElementAtOrDefault(0);
        history.HistoryLabel2 = labels.ElementAtOrDefault(1);
        history.HistoryLabel3 = labels.ElementAtOrDefault(2);
        await repository.SaveRoleGuildAsync(history);

        var memberCounts = await repository.CountMembersAsync();
        var matched = (await repository.GetGuildsAsync())
            .Select(guild => (Guild: guild, Matched: GuildRules.LabelsOf(guild).Count(labels.Contains)))
            .Where(candidate => candidate.Matched > 0 && memberCounts.ContainsKey(candidate.Guild.GuildID))
            .OrderByDescending(candidate => candidate.Matched)
            .ThenByDescending(candidate => memberCounts[candidate.Guild.GuildID])
            .ThenBy(candidate => candidate.Guild.GuildID)
            .Take(GuildRules.MaxSearchResultNum)
            .Select(candidate => candidate.Guild)
            .ToArray();
        var response = new pbSCGuildListSearchRsp { ResultInfo = new pbResultInfo() };
        foreach (var guild in matched)
        {
            var members = await LoadMembersAsync(guild.GuildID);
            response.MatchedGuildList.Add(GuildInfoBuilder.BuildBriefInfo(guild, await FindMasterRoleAsync(members)));
            response.MatchedGuildExtraInfoList.Add(await BuildExtraInfoAsync(guild, members));
        }

        return response;
    }

    public async Task<pbSCGuildListSearchHistoryRsp> ListSearchHistoryAsync(ulong uid)
    {
        var history = await repository.GetRoleGuildAsync(uid);
        return new pbSCGuildListSearchHistoryRsp
        {
            ResultInfo = new pbResultInfo(),
            HistoryLabelList =
            {
                new[] { history.HistoryLabel1, history.HistoryLabel2, history.HistoryLabel3 }
                    .Where(label => label != GuildRules.NoLabel && gameData.GuildLabel.Contains(label)),
            },
        };
    }

    public async Task<pbSCGuildRecommendInfoRsp> RecommendAsync(ulong uid)
    {
        var history = await repository.GetRoleGuildAsync(uid);
        int[] historyLabels = [history.HistoryLabel1, history.HistoryLabel2, history.HistoryLabel3];
        var ownGuildID = (await repository.FindMemberAsync(uid))?.GuildID;
        var memberCounts = await repository.CountMembersAsync();
        var maxMemberNum = GuildRules.MaxMemberNum(gameData);
        var recommended = (await repository.GetGuildsAsync())
            .Where(guild => guild.GuildID != ownGuildID
                && memberCounts.TryGetValue(guild.GuildID, out var count)
                && count < maxMemberNum)
            .OrderByDescending(guild => GuildRules.LabelsOf(guild).Count(label => historyLabels.Contains(label)))
            .ThenBy(_ => Random.Shared.Next())
            .Take(GuildRules.MaxSearchResultNum)
            .ToArray();
        var response = new pbSCGuildRecommendInfoRsp { ResultInfo = new pbResultInfo() };
        foreach (var guild in recommended)
        {
            var members = await LoadMembersAsync(guild.GuildID);
            response.MatchedGuildList.Add(new pbCOMDT_GUILD_RECOMMEND_INFO
            {
                GuildInfo = await BuildGuildInfoAsync(guild, members),
                MemberNum = members.Length,
                MasterInfo = await BuildMasterInfoAsync(members),
            });
        }

        return response;
    }

    public async Task<pbSCGuildQuerySearchGuildInfoRsp> QuerySearchGuildInfoAsync(long guildID)
    {
        if (guildID <= 0 || await repository.FindGuildAsync((ulong)guildID) is not { } guild)
        {
            return new pbSCGuildQuerySearchGuildInfoRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildNotExist) };
        }

        var members = await LoadMembersAsync(guild.GuildID);
        var master = await FindMasterRoleAsync(members);
        var guildInfo = await BuildGuildInfoAsync(guild, members);
        var response = EmptyChildren.Fill(new pbSCGuildQuerySearchGuildInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            Notice = guild.Notice,
            MemberNum = members.Length,
            MasterHeadIconInfo = master is null ? null : await builder.BuildCurHeadIconInfoAsync(master),
        });
        response.MasterHeadInfo = master is null ? GuildInfoBuilder.BuildUnknownHeadInfo() : await builder.BuildHeadInfoAsync(master);
        response.GuildInfo = guildInfo;
        return response;
    }

    public async Task<GuildOutcome<pbSCGuildJoinRsp>> JoinAsync(LMNRoleInfo role, long requestedGuildID)
    {
        var uid = role.GameUlongUID;
        var pushes = new List<(ulong Uid, GamePacket Packet)>();
        GuildEntity guild;
        GuildMember? joined = null;
        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not null)
            {
                return RefusedJoin(PB_ENUM_RES_ERROR_CODE.PbErrGuildHasGuild);
            }

            if (requestedGuildID <= 0 || await repository.FindGuildAsync((ulong)requestedGuildID) is not { } found)
            {
                return RefusedJoin(PB_ENUM_RES_ERROR_CODE.PbErrGuildNotExist);
            }

            guild = found;
            if (await FindJoinCdArgsAsync(uid) is { } cdArgs)
            {
                return new pbSCGuildJoinRsp
                {
                    ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildJoinInCd, Args = cdArgs },
                };
            }

            var members = await LoadMembersAsync(guild.GuildID);
            if (members.Length >= GuildRules.MaxMemberNum(gameData))
            {
                return RefusedJoin(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberFull);
            }

            if (guild.JoinType == GuildRules.AutoJoinJoinType)
            {
                joined = new GuildMember { GameUlongUID = uid, GuildID = guild.GuildID, Office = GuildRules.NormalOffice, JoinTime = Now() };
                if (!await repository.AddMemberAsync(joined))
                {
                    return RefusedJoin(PB_ENUM_RES_ERROR_CODE.PbErrGuildHasGuild);
                }
            }
            else if (guild.JoinType == GuildRules.NeedAgreeJoinType)
            {
                var requests = await repository.GetJoinRequestsAsync(guild.GuildID);
                if (requests.Any(request => request.GameUlongUID == uid))
                {
                    return RefusedJoin(PB_ENUM_RES_ERROR_CODE.PbErrGuildJoinRequest);
                }

                if (requests.Length >= GuildRules.MaxJoinRequestNum(gameData))
                {
                    return RefusedJoin(PB_ENUM_RES_ERROR_CODE.PbErrGuildJoinRequestFull);
                }

                if (!await repository.AddJoinRequestAsync(new GuildJoinRequest { GuildID = guild.GuildID, GameUlongUID = uid, RequestTime = Now() }))
                {
                    return RefusedJoin(PB_ENUM_RES_ERROR_CODE.PbErrGuildJoinRequest);
                }

                if (members.FirstOrDefault(member => member.Office == GuildRules.MasterOffice) is { } master)
                {
                    pushes.Add((master.GameUlongUID, BuildJoinRequestRedDot(requests.Length + 1)));
                }
            }
            else
            {
                return RefusedJoin(PB_ENUM_RES_ERROR_CODE.PbErrGuildDataError);
            }
        }
        finally
        {
            gate.Release();
        }

        await PushAllAsync(pushes);
        if (joined is null)
        {
            logger.LogInformation("Uid {Uid} asked to join guild {GuildID}", uid, guild.GuildID);
            return new pbSCGuildJoinRsp { ResultInfo = new pbResultInfo(), JoinType = guild.JoinType };
        }

        logger.LogInformation("Uid {Uid} joined guild {GuildID}", uid, guild.GuildID);
        await comments.AddEventAsync(guild.GuildID, GuildComments.MemberJoinCommentType, role);
        return new GuildOutcome<pbSCGuildJoinRsp>(
            new pbSCGuildJoinRsp
            {
                ResultInfo = new pbResultInfo(),
                JoinType = guild.JoinType,
                GuildInfo = await BuildGuildInfoAsync(guild),
            },
            [BuildInfoChgNtf(guild, joined)]);
    }

    public async Task<GuildOutcome<pbSCGuildJoinRequestListRsp>> JoinRequestListAsync(ulong uid)
    {
        if (await FindMembershipAsync(uid) is not { } membership)
        {
            return new pbSCGuildJoinRequestListRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist) };
        }

        if (membership.Member.Office != GuildRules.MasterOffice)
        {
            return new pbSCGuildJoinRequestListRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildPermissionDeniedForRequestList) };
        }

        var requests = await repository.GetJoinRequestsAsync(membership.Guild.GuildID);
        return new GuildOutcome<pbSCGuildJoinRequestListRsp>(
            new pbSCGuildJoinRequestListRsp
            {
                ResultInfo = new pbResultInfo(),
                RecordList = { await builder.BuildJoinRequestInfosAsync(requests) },
            },
            [BuildJoinRequestRedDot(0)]);
    }

    public async Task<pbSCGuildAcceptJoinRequestRsp> AcceptJoinRequestAsync(ulong uid, int isAcceptAll, ulong targetUid)
    {
        var response = new pbSCGuildAcceptJoinRequestRsp { ResultInfo = new pbResultInfo(), IsAcceptAll = isAcceptAll };
        var accepted = new List<(GuildMember Member, LMNRoleInfo Role)>();
        GuildEntity guild;
        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not { } membership)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist;
                return response;
            }

            if (membership.Member.Office != GuildRules.MasterOffice)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildPermissionDeniedForDealJoinRequest;
                return response;
            }

            guild = membership.Guild;
            var requests = await repository.GetJoinRequestsAsync(guild.GuildID);
            var pending = isAcceptAll == All ? requests : [.. requests.Where(request => request.GameUlongUID == targetUid)];
            if (pending.Length == 0)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildJoinRequestNotExist;
                return response;
            }

            var freeMemberNum = GuildRules.MaxMemberNum(gameData) - (await LoadMembersAsync(guild.GuildID)).Length;
            var refusal = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
            foreach (var request in pending)
            {
                var requester = await roles.FindAsync(request.GameUlongUID);
                if (requester is null || await repository.FindMemberAsync(request.GameUlongUID) is not null)
                {
                    await repository.DeleteJoinRequestAsync(guild.GuildID, request.GameUlongUID);
                    if (requester is not null)
                    {
                        response.DeletedList.Add(GuildInfoBuilder.BuildDeletedMemberInfo(request.GameUlongUID, requester.InGameUsrName));
                    }

                    refusal = PB_ENUM_RES_ERROR_CODE.PbErrGuildJoinMemberHasGuild;
                    continue;
                }

                if (freeMemberNum <= 0)
                {
                    refusal = isAcceptAll == All ? PB_ENUM_RES_ERROR_CODE.PbErrGuildFreeMemberNumNotEnought : PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberFull;
                    break;
                }

                var member = new GuildMember { GameUlongUID = request.GameUlongUID, GuildID = guild.GuildID, Office = GuildRules.NormalOffice, JoinTime = Now() };
                if (await repository.AddMemberAsync(member))
                {
                    accepted.Add((member, requester));
                    freeMemberNum--;
                }
            }

            response.IsAllSuc = accepted.Count == pending.Length ? AllSucceeded : NotAllSucceeded;
            if (accepted.Count == 0)
            {
                response.ResultInfo.ResultId = (int)refusal;
            }
        }
        finally
        {
            gate.Release();
        }

        foreach (var (member, role) in accepted)
        {
            response.JoinedList.Add(await builder.BuildMemberInfoAsync(member, role));
            logger.LogInformation("Guild {GuildID} master {Uid} accepted uid {JoinedUid}", guild.GuildID, uid, member.GameUlongUID);
            await lobbyRoles.PushAsync(member.GameUlongUID, BuildInfoChgNtf(guild, member));
            await comments.AddEventAsync(guild.GuildID, GuildComments.MemberJoinCommentType, role);
        }

        return response;
    }

    public async Task<GuildOutcome<pbSCGuildRejectJoinRequestRsp>> RejectJoinRequestAsync(ulong uid, int isRejectAll, ulong targetUid)
    {
        var response = new pbSCGuildRejectJoinRequestRsp { ResultInfo = new pbResultInfo(), IsRejectAll = isRejectAll, Uid = targetUid };
        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not { } membership)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist;
                return response;
            }

            if (membership.Member.Office != GuildRules.MasterOffice)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildPermissionDeniedForDealJoinRequest;
                return response;
            }

            var deleted = isRejectAll == All
                ? await repository.DeleteJoinRequestsAsync(membership.Guild.GuildID)
                : await repository.DeleteJoinRequestAsync(membership.Guild.GuildID, targetUid);
            if (isRejectAll != All && deleted == 0)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildJoinRequestNotExist;
                return response;
            }

            logger.LogInformation("Guild {GuildID} master {Uid} rejected {Deleted} join requests", membership.Guild.GuildID, uid, deleted);
            var remaining = (await repository.GetJoinRequestsAsync(membership.Guild.GuildID)).Length;
            return new GuildOutcome<pbSCGuildRejectJoinRequestRsp>(response, [BuildJoinRequestRedDot(remaining)]);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<pbSCGuildSettingRsp> SettingAsync(ulong uid, pbCSGuildSettingReq request)
    {
        var labels = request.LabelList.ToArray();
        var result = ValidateSetting(request.Notice, labels, request.JoinType);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return new pbSCGuildSettingRsp { ResultInfo = Result(result) };
        }

        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not { } membership)
            {
                return new pbSCGuildSettingRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist) };
            }

            if (membership.Member.Office != GuildRules.MasterOffice)
            {
                return new pbSCGuildSettingRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildPermissionDeniedNotMaster) };
            }

            var guild = membership.Guild;
            var joinTypeChanged = guild.JoinType != request.JoinType;
            guild.Notice = request.Notice;
            guild.JoinType = request.JoinType;
            GuildRules.SetLabels(guild, labels);
            await repository.UpdateSettingAsync(guild, joinTypeChanged);
            logger.LogInformation("Guild {GuildID} master {Uid} changed the settings; join type changed {JoinTypeChanged}",
                guild.GuildID, uid, joinTypeChanged);
            return new pbSCGuildSettingRsp { ResultInfo = new pbResultInfo() };
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<GuildOutcome<pbSCGuildTransferMasterRsp>> TransferMasterAsync(LMNRoleInfo role, ulong targetUid)
    {
        var uid = role.GameUlongUID;
        var response = new pbSCGuildTransferMasterRsp { ResultInfo = new pbResultInfo(), Uid = targetUid };
        GuildMembership membership;
        GuildMember target;
        LMNRoleInfo targetRole;
        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not { } found)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist;
                return response;
            }

            membership = found;
            if (membership.Member.Office != GuildRules.MasterOffice)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildPermissionDeniedNotMaster;
                return response;
            }

            if (targetUid == uid
                || await repository.FindMemberAsync(targetUid) is not { } targetMember
                || targetMember.GuildID != membership.Guild.GuildID
                || await roles.FindAsync(targetUid) is not { } foundRole)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist;
                return response;
            }

            target = targetMember;
            targetRole = foundRole;
            await repository.TransferMasterAsync(uid, GuildRules.NormalOffice, targetUid, GuildRules.MasterOffice);
            membership.Member.Office = GuildRules.NormalOffice;
            target.Office = GuildRules.MasterOffice;
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Guild {GuildID} master moved from uid {Uid} to uid {TargetUid}", membership.Guild.GuildID, uid, targetUid);
        await lobbyRoles.PushAsync(targetUid, BuildInfoChgNtf(membership.Guild, target));
        await comments.AddEventAsync(membership.Guild.GuildID, GuildComments.MasterTransferCommentType, role, targetRole);
        return new GuildOutcome<pbSCGuildTransferMasterRsp>(response, [BuildInfoChgNtf(membership.Guild, membership.Member)]);
    }

    public async Task<pbSCGuildKickMemberRsp> KickMemberAsync(ulong uid, ulong targetUid)
    {
        var response = new pbSCGuildKickMemberRsp { ResultInfo = new pbResultInfo(), Uid = targetUid };
        GuildEntity guild;
        IReadOnlyList<RoleMail> gearMails;
        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not { } membership)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist;
                return response;
            }

            if (membership.Member.Office != GuildRules.MasterOffice)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildPermissionDeniedNotMaster;
                return response;
            }

            guild = membership.Guild;
            if (targetUid == uid
                || await repository.FindMemberAsync(targetUid) is not { } target
                || target.GuildID != guild.GuildID)
            {
                response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist;
                return response;
            }

            gearMails = await activities.MailUnclaimedGearsAsync(target);
            await repository.RemoveMemberAsync(targetUid);
        }
        finally
        {
            gate.Release();
        }

        await activities.PushUnclaimedGearMailsAsync(gearMails);

        logger.LogInformation("Guild {GuildID} master {Uid} removed uid {TargetUid}", guild.GuildID, uid, targetUid);
        await lobbyRoles.PushAsync(
            targetUid,
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGuildKickMemberNtf, new pbSCGuildKickMemberNtf
            {
                ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildKickMember),
            }),
            BuildInfoChgNtf(null, null));
        if (await roles.FindAsync(targetUid) is { } targetRole)
        {
            await comments.AddEventAsync(guild.GuildID, GuildComments.MemberQuitCommentType, targetRole);
        }

        return response;
    }

    public async Task<GuildOutcome<pbSCGuildMemberQuitRsp>> QuitAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        GuildEntity guild;
        IReadOnlyList<RoleMail> gearMails;
        var disbanded = false;
        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not { } membership)
            {
                return new pbSCGuildMemberQuitRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist) };
            }

            guild = membership.Guild;
            var members = await repository.GetMembersAsync(guild.GuildID);
            if (membership.Member.Office == GuildRules.MasterOffice && members.Length > 1)
            {
                return new pbSCGuildMemberQuitRsp { ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildHasMember) };
            }

            gearMails = await activities.MailUnclaimedGearsAsync(membership.Member);
            if (members.Length > 1)
            {
                await repository.RemoveMemberAsync(uid);
            }
            else
            {
                await repository.DeleteGuildAsync(guild.GuildID);
                disbanded = true;
            }

            await RecordQuitAsync(uid);
        }
        finally
        {
            gate.Release();
        }

        await activities.PushUnclaimedGearMailsAsync(gearMails);

        if (disbanded)
        {
            logger.LogInformation("Uid {Uid} left guild {GuildID} as its last member; the guild is disbanded", uid, guild.GuildID);
        }
        else
        {
            logger.LogInformation("Uid {Uid} left guild {GuildID}", uid, guild.GuildID);
            await comments.AddEventAsync(guild.GuildID, GuildComments.MemberQuitCommentType, role);
        }

        return new GuildOutcome<pbSCGuildMemberQuitRsp>(new pbSCGuildMemberQuitRsp { ResultInfo = new pbResultInfo() }, [])
        {
            Leading = [.. RedDotsClearedOnQuit.Select(redDotID => GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf,
                new pbSCRedDotClearNtf { RedDotID = (int)redDotID }))],
        };
    }

    public static pbResultInfo Result(PB_ENUM_RES_ERROR_CODE result) => new() { ResultId = (int)result };

    public static int InGuildFlag(GuildMembership? membership) => membership is null ? NotInGuild : InGuild;

    private static pbSCGuildJoinRsp RefusedJoin(PB_ENUM_RES_ERROR_CODE result) => new() { ResultInfo = Result(result) };

    private static GamePacket BuildInfoChgNtf(GuildEntity? guild, GuildMember? member) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGuildInfoChgNtf, new pbSCGuildInfoChgNtf
        {
            GuildInfo = GuildInfoBuilder.BuildAcntGuildInfo(guild, member),
        });

    private static GamePacket BuildJoinRequestRedDot(int requestNum) => requestNum > 0
        ? GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = NewGuildJoinRequestRedDotID, RedDotBadgeNum = requestNum } },
            },
        })
        : GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = NewGuildJoinRequestRedDotID });

    private PB_ENUM_RES_ERROR_CODE ValidateSetting(string notice, IReadOnlyList<int> labels, int joinType)
    {
        if (!GuildRules.IsValidNotice(notice))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrGuildNoticeIllegal;
        }

        if (!GuildRules.AreValidLabels(gameData, labels))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrGuildLabelNumIllegal;
        }

        return GuildRules.IsValidJoinType(joinType) ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
    }

    private async Task<pbArgList?> FindJoinCdArgsAsync(ulong uid)
    {
        var roleGuild = await repository.GetRoleGuildAsync(uid);
        var now = clock.GetUtcNow();
        var lastQuit = DateTimeOffset.FromUnixTimeSeconds(roleGuild.LastQuitTime);
        var cdEnd = lastQuit + GuildRules.JoinGuildCdTime(gameData);
        if (roleGuild.QuitCnt <= GuildRules.MaxDailyFreeQuitCnt || now >= cdEnd)
        {
            return null;
        }

        var remainingMinutes = (int)Math.Ceiling((cdEnd - now).TotalMinutes);
        return new pbArgList
        {
            Args = { new pbArgData { Value = new pbArgDataValue { IntValue = remainingMinutes } } },
        };
    }

    private async Task RecordQuitAsync(ulong uid)
    {
        var roleGuild = await repository.GetRoleGuildAsync(uid);
        var now = clock.GetUtcNow();
        var sameDay = DateTimeOffset.FromUnixTimeSeconds(roleGuild.LastQuitTime).UtcDateTime.Date == now.UtcDateTime.Date;
        roleGuild.QuitCnt = sameDay ? roleGuild.QuitCnt + 1 : 1;
        roleGuild.LastQuitTime = now.ToUnixTimeSeconds();
        await repository.SaveRoleGuildAsync(roleGuild);
    }

    private Task<GuildMember[]> LoadMembersAsync(ulong guildID) => repository.GetMembersAsync(guildID);

    private async Task<LMNRoleInfo?> FindMasterRoleAsync(IEnumerable<GuildMember> members) =>
        members.FirstOrDefault(member => member.Office == GuildRules.MasterOffice) is { } master
            ? await roles.FindAsync(master.GameUlongUID)
            : null;

    private async Task<pbCOMDT_GUILD_MEMBER_INFO?> BuildMasterInfoAsync(IEnumerable<GuildMember> members)
    {
        var master = members.FirstOrDefault(member => member.Office == GuildRules.MasterOffice);
        return master is not null && await roles.FindAsync(master.GameUlongUID) is { } role
            ? await builder.BuildMemberInfoAsync(master, role)
            : null;
    }

    private async Task<pbCOMDT_GUILD_EXTRA_INFO> BuildExtraInfoAsync(GuildEntity guild, GuildMember[] members) => new()
    {
        GuildID = (long)guild.GuildID,
        MemberNum = members.Length,
        MasterInfo = await BuildMasterInfoAsync(members),
        Notice = guild.Notice,
    };

    private async Task<pbCOMDT_GUILD_INFO> BuildGuildInfoAsync(GuildEntity guild, GuildMember[]? members = null)
    {
        members ??= await LoadMembersAsync(guild.GuildID);
        return GuildInfoBuilder.BuildGuildInfo(guild, await FindMasterRoleAsync(members), await activities.BuildBriefInfoAsync(guild.GuildID));
    }

    private async Task PushAllAsync(IEnumerable<(ulong Uid, GamePacket Packet)> pushes)
    {
        foreach (var (pushUid, packet) in pushes)
        {
            await lobbyRoles.PushAsync(pushUid, packet);
        }
    }

    private long Now() => clock.GetUtcNow().ToUnixTimeSeconds();
}
