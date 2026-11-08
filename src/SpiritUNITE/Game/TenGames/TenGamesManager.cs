using System.Globalization;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Auth;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.TenGames;

public sealed record TenGamesMembership(TenGamesTeam Team, TenGamesTeamMember Member);

public sealed record TenGamesRoomTeam(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_TEN_GAMES_TEAM_BRIEF_INFO? TeamInfo, int MapID = 0);

public sealed class TenGamesManager(
    TenGamesRepository repository,
    FriendRepository friends,
    TenGamesSchedule schedule,
    TenGamesTeamInfoBuilder builder,
    RoleInfoStore roles,
    AccountManager accounts,
    MailManager mail,
    GameData gameData,
    LobbyRoles lobbyRoles,
    TimeProvider clock,
    ILogger<TenGamesManager> logger)
{
    private const int NotJoined = 0;
    private const int NotApplied = 0;
    private const int Applied = 1;
    private const int NoTeamID = 0;
    private const int OnlineCompetitionSubMode = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeOnlineCompetition;
    private const PB_ENUM_RES_RED_DOT_ID InviteRedDot = PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdTenGamesTeamInvite;
    private const int TeamMemberLeaveMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTenGamesTeamMemberLeave;
    private const int TeamKickedMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTenGamesTeamKicked;
    private const int TeamDissolveMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTenGamesTeamDissolve;
    private const int RankRewardMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeTenGamesRankReward;
    private const int JoinRewardMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeTenGamesJoinReward;
    private const string TeamMemberLeaveMailSubject = "System_Championship_Team_Tips_13819_001";
    private const string TeamMemberLeaveMailContent = "System_Championship_Team_Tips_13820_001";
    private const string TeamKickedMailSubject = "System_Championship_Team_Tips_13817_001";
    private const string TeamKickedMailContent = "System_Championship_Team_Tips_13818_001";
    private const string TeamDissolveMailSubject = "System_Championship_Team_Tips_13815_001";
    private const string TeamDissolveMailContent = "System_Championship_Team_Tips_13816_001";
    private const string RankRewardMailText = "System_Championship_Mail_Content_18815_001";
    private const string JoinRewardMailText = "System_Championship_Mail_Content_18814_001";
    private const int ParticipationAward = 1;
    private const int MaxTeamNameBytes = 64;

    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<TenGamesMembership?> FindMembershipAsync(ulong uid)
    {
        if (await repository.FindMemberAsync(uid) is not { } member)
        {
            return null;
        }

        if (await repository.FindTeamAsync(member.TeamID) is not { } team)
        {
            await repository.RemoveMemberAsync(uid);
            return null;
        }

        return new TenGamesMembership(team, member);
    }

    public async Task<long> FindTeamIDAsync(ulong uid) =>
        (long)((await repository.FindMemberAsync(uid))?.TeamID ?? NoTeamID);

    public async Task<bool> IsTeamMemberAsync(ulong uid, ulong teamID) =>
        (await repository.FindMemberAsync(uid))?.TeamID == teamID;

    public async Task<pbSCGetTenGamesTeamInfoRsp> GetTeamInfoAsync(ulong uid)
    {
        await ArchiveEndedGamesAsync();
        return new pbSCGetTenGamesTeamInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            TeamInfo = await FindMembershipAsync(uid) is { } membership
                ? await BuildTeamInfoAsync(membership.Team)
                : TenGamesTeamInfoBuilder.NoTeam,
        };
    }

    public async Task<pbSCCreateTenGamesTeamRsp> CreateAsync(LMNRoleInfo role, string teamName)
    {
        var uid = role.GameUlongUID;
        var refused = new pbSCCreateTenGamesTeamRsp { ResultInfo = new pbResultInfo(), TeamInfo = TenGamesTeamInfoBuilder.NoTeam };
        if (!IsValidTeamName(teamName))
        {
            return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamNameIllegal);
        }

        if (!await IsPTCSAsync(uid))
        {
            return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesJoinTeamNotBindPtcs);
        }

        var now = Now();
        TenGamesTeam team;
        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not null)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesHasTeam);
            }

            var createCd = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalCreateTengamesTeamCd);
            if (now - (await repository.GetRoleAsync(uid)).TenGamesTeamCreateTime < createCd)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesCreateTeamCd);
            }

            team = new TenGamesTeam { TeamName = teamName, TeamLeaderUid = uid, CreateTime = now };
            if (await repository.CreateTeamAsync(team) is not { } teamID)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamNameExist);
            }

            team.TeamID = teamID;
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Uid {Uid} created ten games team {TeamID} {TeamName}", uid, team.TeamID, team.TeamName);
        await PushTeamChangeToFriendsAsync(uid, team.TeamID);
        return new pbSCCreateTenGamesTeamRsp { ResultInfo = new pbResultInfo(), TeamInfo = await BuildTeamInfoAsync(team) };
    }

    public async Task<pbSCInviteJoinTenGamesTeamRsp> InviteAsync(ulong uid, long teamID, ulong inviteeUid)
    {
        var response = new pbSCInviteJoinTenGamesTeamRsp { ResultInfo = new pbResultInfo(), TeamID = teamID, InviteeUid = inviteeUid };
        TenGamesTeam team;
        int pendingInvites;
        await gate.WaitAsync();
        try
        {
            var (membership, refusal) = await FindOwnTeamAsync(uid, teamID);
            if (refusal is { } result)
            {
                return Refuse(response, response.ResultInfo, result);
            }

            team = membership!.Team;
            if (team.JoinTenGamesID != NotJoined)
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesApplying);
            }

            if (inviteeUid == uid || await roles.FindAsync(inviteeUid) is null)
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesOperaterFail);
            }

            if ((await repository.GetMembersAsync(team.TeamID)).Length >= schedule.MaxTeamMemberNum)
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamFull);
            }

            if (await repository.FindMemberAsync(inviteeUid) is not null)
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesOhterAlreadyInTeam);
            }

            var invite = new TenGamesTeamInvite { GameUlongUID = inviteeUid, TeamID = team.TeamID, InviteUid = uid, InvitedTime = Now() };
            if (!await repository.AddInviteAsync(invite))
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesRepeatedInvite);
            }

            pendingInvites = await CountUnreadInvitesAsync(inviteeUid);
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Uid {Uid} invited uid {InviteeUid} to ten games team {TeamID}", uid, inviteeUid, team.TeamID);
        await lobbyRoles.PushAsync(inviteeUid, GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf,
            LobbyNotices.BuildRedDotReplaceNtf(InviteRedDot, pendingInvites)));
        return response;
    }

    public async Task<pbSCRefuseJoinTenGamesTeamRsp> RefuseAsync(ulong uid, long teamID, ulong inviterUid)
    {
        var response = new pbSCRefuseJoinTenGamesTeamRsp { ResultInfo = new pbResultInfo(), TeamID = teamID, InviterUid = inviterUid };
        if (teamID <= NoTeamID || await repository.FindInviteAsync(uid, (ulong)teamID) is null)
        {
            return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamNotExist);
        }

        await repository.DeleteInviteAsync(uid, (ulong)teamID);
        logger.LogInformation("Uid {Uid} refused the invite to ten games team {TeamID}", uid, teamID);
        return response;
    }

    public async Task<pbSCJoinTenGamesTeamRsp> JoinAsync(ulong uid, long teamID)
    {
        var refused = new pbSCJoinTenGamesTeamRsp { ResultInfo = new pbResultInfo(), TeamInfo = TenGamesTeamInfoBuilder.NoTeam };
        if (teamID <= NoTeamID)
        {
            return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamNotExist);
        }

        if (!await IsPTCSAsync(uid))
        {
            return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesJoinTeamNotBindPtcs);
        }

        TenGamesTeam team;
        await gate.WaitAsync();
        try
        {
            if (await repository.FindTeamAsync((ulong)teamID) is not { } found)
            {
                await repository.DeleteInviteAsync(uid, (ulong)teamID);
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamNotExist);
            }

            team = found;
            if (await repository.FindInviteAsync(uid, team.TeamID) is null)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesOperaterFail);
            }

            if (await FindMembershipAsync(uid) is not null)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesHasTeam);
            }

            if (team.JoinTenGamesID != NotJoined)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesApplying);
            }

            if ((await repository.GetMembersAsync(team.TeamID)).Length >= schedule.MaxTeamMemberNum)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamFull);
            }

            if (!await repository.AddMemberAsync(new TenGamesTeamMember { GameUlongUID = uid, TeamID = team.TeamID, JoinTime = Now() }))
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesHasTeam);
            }
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Uid {Uid} joined ten games team {TeamID}", uid, team.TeamID);
        await PushTeamChangeToFriendsAsync(uid, team.TeamID);
        await lobbyRoles.PushAsync(uid, GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf,
            LobbyNotices.BuildRedDotClearNtf(InviteRedDot)));
        return new pbSCJoinTenGamesTeamRsp { ResultInfo = new pbResultInfo(), TeamInfo = await BuildTeamInfoAsync(team) };
    }

    public async Task<pbSCModifyTenGamesTeamNameRsp> RenameAsync(ulong uid, long teamID, string newTeamName)
    {
        var response = new pbSCModifyTenGamesTeamNameRsp { ResultInfo = new pbResultInfo(), TeamID = teamID, NewTeamName = newTeamName };
        if (!IsValidTeamName(newTeamName))
        {
            return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamNameIllegal);
        }

        var now = Now();
        await gate.WaitAsync();
        try
        {
            if (await FindLeaderTeamAsync(uid, teamID) is { } refusal)
            {
                return Refuse(response, response.ResultInfo, refusal);
            }

            var team = (await repository.FindTeamAsync((ulong)teamID))!;
            var modifyNameCd = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalModifyTengamesTeamNameCd);
            if (now - team.ModifyNameTime < modifyNameCd)
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesModifyNameCd);
            }

            if (!await repository.RenameTeamAsync(team.TeamID, newTeamName, now))
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamNameExist);
            }
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Uid {Uid} renamed ten games team {TeamID} to {TeamName}", uid, teamID, newTeamName);
        return response;
    }

    public async Task<pbSCTenGamesTeamKickMemberRsp> KickAsync(ulong uid, long teamID, ulong kickUid)
    {
        var response = new pbSCTenGamesTeamKickMemberRsp { ResultInfo = new pbResultInfo(), TeamID = teamID, KickUid = kickUid };
        await gate.WaitAsync();
        try
        {
            if (await FindLeaderTeamAsync(uid, teamID) is { } refusal)
            {
                return Refuse(response, response.ResultInfo, refusal);
            }

            if (kickUid == uid)
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesOperaterFail);
            }

            if (!await IsTeamMemberAsync(kickUid, (ulong)teamID))
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesOhterNotInTeam);
            }

            await repository.RemoveMemberAsync(kickUid);
        }
        finally
        {
            gate.Release();
        }

        response.KickRoleName = (await roles.FindAsync(kickUid))?.InGameUsrName ?? "";
        logger.LogInformation("Uid {Uid} removed uid {KickUid} from ten games team {TeamID}", uid, kickUid, teamID);
        await SendTeamMailAsync(kickUid, TeamKickedMailSubject, TeamKickedMailContent, TeamKickedMailID);
        await PushTeamChangeToFriendsAsync(kickUid, NoTeamID);
        return response;
    }

    public async Task<pbSCDissolveTenGamesTeamRsp> DissolveAsync(ulong uid, long teamID)
    {
        var response = new pbSCDissolveTenGamesTeamRsp { ResultInfo = new pbResultInfo(), TeamID = teamID };
        TenGamesTeamMember[] members;
        await gate.WaitAsync();
        try
        {
            if (await FindLeaderTeamAsync(uid, teamID) is { } refusal)
            {
                return Refuse(response, response.ResultInfo, refusal);
            }

            members = await repository.GetMembersAsync((ulong)teamID);
            await repository.DeleteTeamAsync((ulong)teamID);
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Uid {Uid} disbanded ten games team {TeamID} of {MemberCount} members", uid, teamID, members.Length);
        foreach (var member in members)
        {
            if (member.GameUlongUID != uid)
            {
                await SendTeamMailAsync(member.GameUlongUID, TeamDissolveMailSubject, TeamDissolveMailContent, TeamDissolveMailID);
            }

            await PushTeamChangeToFriendsAsync(member.GameUlongUID, NoTeamID);
        }

        return response;
    }

    public async Task<pbSCLeaveTenGamesTeamRsp> LeaveAsync(ulong uid, long teamID)
    {
        var response = new pbSCLeaveTenGamesTeamRsp { ResultInfo = new pbResultInfo(), TeamID = teamID };
        TenGamesTeam team;
        await gate.WaitAsync();
        try
        {
            var (membership, refusal) = await FindOwnTeamAsync(uid, teamID);
            if (refusal is { } result)
            {
                return Refuse(response, response.ResultInfo, result);
            }

            team = membership!.Team;
            if (team.TeamLeaderUid == uid)
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesLeaderNotAllowLeave);
            }

            if (team.JoinTenGamesID != NotJoined)
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesApplying);
            }

            await repository.RemoveMemberAsync(uid);
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Uid {Uid} left ten games team {TeamID}", uid, teamID);
        await SendTeamMailAsync(team.TeamLeaderUid, TeamMemberLeaveMailSubject, TeamMemberLeaveMailContent, TeamMemberLeaveMailID);
        await PushTeamChangeToFriendsAsync(uid, NoTeamID);
        return response;
    }

    public async Task<pbSCTransferTenGamesTeamLeaderRsp> TransferLeaderAsync(ulong uid, long teamID, ulong newLeaderUid)
    {
        var response = new pbSCTransferTenGamesTeamLeaderRsp { ResultInfo = new pbResultInfo(), TeamID = teamID, NewLeaderUid = newLeaderUid };
        await gate.WaitAsync();
        try
        {
            if (await FindLeaderTeamAsync(uid, teamID) is { } refusal)
            {
                return Refuse(response, response.ResultInfo, refusal);
            }

            if (newLeaderUid == uid)
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesOperaterFail);
            }

            if (!await IsTeamMemberAsync(newLeaderUid, (ulong)teamID))
            {
                return Refuse(response, response.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesOhterNotInTeam);
            }

            await repository.SetLeaderAsync((ulong)teamID, newLeaderUid);
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Uid {Uid} made uid {NewLeaderUid} leader of ten games team {TeamID}", uid, newLeaderUid, teamID);
        return response;
    }

    public async Task<GamePacket[]> GetInviteListAsync(ulong uid, GamePacket request)
    {
        var inviteList = new pbCOMDT_TEN_GAMES_INVITE_LIST_INFO();
        foreach (var invite in await repository.GetInvitesAsync(uid))
        {
            if (await repository.FindTeamAsync(invite.TeamID) is not { } team)
            {
                await repository.DeleteInviteAsync(uid, invite.TeamID);
                continue;
            }

            var leader = await roles.FindAsync(team.TeamLeaderUid);
            inviteList.InviteList.Add(new pbCOMDT_TEN_GAMES_INVITE_INFO
            {
                InvitedTime = invite.InvitedTime,
                TeamID = (long)team.TeamID,
                TeamName = team.TeamName,
                TeamLeaderUid = team.TeamLeaderUid,
                TeamLeaderName = leader?.InGameUsrName ?? "",
                InviteUid = invite.InviteUid,
            });
        }

        await repository.SaveLastReadInviteTimeAsync(uid, Now());
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, LobbyNotices.BuildRedDotClearNtf(InviteRedDot)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetTenGamesInviteListInfoRsp, new pbSCGetTenGamesInviteListInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                InviteListInfo = inviteList,
            }),
        ];
    }

    public async Task<pbSCGetOtherTenGamesTeamInfoRsp> GetOtherTeamInfoAsync(long teamID) =>
        teamID > NoTeamID && await repository.FindTeamAsync((ulong)teamID) is { } team
            ? new pbSCGetOtherTenGamesTeamInfoRsp { ResultInfo = new pbResultInfo(), TeamInfo = await BuildTeamInfoAsync(team) }
            : new pbSCGetOtherTenGamesTeamInfoRsp
            {
                ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamNotExist),
                TeamInfo = TenGamesTeamInfoBuilder.NoTeam,
            };

    public async Task<pbSCGetTenGamesInfoRsp> GetInfoAsync(ulong uid)
    {
        await ArchiveEndedGamesAsync();
        var membership = await FindMembershipAsync(uid);
        var (gamesId, states, statesInfo, applyInfo) = await BuildStateAsync(membership?.Team);
        return new pbSCGetTenGamesInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            GamesId = gamesId,
            States = (int)states,
            StatesInfo = statesInfo,
            IsPTCS = await IsPTCSAsync(uid) ? 1 : 0,
            ApplyInfo = applyInfo,
        };
    }

    public async Task<pbSCTenGamesApplyRsp> ApplyAsync(ulong uid, int gamesId)
    {
        var now = Now();
        var refused = new pbSCTenGamesApplyRsp
        {
            ResultInfo = new pbResultInfo(),
            StatesInfo = EmptyChildren.Fill(new pbCOMDT_TEN_GAMES_STATES_INFO()),
            ApplyInfo = new pbCOMDT_TEN_GAMES_APPLY_INFO(),
        };
        if (schedule.Find(gamesId) is not { } game || !game.IsDisplayed(now))
        {
            return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesInvalidMatchTime);
        }

        if (!game.IsInRegisterTime(now))
        {
            return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesApplyTimeLimit);
        }

        TenGamesTeam team;
        await gate.WaitAsync();
        try
        {
            if (await FindMembershipAsync(uid) is not { } membership)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesNotTeam);
            }

            team = membership.Team;
            if (team.TeamLeaderUid != uid)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesNotTeamLeader);
            }

            if (team.JoinTenGamesID != NotJoined)
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesApplying);
            }

            var members = await repository.GetMembersAsync(team.TeamID);
            if (!game.AllowsMemberCount(members.Length))
            {
                return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesApplyTeamMemberNumNot);
            }

            foreach (var member in members)
            {
                if (!await IsPTCSAsync(member.GameUlongUID))
                {
                    return Refuse(refused, refused.ResultInfo, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesJoinTeamNotBindPtcs);
                }
            }

            var initScore = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTengamesTeamApplyInitScore);
            await repository.ApplyAsync(team.TeamID, gamesId, initScore);
            team.JoinTenGamesID = gamesId;
            team.CurScore = initScore;
            team.WinCount = 0;
            team.LoseCount = 0;
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Uid {Uid} entered ten games team {TeamID} into games {GamesId}", uid, team.TeamID, gamesId);
        var (_, states, statesInfo, applyInfo) = await BuildStateAsync(team);
        return new pbSCTenGamesApplyRsp
        {
            ResultInfo = new pbResultInfo(),
            GamesId = gamesId,
            States = (int)states,
            StatesInfo = statesInfo,
            ApplyInfo = applyInfo,
        };
    }

    public async Task<pbSCGetTenGamesHistoryInfoRsp> GetHistoryAsync(ulong uid)
    {
        await ArchiveEndedGamesAsync();
        var response = new pbSCGetTenGamesHistoryInfoRsp { ResultInfo = new pbResultInfo() };
        foreach (var archived in await repository.GetHistoryAsync(uid))
        {
            var history = archived.History;
            response.TenGamesHistoryInfoList.Add(new pbCOMDT_TEN_GAMES_HISTORY_INFO
            {
                GamesId = history.GamesId,
                TotalScore = history.TotalScore,
                RankNo = history.RankNo,
                TeamID = (long)history.TeamID,
                TeamName = history.TeamName,
                TeamMemberUidList = { await builder.BuildMemberInfosAsync(archived.MemberUids) },
                TeamLeaderUid = history.TeamLeaderUid,
            });
        }

        return response;
    }

    public bool IsTenGamesRoom(int subMapMode, int mapID) =>
        subMapMode == OnlineCompetitionSubMode
        || (mapID != 0 && gameData.SceneMap.Find(mapID)?.MapSubMode == OnlineCompetitionSubMode);

    public async Task<TenGamesRoomTeam> FindRoomTeamAsync(ulong uid)
    {
        if (await FindMembershipAsync(uid) is not { } membership)
        {
            return new TenGamesRoomTeam(PB_ENUM_RES_ERROR_CODE.PbErrTenGamesNotTeam, null);
        }

        var team = membership.Team;
        if (team.JoinTenGamesID == NotJoined)
        {
            return new TenGamesRoomTeam(PB_ENUM_RES_ERROR_CODE.PbErrTenGamesNotApply, null);
        }

        if (schedule.Find(team.JoinTenGamesID) is not { } game || game.FindOpenWindow(Now()) is null)
        {
            return new TenGamesRoomTeam(PB_ENUM_RES_ERROR_CODE.PbErrTenGamesInvalidMatchTime, null);
        }

        var teamInfo = new pbCOMDT_TEN_GAMES_TEAM_BRIEF_INFO
        {
            TeamID = (long)team.TeamID,
            TeamName = team.TeamName,
            TeamMemberUidList = { await GetMemberUidsAsync(team.TeamID) },
            TeamLeaderUid = team.TeamLeaderUid,
            JoinTenGamesID = team.JoinTenGamesID,
            CurScore = team.CurScore,
            WinCount = team.WinCount,
            LoseCount = team.LoseCount,
        };
        return new TenGamesRoomTeam(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, teamInfo, game.Row.MapList.FirstOrDefault(mapID => mapID != 0));
    }

    public async Task<TenGamesTeam?> FindPlayingTeamAsync(ulong uid, int mapId)
    {
        if (await FindMembershipAsync(uid) is not { } membership
            || schedule.Find(membership.Team.JoinTenGamesID) is not { } game
            || game.FindOpenWindow(Now()) is null
            || !game.Row.MapList.Contains(mapId))
        {
            return null;
        }

        return membership.Team;
    }

    public async Task<IReadOnlyList<ulong>> GetMemberUidsAsync(ulong teamID) =>
        [.. (await repository.GetMembersAsync(teamID)).Select(member => member.GameUlongUID)];

    public async Task SaveBattleAsync(TenGamesTeam team)
    {
        await gate.WaitAsync();
        try
        {
            await repository.SaveBattleAsync(team);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(int GamesId, TenGamesState States, pbCOMDT_TEN_GAMES_STATES_INFO StatesInfo, pbCOMDT_TEN_GAMES_APPLY_INFO ApplyInfo)> BuildStateAsync(
        TenGamesTeam? team)
    {
        var statesInfo = EmptyChildren.Fill(new pbCOMDT_TEN_GAMES_STATES_INFO());
        if (team is null || team.JoinTenGamesID == NotJoined || schedule.Find(team.JoinTenGamesID) is not { } game)
        {
            return (NotJoined, TenGamesState.NotApply, statesInfo, new pbCOMDT_TEN_GAMES_APPLY_INFO { ApplyState = NotApplied });
        }

        var status = game.StatusAt(Now());
        var applyInfo = new pbCOMDT_TEN_GAMES_APPLY_INFO { ApplyState = Applied, EndTime = status.EndTime };
        switch (status.State)
        {
            case TenGamesState.Apply:
                statesInfo.Apply.EndTime = status.EndTime;
                break;
            case TenGamesState.NotStart:
                statesInfo.NotStart.NextStartTime = status.EndTime;
                break;
            case TenGamesState.Start:
                statesInfo.Start.EndTime = status.EndTime;
                statesInfo.Start.RankNo = await FindRankNoAsync(team);
                statesInfo.Start.RankScore = team.CurScore;
                statesInfo.Start.WinCnt = team.WinCount;
                statesInfo.Start.LoseCnt = team.LoseCount;
                break;
            case TenGamesState.Interval:
                statesInfo.Interval.NextStartTime = status.EndTime;
                statesInfo.Interval.RankNo = await FindRankNoAsync(team);
                statesInfo.Interval.RankScore = team.CurScore;
                statesInfo.Interval.WinCnt = team.WinCount;
                statesInfo.Interval.LoseCnt = team.LoseCount;
                break;
            default:
                statesInfo.Settle.EndTime = status.EndTime;
                break;
        }

        return (game.GamesId, status.State == TenGamesState.Ended ? TenGamesState.Settle : status.State, statesInfo, applyInfo);
    }

    private async Task<int> FindRankNoAsync(TenGamesTeam team) =>
        RankTeams(await repository.GetTeamsOfGameAsync(team.JoinTenGamesID)).FindIndex(ranked => ranked.TeamID == team.TeamID) + 1;

    private async Task ArchiveEndedGamesAsync()
    {
        var now = Now();
        foreach (var gamesId in await repository.GetJoinedGamesIdsAsync())
        {
            if (schedule.Find(gamesId) is { } game && game.StatusAt(now).State != TenGamesState.Ended)
            {
                continue;
            }

            await ArchiveGameAsync(gamesId);
        }
    }

    private async Task ArchiveGameAsync(int gamesId)
    {
        List<TenGamesArchivedTeam> archived = [];
        HashSet<ulong> playedTeamIDs = [];
        await gate.WaitAsync();
        try
        {
            var teams = RankTeams(await repository.GetTeamsOfGameAsync(gamesId));
            for (var index = 0; index < teams.Count; index++)
            {
                var team = teams[index];
                if (team.WinCount + team.LoseCount > 0)
                {
                    playedTeamIDs.Add(team.TeamID);
                }

                var memberUids = (await repository.GetMembersAsync(team.TeamID)).Select(member => member.GameUlongUID).ToArray();
                archived.Add(new TenGamesArchivedTeam(new TenGamesHistory
                {
                    GamesId = gamesId,
                    TeamID = team.TeamID,
                    TeamName = team.TeamName,
                    TeamLeaderUid = team.TeamLeaderUid,
                    TotalScore = team.CurScore,
                    RankNo = index + 1,
                }, memberUids));
            }

            await repository.ArchiveGameAsync(gamesId, archived);
        }
        finally
        {
            gate.Release();
        }

        logger.LogInformation("Ten games {GamesId} ended: {TeamCount} teams archived", gamesId, archived.Count);
        foreach (var team in archived)
        {
            await MailRewardAsync(gamesId, team, playedTeamIDs.Contains(team.History.TeamID));
        }
    }

    private async Task MailRewardAsync(int gamesId, TenGamesArchivedTeam team, bool played)
    {
        if (!played)
        {
            return;
        }

        var rankNo = team.History.RankNo;
        var rankReward = gameData.TenGamesRankReward.Rows.FirstOrDefault(reward =>
            reward.GamesId == gamesId
            && reward.IsParticipationAward != ParticipationAward
            && reward.StartRank <= rankNo
            && rankNo <= reward.EndRank);
        var (dropID, text, mailID) = rankReward is not null
            ? (rankReward.RewardDropID, RankRewardMailText, RankRewardMailID)
            : (schedule.Find(gamesId)?.Row.JoinReward ?? 0, JoinRewardMailText, JoinRewardMailID);
        var attachments = BuildAttachments(dropID);
        foreach (var uid in team.MemberUids)
        {
            foreach (var chunk in attachments.Chunk(MailBox.MaxMailAccess))
            {
                await mail.SendSystemMailAsync(uid, new SystemMail(text, text, chunk, MailID: mailID));
            }
        }
    }

    private pbCOMDT_ONE_RESOURCE_AWARD_INFO[] BuildAttachments(int dropID)
    {
        var attachments = new List<pbCOMDT_ONE_RESOURCE_AWARD_INFO>();
        foreach (var drop in gameData.DropStatic.Rows.Where(drop => drop.SDropID == dropID).OrderBy(drop => drop.SDropSeq))
        {
            var award = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
            {
                ResID = drop.ResID,
                ResCnt = drop.ResNum,
                ResType = (int)CoinID.GetResIDType(drop.ResID),
            };
            if (mail.CanAttach(award))
            {
                attachments.Add(award);
            }
            else
            {
                logger.LogWarning("Ten games reward drop {DropID} has resource {ResID} x{ResNum} that mail cannot attach", dropID, drop.ResID, drop.ResNum);
            }
        }

        return [.. attachments];
    }

    private static List<TenGamesTeam> RankTeams(IEnumerable<TenGamesTeam> teams) =>
        [.. teams.OrderByDescending(team => team.CurScore).ThenByDescending(team => team.WinCount).ThenBy(team => team.TeamID)];

    private async Task<pbCOMDT_TEN_GAMES_TEAM_INFO> BuildTeamInfoAsync(TenGamesTeam team) =>
        await builder.BuildTeamInfoAsync(team, (await repository.GetMembersAsync(team.TeamID)).Select(member => member.GameUlongUID));

    private async Task<(TenGamesMembership? Membership, PB_ENUM_RES_ERROR_CODE? Refusal)> FindOwnTeamAsync(ulong uid, long teamID)
    {
        if (await FindMembershipAsync(uid) is not { } membership)
        {
            return (null, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesNotInTeam);
        }

        return (long)membership.Team.TeamID == teamID ? (membership, null) : (null, PB_ENUM_RES_ERROR_CODE.PbErrTenGamesTeamNotExist);
    }

    private async Task<PB_ENUM_RES_ERROR_CODE?> FindLeaderTeamAsync(ulong uid, long teamID)
    {
        var (membership, refusal) = await FindOwnTeamAsync(uid, teamID);
        if (refusal is not null)
        {
            return refusal;
        }

        if (membership!.Team.TeamLeaderUid != uid)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrTenGamesNotTeamLeader;
        }

        return membership.Team.JoinTenGamesID != NotJoined ? PB_ENUM_RES_ERROR_CODE.PbErrTenGamesApplying : null;
    }

    public async Task<GamePacket> BuildInviteRedDotAsync(ulong uid) =>
        await CountUnreadInvitesAsync(uid) is var unreadInvites and > 0
            ? GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, LobbyNotices.BuildRedDotReplaceNtf(InviteRedDot, unreadInvites))
            : GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, LobbyNotices.BuildRedDotClearNtf(InviteRedDot));

    private async Task<int> CountUnreadInvitesAsync(ulong uid)
    {
        var lastRead = (await repository.GetRoleAsync(uid)).TenGamesLastReadInviteTime;
        return (await repository.GetInvitesAsync(uid)).Count(invite => invite.InvitedTime >= lastRead);
    }

    private async Task<bool> IsPTCSAsync(ulong uid) =>
        (await accounts.FindBoundChannelsAsync(uid)).Contains(LoginPlatform.POKEMON);

    private async Task PushTeamChangeToFriendsAsync(ulong uid, ulong teamID)
    {
        var change = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidFriendTenGamesTeamChgNtf, new pbSCFriendTenGamesTeamChgNtf
        {
            Uid = uid,
            TenGamesTeamID = (long)teamID,
        });
        foreach (var friend in await friends.GetFriendsAsync(uid))
        {
            await lobbyRoles.PushAsync(friend.FriendUid, change);
        }
    }

    private async Task SendTeamMailAsync(ulong uid, string subject, string content, int mailID) =>
        await mail.SendSystemMailAsync(uid, new SystemMail(subject, content, [], MailID: mailID));

    private bool IsValidTeamName(string teamName)
    {
        var maxLength = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalTengamesTeamNameLen);
        return teamName.Length > 0
            && teamName.Trim().Length == teamName.Length
            && new StringInfo(teamName).LengthInTextElements <= maxLength
            && System.Text.Encoding.UTF8.GetByteCount(teamName) <= MaxTeamNameBytes
            && !teamName.Any(char.IsControl);
    }

    private long Now() => clock.GetUtcNow().ToUnixTimeSeconds();

    private static pbResultInfo Result(PB_ENUM_RES_ERROR_CODE result) => new() { ResultId = (int)result };

    private static TResponse Refuse<TResponse>(TResponse response, pbResultInfo resultInfo, PB_ENUM_RES_ERROR_CODE result)
    {
        resultInfo.ResultId = (int)result;
        return response;
    }
}
