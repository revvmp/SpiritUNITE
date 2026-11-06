using System.Collections.Concurrent;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Online;

namespace SpiritUNITE.Game.Friend;

public sealed class FriendPresence(LobbyRoles lobbyRoles)
{
    private const int Online = 1;
    private const int Offline = 0;

    private const int Invisible = 1;

    private readonly ConcurrentDictionary<ulong, (int GameStateType, long GameRealStartTime)> gameStates = new();
    private readonly ConcurrentDictionary<ulong, pbCOMDT_PLAYER_INVISIBLE_INFO> invisibleInfos = new();

    public event Func<ulong, Task>? GameStateChanged;

    public bool IsOnline(ulong uid) => lobbyRoles.IsOnline(uid);

    public void RestoreInvisible(ulong uid, bool isInvisible, long loginTime)
    {
        if (isInvisible)
        {
            invisibleInfos[uid] = new pbCOMDT_PLAYER_INVISIBLE_INFO { IsInvisible = Invisible, SelfInvisibleTime = loginTime };
        }
        else
        {
            invisibleInfos.TryRemove(uid, out _);
        }
    }

    public async Task SetInvisibleAsync(ulong uid, PB_ENUM_ENM_INVISIBLE_TYPE invisibleType, long changeTime)
    {
        switch (invisibleType)
        {
            case PB_ENUM_ENM_INVISIBLE_TYPE.PbEnmInvisibleTypeTemp:
                invisibleInfos[uid] = new pbCOMDT_PLAYER_INVISIBLE_INFO { IsInvisible = Invisible, SelfInvisibleTime = changeTime };
                break;
            case PB_ENUM_ENM_INVISIBLE_TYPE.PbEnmInvisibleTypeForever:
                invisibleInfos[uid] = new pbCOMDT_PLAYER_INVISIBLE_INFO { IsForeverInvisible = Invisible, ForeverInvisibleTime = changeTime };
                break;
            case PB_ENUM_ENM_INVISIBLE_TYPE.PbEnmInvisibleTypeOnline:
                invisibleInfos.TryRemove(uid, out _);
                break;
            default:
                return;
        }

        if (GameStateChanged is { } changed)
        {
            await changed(uid);
        }
    }

    public pbCOMDT_PLAYER_INVISIBLE_INFO BuildInvisibleInfo(ulong uid) =>
        invisibleInfos.TryGetValue(uid, out var invisibleInfo) ? invisibleInfo.Clone() : new pbCOMDT_PLAYER_INVISIBLE_INFO();

    public async Task SetGameStateAsync(ulong uid, PB_ENUM_E_ACNT_GAMESTATE gameStateType, long gameRealStartTime)
    {
        if (gameStateType == PB_ENUM_E_ACNT_GAMESTATE.PbEacntGameidle)
        {
            gameStates.TryRemove(uid, out _);
        }
        else
        {
            gameStates[uid] = ((int)gameStateType, gameRealStartTime);
        }

        if (GameStateChanged is { } changed)
        {
            await changed(uid);
        }
    }

    public async Task LeaveGameAsync(ulong uid)
    {
        if (!gameStates.TryGetValue(uid, out var state)
            || state.GameStateType != (int)PB_ENUM_E_ACNT_GAMESTATE.PbEacntGamemulti
            || !gameStates.TryRemove(KeyValuePair.Create(uid, state)))
        {
            return;
        }

        if (GameStateChanged is { } changed)
        {
            await changed(uid);
        }
    }

    public void Forget(ulong uid)
    {
        gameStates.TryRemove(uid, out _);
        invisibleInfos.TryRemove(uid, out _);
    }

    public pbCOMDT_GAME_STATE_INFO BuildGameState(ulong uid)
    {
        var online = lobbyRoles.IsOnline(uid);
        var (gameStateType, gameRealStartTime) = online ? gameStates.GetValueOrDefault(uid) : default;
        return EmptyChildren.Fill(new pbCOMDT_GAME_STATE_INFO
        {
            Online = online ? Online : Offline,
            GameStateType = gameStateType,
            GameRealStartTime = gameRealStartTime,
            SelfInvisibleInfo = online ? BuildInvisibleInfo(uid) : null,
        });
    }

    public pbCOMDT_UID_GAME_STATE BuildUidGameState(ulong uid) => new()
    {
        Uid = uid,
        GameState = BuildGameState(uid),
        OtherState = new pbCOMDT_OTHER_STATE_INFO(),
    };
}
