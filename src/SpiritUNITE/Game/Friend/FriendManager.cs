using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Network;

namespace SpiritUNITE.Game.Friend;

public sealed class FriendManager
{
    public const int MaxFriendNum = (int)PB_MACRO_MAX_FRIEND_NUM.PbMaxFriendNum;
    public const int MaxFriendRequestNum = (int)PB_MACRO_MAX_FRIEND_REQUEST_NUM.PbMaxFriendRequestNum;
    public const int MaxFriendBlackListLen = (int)PB_MACRO_MAX_FRIEND_BLACK_LIST_LEN.PbMaxFriendBlackListLen;
    public const int MaxSearchResultNum = 10;

    private const int MaxFriendListOnceNtfNum = (int)PB_MACRO_MAX_FRIEND_LIST_ONCE_NTF_NUM.PbMaxFriendListOnceNtfNum;
    private const ulong DenyAllRequestBit = 1;
    private const int DenyAllRequestOn = 1;
    private const int BaseOnName = (int)PB_ENUM_ENM_SEARCH_BASE_ON_TYPE.PbEnmSearchBaseOnTypeName;
    private const int BaseOnShortId = (int)PB_ENUM_ENM_SEARCH_BASE_ON_TYPE.PbEnmSearchBaseOnTypeShortid;
    private const int Online = 1;
    private const int Offline = 0;
    private const long LoggingInLastLogoutTime = 0;

    private readonly FriendRepository repository;
    private readonly RoleInfoStore roles;
    private readonly RoleSessions sessions;
    private readonly FriendPresence presence;
    private readonly RoleBriefBuilder briefs;
    private readonly SettingsManager settings;
    private readonly GameData gameData;
    private readonly TimeProvider clock;
    private readonly ILogger<FriendManager> logger;

    public FriendManager(
        FriendRepository repository,
        RoleInfoStore roles,
        RoleSessions sessions,
        FriendPresence presence,
        RoleBriefBuilder briefs,
        SettingsManager settings,
        GameData gameData,
        TimeProvider clock,
        ILogger<FriendManager> logger)
    {
        this.repository = repository;
        this.roles = roles;
        this.sessions = sessions;
        this.presence = presence;
        this.briefs = briefs;
        this.settings = settings;
        this.gameData = gameData;
        this.clock = clock;
        this.logger = logger;
        sessions.RoleOnline += OnRoleOnlineAsync;
        sessions.RoleOffline += OnRoleOfflineAsync;
        presence.GameStateChanged += NotifyGameStateAsync;
    }

