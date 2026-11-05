using System.Collections.Concurrent;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Network;

namespace SpiritUNITE.Game.Friend;

public sealed class FriendPresence(RoleSessions sessions)
{
    private const int Online = 1;
    private const int Offline = 0;

    private readonly ConcurrentDictionary<ulong, (int GameStateType, long GameRealStartTime)> gameStates = new();

    public event Func<ulong, Task>? GameStateChanged;

    public bool IsOnline(ulong uid) => sessions.IsOnline(uid);

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

    public void Forget(ulong uid) => gameStates.TryRemove(uid, out _);

    public pbCOMDT_GAME_STATE_INFO BuildGameState(ulong uid)
    {
        var online = sessions.IsOnline(uid);
        var (gameStateType, gameRealStartTime) = online ? gameStates.GetValueOrDefault(uid) : default;
        return EmptyChildren.Fill(new pbCOMDT_GAME_STATE_INFO
        {
            Online = online ? Online : Offline,
            GameStateType = gameStateType,
            GameRealStartTime = gameRealStartTime,
        });
    }

    public pbCOMDT_UID_GAME_STATE BuildUidGameState(ulong uid) => new()
    {
        Uid = uid,
        GameState = BuildGameState(uid),
        OtherState = new pbCOMDT_OTHER_STATE_INFO(),
    };
}
