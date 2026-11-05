using System.Text;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Auth;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Friend;

public sealed class FriendHandlers(RoleInfoStore roles, FriendManager friends, GameData gameData, ILogger<FriendHandlers> logger)
{
    private const int MaximumSearchStringBytes = 32;
    private const int Accepted = 1;
    private const int NoMoreRequest = 0;
    private const int MaximumPlaymateQueryNum = 100;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetFriendDataReq)]
    public async Task<GamePacket[]> OnGetFriendDataReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetFriendDataReq.Parser.ParseFrom(request.PkgData);
        var packets = await friends.BuildFriendDataPacketsAsync(role);
        return [.. packets.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSearchPlayerReq)]
    public async Task<GamePacket[]> OnSearchPlayerReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var searchString = pbCSSearchPlayerReq.Parser.ParseFrom(request.PkgData).SearchString.Trim();
        var response = new pbSCSearchPlayerRsp { ResultInfo = new pbResultInfo(), SearchString = searchString };
        var refusal = ValidateSearchString(searchString);
        if (refusal != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            response.ResultInfo.ResultId = (int)refusal;
            response.SearchString = string.Empty;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSearchPlayerRsp, response)];
        }

        response.PlayerList.Add(await friends.SearchPlayerAsync(searchString));
        logger.LogInformation("Uid {Uid} searched players, {Count} results", request.Uid, response.PlayerList.Count);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSearchPlayerRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAddFriendReq)]
    public async Task<GamePacket[]> OnAddFriendReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var addFriend = pbCSAddFriendReq.Parser.ParseFrom(request.PkgData);
        var fromType = Enum.IsDefined((PB_ENUM_ENM_FRIEND_REQUEST_FROM_TYPE)addFriend.FromType) ? addFriend.FromType : 0;
        var selfCamp = addFriend.SelfCamp is 0 or 1 ? addFriend.SelfCamp : 0;
        var result = await friends.AddFriendAsync(role, addFriend.Uid, fromType, selfCamp);
        var response = new pbSCAddFriendRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            Uid = addFriend.Uid,
            SelfCamp = selfCamp,
            CreditCheckInfo = EmptyChildren.Fill(new pbCOMDT_COMMON_CREDIT_CHECK_INFO()),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAddFriendRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetFriendRequestListReq)]
    public async Task<GamePacket[]> OnGetFriendRequestListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGetFriendRequestListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetFriendRequestListRsp { HaveMoreRequest = NoMoreRequest };
        response.RequestList.Add(await friends.GetFriendRequestListAsync(request.Uid));
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetFriendRequestListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidProcessFriendRequestReq)]
    public async Task<GamePacket[]> OnProcessFriendRequestReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var process = pbCSProcessFriendRequestReq.Parser.ParseFrom(request.PkgData);
        var result = await friends.ProcessFriendRequestAsync(role, process.Uid, process.Accept == Accepted);
        var response = new pbSCProcessFriendRequestRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            Uid = process.Uid,
            Accept = process.Accept == Accepted ? Accepted : 0,
            CreditCheckInfo = EmptyChildren.Fill(new pbCOMDT_COMMON_CREDIT_CHECK_INFO()),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidProcessFriendRequestRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcceptCurrentAllFriendRequestsReq)]
    public async Task<GamePacket[]> OnAcceptCurrentAllFriendRequestsReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSAcceptCurrentAllFriendRequestsReq.Parser.ParseFrom(request.PkgData);
        var result = await friends.AcceptCurrentAllFriendRequestsAsync(role);
        var response = new pbSCAcceptCurrentAllFriendRequestsRsp { ResultInfo = new pbResultInfo { ResultId = (int)result } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcceptCurrentAllFriendRequestsRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidIgnoreCurrentAllFriendRequestsReq)]
    public async Task<GamePacket[]> OnIgnoreCurrentAllFriendRequestsReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSIgnoreCurrentAllFriendRequestsReq.Parser.ParseFrom(request.PkgData);
        await friends.IgnoreCurrentAllFriendRequestsAsync(request.Uid);
        var response = new pbSCIgnoreCurrentAllFriendRequestsRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidIgnoreCurrentAllFriendRequestsRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidDenySubsequentFriendRequestsReq)]
    public async Task<GamePacket[]> OnDenySubsequentFriendRequestsReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var deny = pbCSDenySubsequentFriendRequestsReq.Parser.ParseFrom(request.PkgData);
        if (deny.DenyAllRequestFlag is not (0 or 1))
        {
            return
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidDenySubsequentFriendRequestsRsp, new pbSCDenySubsequentFriendRequestsRsp
                {
                    ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid },
                }),
            ];
        }

        await friends.SetDenyAllRequestFlagAsync(request.Uid, deny.DenyAllRequestFlag == 1);
        var response = new pbSCDenySubsequentFriendRequestsRsp { DenyAllRequestFlag = deny.DenyAllRequestFlag };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidDenySubsequentFriendRequestsRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidDeleteFriendReq)]
    public async Task<GamePacket[]> OnDeleteFriendReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var delete = pbCSDeleteFriendReq.Parser.ParseFrom(request.PkgData);
        var result = await friends.DeleteFriendAsync(request.Uid, delete.Uid);
        var response = new pbSCDeleteFriendRsp { ResultInfo = new pbResultInfo { ResultId = (int)result }, Uid = delete.Uid };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidDeleteFriendRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetFriendBlackListReq)]
    public async Task<GamePacket[]> OnGetFriendBlackListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGetFriendBlackListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetFriendBlackListRsp { ResultInfo = new pbResultInfo() };
        response.FriendBlackList.Add(await friends.GetFriendBlackListAsync(request.Uid));
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetFriendBlackListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAddFriendBlackListReq)]
    public async Task<GamePacket[]> OnAddFriendBlackListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var add = pbCSAddFriendBlackListReq.Parser.ParseFrom(request.PkgData);
        var result = await friends.AddFriendBlackListAsync(request.Uid, add.Uid);
        var response = new pbSCAddFriendBlackListRsp { ResultInfo = new pbResultInfo { ResultId = (int)result }, Uid = add.Uid };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAddFriendBlackListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidDeleteFriendBlackListReq)]
    public async Task<GamePacket[]> OnDeleteFriendBlackListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var delete = pbCSDeleteFriendBlackListReq.Parser.ParseFrom(request.PkgData);
        var result = await friends.DeleteFriendBlackListAsync(request.Uid, delete.Uid);
        var response = new pbSCDeleteFriendBlackListRsp { ResultInfo = new pbResultInfo { ResultId = (int)result }, Uid = delete.Uid };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidDeleteFriendBlackListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidLatelyPlaymateStateReq)]
    public async Task<GamePacket[]> OnLatelyPlaymateStateReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSLatelyPlaymateStateReq.Parser.ParseFrom(request.PkgData);
        if (query.UidList.Count > MaximumPlaymateQueryNum)
        {
            throw new InvalidDataException($"Playmate state query of {query.UidList.Count} uids is too long");
        }

        ulong[] uids = [.. query.UidList.Where(uid => uid > 0).Select(uid => (ulong)uid).Distinct()];
        var response = new pbSCLatelyPlaymateStateRsp();
        response.PlaymateStateList.Add(await friends.GetLatelyPlaymateStatesAsync(uids));
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidLatelyPlaymateStateRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetDailyGiftCoinListReq)]
    public GamePacket[] OnGetDailyGiftCoinListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGetDailyGiftCoinListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetDailyGiftCoinListRsp
        {
            ResultInfo = new pbResultInfo(),
            DailyGiftCoinInfo = new pbCOMDT_DAILY_GIFT_COIN(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetDailyGiftCoinListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetOverSeaPlatformFriendListReq)]
    public GamePacket[] OnGetOverSeaPlatformFriendListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGetOverSeaPlatformFriendListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetOverSeaPlatformFriendListRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetOverSeaPlatformFriendListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetSwitchFriendListReq)]
    public GamePacket[] OnGetSwitchFriendListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGetSwitchFriendListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetSwitchFriendListRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetSwitchFriendListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryPlatformFriendListReq)]
    public GamePacket[] OnQueryPlatformFriendListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSQueryPlatformFriendListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCQueryPlatformFriendListRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQueryPlatformFriendListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.DirServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryRoleBriefInfoReq)]
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryRoleBriefInfoReq)]
    public async Task<GamePacket[]> OnQueryRoleBriefInfoReq(GamePacket request)
    {
        var acntOpenID = pbCSQueryRoleBriefInfoReq.Parser.ParseFrom(request.PkgData).AcntOpenID;
        var response = new pbSCQueryRoleBriefInfoRsp { ResultInfo = new pbResultInfo(), AcntOpenID = acntOpenID };
        var role = AccountSessionManager.TryParseOpenId(acntOpenID, out var uid) ? await roles.FindAsync(uid) : null;
        if (role is null)
        {
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQueryRoleBriefInfoRsp, response)];
        }

        response.RoleName = role.InGameUsrName;
        response.RoleLevel = RoleProfile.GetLevel(gameData, role);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQueryRoleBriefInfoRsp, response)];
    }

    private static PB_ENUM_RES_ERROR_CODE ValidateSearchString(string searchString)
    {
        if (searchString.Length == 0 || searchString.Any(char.IsControl))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendSearchStringInvalid;
        }

        return Encoding.UTF8.GetByteCount(searchString) > MaximumSearchStringBytes
            ? PB_ENUM_RES_ERROR_CODE.PbErrFriendSearchStringTooLong
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }
}