    public async Task<GamePacket[]> BuildFriendDataPacketsAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        var social = await repository.GetSocialAsync(uid);
        var friendList = await BuildFriendInfosAsync(uid);
        var friendData = new pbSCFriendDataNtf
        {
            ResultInfo = new pbResultInfo(),
            FriendBasicInfo = EmptyChildren.Fill(new pbCOMDT_FRIEND_BASIC_INFO
            {
                InteractionBitMasks = social.DenyAllRequestFlag == DenyAllRequestOn ? DenyAllRequestBit : 0,
            }),
            SelfBriefInfo = await briefs.BuildRoleBriefInfoAsync(role, LoggingInLastLogoutTime),
            FriendTotalNum = friendList.Length,
        };
        return
        [
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidFriendDataNtf, friendData),
            .. friendList.Chunk(MaxFriendListOnceNtfNum).Select(chunk =>
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidFriendListNtf, new pbSCFriendListNtf { FriendList = { chunk } })),
        ];
    }

    public async Task<pbCOMDT_SEARCH_PLAYER_RESULT[]> SearchPlayerAsync(string searchString)
    {
        var uids = new List<(ulong Uid, int BaseOnType)>();
        if (RoleProfile.TryParseShortId(searchString, out var shortId))
        {
            uids.Add((shortId, BaseOnShortId));
        }

        foreach (var nameUid in await repository.SearchRoleUidsByNameAsync(searchString, MaxSearchResultNum + 1))
        {
            if (uids.All(found => found.Uid != nameUid))
            {
                uids.Add((nameUid, BaseOnName));
            }
        }

        var lastLogoutTimes = await repository.GetLastLogoutTimesAsync([.. uids.Select(found => found.Uid)]);
        var results = new List<pbCOMDT_SEARCH_PLAYER_RESULT>();
        foreach (var (foundUid, baseOnType) in uids.Take(MaxSearchResultNum))
        {
            if (await roles.FindAsync(foundUid) is not { } found)
            {
                continue;
            }

            var online = presence.IsOnline(foundUid);
            results.Add(new pbCOMDT_SEARCH_PLAYER_RESULT
            {
                BriefInfo = await briefs.BuildRoleBriefInfoAsync(found, lastLogoutTimes.GetValueOrDefault(foundUid)),
                BaseOnType = baseOnType,
                Online = online ? Online : Offline,
                SelfInvisibleInfo = online ? presence.BuildInvisibleInfo(foundUid) : null,
            });
        }

        return [.. results];
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> AddFriendAsync(LMNRoleInfo role, ulong targetUid, int fromType, int selfCamp)
    {
        var uid = role.GameUlongUID;
        if (targetUid == uid)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendAddSelf;
        }

        if (await roles.FindAsync(targetUid) is not { } target)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendNotReady;
        }

        if (await repository.FindFriendAsync(uid, targetUid) is not null)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendAlready;
        }

        if (await repository.IsBlackedAsync(targetUid, uid))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendInBlackList;
        }

        if (await repository.DeleteBlackAsync(uid, targetUid))
        {
            await sessions.PushAsync(uid, GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidFriendBlackNodeDeletedNtf,
                new pbSCFriendBlackNodeDeletedNtf { Uid = targetUid }));
        }

        if (await repository.HasRequestAsync(uid, targetUid))
        {
            return await AcceptRequestAsync(role, target);
        }

        if ((await repository.GetSocialAsync(targetUid)).DenyAllRequestFlag == DenyAllRequestOn)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendDeny;
        }

        if ((await repository.GetFriendsAsync(uid)).Length >= MaxFriendNum)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendSelfFull;
        }

        var request = new RoleFriendRequest
        {
            GameUlongUID = targetUid,
            RequestUid = uid,
            FromType = fromType,
            SelfCamp = selfCamp,
            RequestTime = clock.GetUtcNow().ToUnixTimeSeconds(),
        };
        await repository.SaveRequestAsync(request, MaxFriendRequestNum);
        logger.LogInformation("Uid {Uid} sent a friend request to uid {TargetUid}", uid, targetUid);
        await sessions.PushAsync(targetUid, GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidNewFriendRequestNtf,
            new pbSCNewFriendRequestNtf { FriendRequest = BuildFriendRequest(request, role, 0) }));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<pbCOMDT_FRIEND_REQUEST[]> GetFriendRequestListAsync(ulong uid)
    {
        var requests = await repository.GetRequestsAsync(uid);
        var list = new List<pbCOMDT_FRIEND_REQUEST>();
        foreach (var request in requests)
        {
            if (await roles.FindAsync(request.RequestUid) is { } requester)
            {
                list.Add(BuildFriendRequest(request, requester, list.Count));
            }
        }

        return [.. list];
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ProcessFriendRequestAsync(LMNRoleInfo role, ulong requestUid, bool accept)
    {
        if (!await repository.HasRequestAsync(role.GameUlongUID, requestUid))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendNoRequest;
        }

        if (!accept)
        {
            await repository.DeleteRequestAsync(role.GameUlongUID, requestUid);
            logger.LogInformation("Uid {Uid} ignored the friend request of uid {RequestUid}", role.GameUlongUID, requestUid);
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        }

        if (await roles.FindAsync(requestUid) is not { } requester)
        {
            await repository.DeleteRequestAsync(role.GameUlongUID, requestUid);
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendNotReady;
        }

        return await AcceptRequestAsync(role, requester);
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> AcceptCurrentAllFriendRequestsAsync(LMNRoleInfo role)
    {
        var result = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        foreach (var request in await repository.GetRequestsAsync(role.GameUlongUID))
        {
            if (await roles.FindAsync(request.RequestUid) is not { } requester)
            {
                await repository.DeleteRequestAsync(role.GameUlongUID, request.RequestUid);
                continue;
            }

            var accepted = await AcceptRequestAsync(role, requester);
            if (accepted == PB_ENUM_RES_ERROR_CODE.PbErrFriendSelfFull)
            {
                return accepted;
            }

            if (accepted != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                result = accepted;
            }
        }

        return result;
    }

    public async Task IgnoreCurrentAllFriendRequestsAsync(ulong uid)
    {
        var deleted = await repository.DeleteAllRequestsAsync(uid);
        logger.LogInformation("Uid {Uid} ignored {Count} friend requests", uid, deleted);
    }

    public Task SetDenyAllRequestFlagAsync(ulong uid, bool denyAllRequest) =>
        repository.SaveDenyAllRequestFlagAsync(uid, denyAllRequest ? DenyAllRequestOn : 0);

    public async Task<PB_ENUM_RES_ERROR_CODE> DeleteFriendAsync(ulong uid, ulong friendUid)
    {
        if (!await repository.DeleteFriendshipAsync(uid, friendUid))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendNotFriend;
        }

        logger.LogInformation("Uid {Uid} deleted friend uid {FriendUid}", uid, friendUid);
        await PushFriendDeletedAsync(uid, friendUid);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<pbCSDT_FRIEND_BLACK_LIST_INFO[]> GetFriendBlackListAsync(ulong uid)
    {
        var list = new List<pbCSDT_FRIEND_BLACK_LIST_INFO>();
        foreach (var black in await repository.GetBlackListAsync(uid))
        {
            if (await roles.FindAsync(black.BlackUid) is { } blackRole)
            {
                list.Add(BuildBlackListInfo(blackRole));
            }
        }

        return [.. list];
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> AddFriendBlackListAsync(ulong uid, ulong blackUid)
    {
        if (blackUid == uid)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        if (await roles.FindAsync(blackUid) is not { } blackRole)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendNotReady;
        }

        var (result, wasFriend) = await repository.AddBlackAsync(uid, blackUid, clock.GetUtcNow().ToUnixTimeSeconds(), MaxFriendBlackListLen);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return result;
        }

        logger.LogInformation("Uid {Uid} blocked uid {BlackUid}", uid, blackUid);
        if (wasFriend)
        {
            await PushFriendDeletedAsync(uid, blackUid);
        }

        await sessions.PushAsync(uid, GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidNewFriendBlackNodeNtf,
            new pbSCNewFriendBlackNodeNtf { NewNode = BuildBlackListInfo(blackRole) }));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> DeleteFriendBlackListAsync(ulong uid, ulong blackUid)
    {
        if (!await repository.DeleteBlackAsync(uid, blackUid))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        logger.LogInformation("Uid {Uid} unblocked uid {BlackUid}", uid, blackUid);
        await sessions.PushAsync(uid, GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidFriendBlackNodeDeletedNtf,
            new pbSCFriendBlackNodeDeletedNtf { Uid = blackUid }));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetBanChatFlagAsync(ulong uid, ulong friendUid, int banChatFlag) =>
        await repository.SetBanChatFlagAsync(uid, friendUid, banChatFlag)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrFriendNotFriend;

    public Task<RoleFriend?> FindFriendAsync(ulong uid, ulong friendUid) => repository.FindFriendAsync(uid, friendUid);

    public Task<bool> IsBlackedAsync(ulong uid, ulong blackUid) => repository.IsBlackedAsync(uid, blackUid);

    public async Task<pbCOMDT_LATELY_PLAYMATE_STATE_INFO[]> GetLatelyPlaymateStatesAsync(IReadOnlyCollection<ulong> uids)
    {
        var lastLogoutTimes = await repository.GetLastLogoutTimesAsync(uids);
        var states = new List<pbCOMDT_LATELY_PLAYMATE_STATE_INFO>();
        foreach (var uid in uids)
        {
            if (await roles.FindAsync(uid) is null)
            {
                continue;
            }

            var gameState = presence.BuildGameState(uid);
            states.Add(EmptyChildren.Fill(new pbCOMDT_LATELY_PLAYMATE_STATE_INFO
            {
                Uid = uid,
                Online = gameState.Online,
                GameStateType = gameState.GameStateType,
                GameRealStartTime = gameState.GameRealStartTime,
                RankedDivision = new pbCOMDT_RANKED_DIVISION_INFO_IN_BRIEF(),
                LastLogoutTime = lastLogoutTimes.GetValueOrDefault(uid),
            }));
        }

        return [.. states];
    }

    private async Task<PB_ENUM_RES_ERROR_CODE> AcceptRequestAsync(LMNRoleInfo role, LMNRoleInfo requester)
    {
        var uid = role.GameUlongUID;
        var requestUid = requester.GameUlongUID;
        var result = await repository.MakeFriendsAsync(uid, requestUid, clock.GetUtcNow().ToUnixTimeSeconds(), MaxFriendNum);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Friend request of uid {RequestUid} to uid {Uid} not accepted: {Result}", requestUid, uid, result);
            return result;
        }

        logger.LogInformation("Uid {Uid} and uid {RequestUid} are now friends", uid, requestUid);
        await repository.DeleteBlackAsync(requestUid, uid);
        await PushNewFriendAsync(uid, requester);
        await PushNewFriendAsync(requestUid, role);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private async Task PushNewFriendAsync(ulong uid, LMNRoleInfo friendRole)
    {
        if (!sessions.IsOnline(uid) || await repository.FindFriendAsync(uid, friendRole.GameUlongUID) is not { } friend)
        {
            return;
        }

        var lastLogoutTimes = await repository.GetLastLogoutTimesAsync([friend.FriendUid]);
        await sessions.PushAsync(uid, GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidNewFriendNtf,
            new pbSCNewFriendNtf { FriendInfo = await BuildFriendInfoAsync(friend, friendRole, lastLogoutTimes.GetValueOrDefault(friend.FriendUid)) }));
    }

    private async Task PushFriendDeletedAsync(ulong uid, ulong friendUid)
    {
        await sessions.PushAsync(uid, GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidFriendDeletedNtf,
            new pbSCFriendDeletedNtf { Uid = friendUid }));
        await sessions.PushAsync(friendUid, GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidFriendDeletedNtf,
            new pbSCFriendDeletedNtf { Uid = uid }));
    }

    private async Task<pbCOMDT_FRIEND_INFO[]> BuildFriendInfosAsync(ulong uid)
    {
        var friends = await repository.GetFriendsAsync(uid);
        var lastLogoutTimes = await repository.GetLastLogoutTimesAsync([.. friends.Select(friend => friend.FriendUid)]);
        var friendInfos = new List<pbCOMDT_FRIEND_INFO>();
        foreach (var friend in friends)
        {
            if (await roles.FindAsync(friend.FriendUid) is { } friendRole)
            {
                friendInfos.Add(await BuildFriendInfoAsync(friend, friendRole, lastLogoutTimes.GetValueOrDefault(friend.FriendUid)));
            }
        }

        return [.. friendInfos];
    }

    private async Task<pbCOMDT_FRIEND_INFO> BuildFriendInfoAsync(RoleFriend friend, LMNRoleInfo friendRole, long lastLogoutTime) => new()
    {
        FriendInfo = new pbCOMDT_FRIEND_INFO_IN_MEM
        {
            Uid = friend.FriendUid,
            GameState = presence.BuildGameState(friend.FriendUid),
            TypeFlags = (int)PB_ENUM_ENM_FRIEND_FLAGS.PbEnmFriendFlagsGame,
            OtherState = new pbCOMDT_OTHER_STATE_INFO(),
            AddFriendTime = friend.AddFriendTime,
        },
        BriefInfo = await briefs.BuildRoleBriefInfoAsync(friendRole, lastLogoutTime),
        FriendInteraction = EmptyChildren.Fill(new pbCOMDT_FRIEND_INTERACTION
        {
            FriendUid = friend.FriendUid,
            BanChatFlag = friend.BanChatFlag,
        }),
    };

    private pbCOMDT_FRIEND_REQUEST BuildFriendRequest(RoleFriendRequest request, LMNRoleInfo requester, int listIndex) =>
        EmptyChildren.Fill(new pbCOMDT_FRIEND_REQUEST
        {
            RequestUid = request.RequestUid,
            RoleName = requester.InGameUsrName,
            RoleLevel = RoleProfile.GetLevel(gameData, requester),
            RoleGender = requester.Gender,
            FromType = request.FromType,
            SelfCamp = request.SelfCamp,
            ListIndex = listIndex,
            RankedDivision = new pbCOMDT_RANKED_DIVISION_INFO_IN_BRIEF(),
            Time = request.RequestTime,
            RoleAvatarInfo = requester.RoleAvatarInfo.Clone(),
        });

    private static pbCSDT_FRIEND_BLACK_LIST_INFO BuildBlackListInfo(LMNRoleInfo blackRole) => new()
    {
        Uid = blackRole.GameUlongUID,
        RoleName = blackRole.InGameUsrName,
        HeadInfo = RoleBrief.BuildHeadInfo(blackRole),
    };

    private async Task OnRoleOnlineAsync(ulong uid)
    {
        var saved = await settings.LoadAsync(uid);
        presence.RestoreInvisible(uid, saved.IsInvisible == RoleSettings.On, clock.GetUtcNow().ToUnixTimeSeconds());
        await NotifyGameStateAsync(uid);
    }

    private async Task OnRoleOfflineAsync(ulong uid)
    {
        presence.Forget(uid);
        var logoutTime = clock.GetUtcNow().ToUnixTimeSeconds();
        await repository.SaveLastLogoutTimeAsync(uid, logoutTime);
        var offlined = GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidFriendOfflinedNtf,
            new pbSCFriendOfflinedNtf { Uid = uid, LogoutTime = logoutTime });
        foreach (var friend in await repository.GetFriendsAsync(uid))
        {
            await sessions.PushAsync(friend.FriendUid, offlined);
        }
    }

    private async Task NotifyGameStateAsync(ulong uid)
    {
        var gameState = GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidFriendGameStateNtf,
            new pbSCFriendGameStateNtf { FriendGameStateList = { presence.BuildUidGameState(uid) } });
        foreach (var friend in await repository.GetFriendsAsync(uid))
        {
            await sessions.PushAsync(friend.FriendUid, gameState);
        }
    }
}
