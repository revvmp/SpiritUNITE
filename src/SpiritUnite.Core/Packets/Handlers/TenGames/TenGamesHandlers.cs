using SpiritUnite.Game.Role;
using SpiritUnite.Game.TenGames;
using SpiritUnite.Packets.Messages;
using static SpiritUnite.Packets.Messages.PB_ENUM_CS_MSGID_DEF;

namespace SpiritUnite.Packets.Handlers.TenGames;

public sealed class TenGamesHandlers(TenGamesManager tenGames, RoleInfoStore roles)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGetTenGamesTeamInfoReq)]
    public async Task<GamePacket[]> OnGetTenGamesTeamInfoReq(GamePacket request)
    {
        pbCSGetTenGamesTeamInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGetTenGamesTeamInfoRsp, await tenGames.GetTeamInfoAsync(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidCreateTenGamesTeamReq)]
    public async Task<GamePacket[]> OnCreateTenGamesTeamReq(GamePacket request)
    {
        var create = pbCSCreateTenGamesTeamReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidCreateTenGamesTeamRsp, await tenGames.CreateAsync(roles.Require(request.Uid), create.TeamName))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidInviteJoinTenGamesTeamReq)]
    public async Task<GamePacket[]> OnInviteJoinTenGamesTeamReq(GamePacket request)
    {
        var invite = pbCSInviteJoinTenGamesTeamReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidInviteJoinTenGamesTeamRsp, await tenGames.InviteAsync(request.Uid, invite.TeamID, invite.InviteeUid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidRefuseJoinTenGamesTeamReq)]
    public async Task<GamePacket[]> OnRefuseJoinTenGamesTeamReq(GamePacket request)
    {
        var refuse = pbCSRefuseJoinTenGamesTeamReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidRefuseJoinTenGamesTeamRsp, await tenGames.RefuseAsync(request.Uid, refuse.TeamID, refuse.InviterUid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidJoinTenGamesTeamReq)]
    public async Task<GamePacket[]> OnJoinTenGamesTeamReq(GamePacket request)
    {
        var join = pbCSJoinTenGamesTeamReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidJoinTenGamesTeamRsp, await tenGames.JoinAsync(request.Uid, join.TeamID))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidModifyTenGamesTeamNameReq)]
    public async Task<GamePacket[]> OnModifyTenGamesTeamNameReq(GamePacket request)
    {
        var rename = pbCSModifyTenGamesTeamNameReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidModifyTenGamesTeamNameRsp, await tenGames.RenameAsync(request.Uid, rename.TeamID, rename.NewTeamName))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidTenGamesTeamKickMemberReq)]
    public async Task<GamePacket[]> OnTenGamesTeamKickMemberReq(GamePacket request)
    {
        var kick = pbCSTenGamesTeamKickMemberReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidTenGamesTeamKickMemberRsp, await tenGames.KickAsync(request.Uid, kick.TeamID, kick.KickUid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidDissolveTenGamesTeamReq)]
    public async Task<GamePacket[]> OnDissolveTenGamesTeamReq(GamePacket request)
    {
        var dissolve = pbCSDissolveTenGamesTeamReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidDissolveTenGamesTeamRsp, await tenGames.DissolveAsync(request.Uid, dissolve.TeamID))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidLeaveTenGamesTeamReq)]
    public async Task<GamePacket[]> OnLeaveTenGamesTeamReq(GamePacket request)
    {
        var leave = pbCSLeaveTenGamesTeamReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidLeaveTenGamesTeamRsp, await tenGames.LeaveAsync(request.Uid, leave.TeamID))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGetTenGamesInviteListInfoReq)]
    public async Task<GamePacket[]> OnGetTenGamesInviteListInfoReq(GamePacket request)
    {
        pbCSGetTenGamesInviteListInfoReq.Parser.ParseFrom(request.PkgData);
        return await tenGames.GetInviteListAsync(request.Uid, request);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidTransferTenGamesTeamLeaderReq)]
    public async Task<GamePacket[]> OnTransferTenGamesTeamLeaderReq(GamePacket request)
    {
        var transfer = pbCSTransferTenGamesTeamLeaderReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidTransferTenGamesTeamLeaderRsp, await tenGames.TransferLeaderAsync(request.Uid, transfer.TeamID, transfer.NewLeaderUid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGetOtherTenGamesTeamInfoReq)]
    public async Task<GamePacket[]> OnGetOtherTenGamesTeamInfoReq(GamePacket request)
    {
        var other = pbCSGetOtherTenGamesTeamInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGetOtherTenGamesTeamInfoRsp, await tenGames.GetOtherTeamInfoAsync(other.TeamID))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGetTenGamesInfoReq)]
    public async Task<GamePacket[]> OnGetTenGamesInfoReq(GamePacket request)
    {
        pbCSGetTenGamesInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGetTenGamesInfoRsp, await tenGames.GetInfoAsync(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidTenGamesApplyReq)]
    public async Task<GamePacket[]> OnTenGamesApplyReq(GamePacket request)
    {
        var apply = pbCSTenGamesApplyReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidTenGamesApplyRsp, await tenGames.ApplyAsync(request.Uid, apply.GamesId))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGetTenGamesHistoryInfoReq)]
    public async Task<GamePacket[]> OnGetTenGamesHistoryInfoReq(GamePacket request)
    {
        pbCSGetTenGamesHistoryInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGetTenGamesHistoryInfoRsp, await tenGames.GetHistoryAsync(request.Uid))];
    }
}
