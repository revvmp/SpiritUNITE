using System.Collections.Concurrent;
using SpiritUnite.Online;

namespace SpiritUnite.Game.Video;

public sealed class VideoPlayback
{
    public const int VideoPlaying = 1;
    public const int VideoStopped = 0;

    private readonly ConcurrentDictionary<ulong, byte> playingRoles = new();

    public VideoPlayback(LobbyRoles lobbyRoles)
    {
        lobbyRoles.RoleOffline += ForgetGoneSessionAsync;
        lobbyRoles.FreshLogin += ForgetGoneSessionAsync;
    }

    private Task ForgetGoneSessionAsync(ulong uid)
    {
        playingRoles.TryRemove(uid, out _);
        return Task.CompletedTask;
    }

    public bool IsPlayingVideo(ulong uid) => playingRoles.ContainsKey(uid);

    public bool SetVideoPlayStatus(ulong uid, int videoPlayStatus)
    {
        switch (videoPlayStatus)
        {
            case VideoPlaying:
                playingRoles[uid] = 0;
                return true;
            case VideoStopped:
                playingRoles.TryRemove(uid, out _);
                return true;
            default:
                return false;
        }
    }
}
