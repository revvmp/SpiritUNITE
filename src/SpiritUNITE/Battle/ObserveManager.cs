using System.Security.Cryptography;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Observe;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Network.Lwip;
using SpiritUNITE.Online;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Battle;

public sealed class ObserveManager
{
    public const int VideoPieceBytes = 32 * 1024;

    private const int RelayIsUDP = 1;
    private const int NewStage = 1;
    private const int AllOver = 1;

    private readonly DeskManager desks;
    private readonly LwipTickets relayTickets;
    private readonly RelayRoles relayRoles;
    private readonly GVoiceOptions gVoice;
    private readonly ILogger<ObserveManager> logger;
    private readonly Lock gate = new();
    private readonly List<ObserveVideo> videos = [];
    private readonly Dictionary<ulong, Watcher> watchers = [];
    private readonly TimeSpan observeDelay;

    public ObserveManager(
        RelayManager relays,
        DeskManager desks,
        LwipTickets relayTickets,
        RelayRoles relayRoles,
        LobbyRoles lobbyRoles,
        GameData gameData,
        IOptions<GVoiceOptions> gVoice,
        ILogger<ObserveManager> logger)
    {
        this.desks = desks;
        this.relayTickets = relayTickets;
        this.relayRoles = relayRoles;
        this.gVoice = gVoice.Value;
        this.logger = logger;
        observeDelay = TimeSpan.FromSeconds(gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalObDelaySeconds));
        relays.GameCreated += Record;
        relays.IsObserverSession = IsRelayWatcher;
        relays.FindObservedGame = FindRelayWatchedGame;
        lobbyRoles.RoleOffline += uid =>
        {
            Quit(uid);
            return Task.CompletedTask;
        };
    }

    public bool IsOnRelayDesk(ulong uid) => desks.FindRelayDesk(uid) is not null;

    public IReadOnlyList<ObservableBattle> FindObservableBattles(ulong requesterUid)
    {
        List<ObserveVideo> snapshot;
        lock (gate)
        {
            Prune();
            snapshot = [.. videos];
        }

        return [.. snapshot
            .Where(video => video.IsFightingLive && video.HasDelayedFrames(observeDelay) && video.Desk.FindAttendee(requesterUid) is null)
            .Select(video => new ObservableBattle(
                video.Desk.DeskID,
                video.Desk.DeskCrtSeq,
                video.Desk.RelayEntity,
                video.Desk.DeskInfo.MapId,
                video.GameStartTime,
                ObserveNumOf(video),
                [.. video.Desk.Humans.Select(player => new ObservablePlayer(player.Uid, player.ChosenHeroID))]))];
    }

    public pbCOMDT_TGWINFO? WatchThroughRelay(ulong uid, ObservableBattle battle)
    {
        if (FindVideo(battle) is not { } video)
        {
            return null;
        }

        var token = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var ticket = new RelayTicket(token, relayTickets.Issue(uid, token));
        Watch(uid, new Watcher(video, ticket));
        logger.LogInformation("Uid {Uid} observes desk {DeskID} through the relay", uid, video.Desk.DeskID);
        return new pbCOMDT_TGWINFO
        {
            IsUDP = RelayIsUDP,
            VPort = relayRoles.AdvertisedPort,
            RelayUrl = relayRoles.AdvertisedHost,
            ZSDCode = ticket.ZSDCode,
            IdcTag = gVoice.IdcTag,
            Token = ticket.Token,
            GVoiceInfo = gVoice.BuildGVoiceInfo(),
        };
    }

    public bool WatchOnLobby(ulong uid, ObservableBattle battle)
    {
        if (FindVideo(battle) is not { } video)
        {
            return false;
        }

        Watch(uid, new Watcher(video, Ticket: null));
        logger.LogInformation("Uid {Uid} observes desk {DeskID} through the lobby", uid, video.Desk.DeskID);
        return true;
    }

    public pbSCGetVideoOfFrapsRsp GetVideoOfFraps(ulong uid, pbCSGetVideoOfFrapsReq request)
    {
        Watcher? watcher;
        lock (gate)
        {
            watcher = watchers.GetValueOrDefault(uid);
        }

        if (watcher is null)
        {
            return Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveVideoNotExist);
        }

        lock (watcher)
        {
            if (request.NewStage == NewStage)
            {
                if (request.Offset < 0)
                {
                    return Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveVideoNotExist);
                }

                watcher.Stage = watcher.Video.NextStage(request.Offset, observeDelay, out var allOver);
                watcher.NextPos = 0;
                if (watcher.Stage is null)
                {
                    return allOver
                        ? new pbSCGetVideoOfFrapsRsp { ResultInfo = new pbResultInfo(), VideoOfFraps = new pbCOMDT_VIDEO_OF_FRAPS { Offset = request.Offset, AllOver = AllOver } }
                        : Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveNoNewVideoContent);
                }
            }

            if (watcher.Stage is not { } stage)
            {
                return Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveNoNewVideoContent);
            }

            var totalNum = (int)((stage.End - stage.Offset + VideoPieceBytes - 1) / VideoPieceBytes);
            if (watcher.NextPos >= totalNum)
            {
                return Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveNoNewVideoContent);
            }

            var pieceOffset = stage.Offset + (long)watcher.NextPos * VideoPieceBytes;
            var pieceLength = (int)Math.Min(VideoPieceBytes, stage.End - pieceOffset);
            var piece = new pbCOMDT_VIDEO_OF_FRAPS
            {
                Offset = (int)pieceOffset,
                Buf = ByteString.CopyFrom(watcher.Video.Read(pieceOffset, pieceLength)),
                TotalNum = totalNum,
                ThisPos = watcher.NextPos,
            };
            watcher.NextPos++;
            return new pbSCGetVideoOfFrapsRsp { ResultInfo = new pbResultInfo(), VideoOfFraps = piece };
        }
    }

    public void Quit(ulong uid)
    {
        Watcher? watcher;
        lock (gate)
        {
            if (!watchers.Remove(uid, out watcher))
            {
                return;
            }

            watcher.Video.ObserveNum--;
            Prune();
        }

        if (watcher.Ticket is { } ticket)
        {
            relayTickets.Revoke(ticket.ZSDCode);
            relayRoles.Close(uid);
        }

        logger.LogInformation("Uid {Uid} stopped observing desk {DeskID}", uid, watcher.Video.Desk.DeskID);
    }

    private void Record(RelayGame game)
    {
        lock (gate)
        {
            Prune();
            videos.Add(new ObserveVideo(game));
        }
    }

    private ObserveVideo? FindVideo(ObservableBattle battle)
    {
        lock (gate)
        {
            return videos.FirstOrDefault(video =>
                video.Desk.DeskID == battle.DeskID && video.Desk.DeskCrtSeq == battle.DeskCrtSeq && video.Desk.RelayEntity == battle.RelayEntity);
        }
    }

    private void Watch(ulong uid, Watcher watcher)
    {
        Quit(uid);
        lock (gate)
        {
            watchers[uid] = watcher;
            watcher.Video.ObserveNum++;
        }
    }

    private bool IsRelayWatcher(ulong uid)
    {
        lock (gate)
        {
            return watchers.GetValueOrDefault(uid) is { Ticket: not null };
        }
    }

    private RelayGame? FindRelayWatchedGame(ulong uid)
    {
        lock (gate)
        {
            return watchers.GetValueOrDefault(uid) is { Ticket: not null } watcher ? watcher.Video.Game : null;
        }
    }

    private void Prune() => videos.RemoveAll(video => video.ObserveNum <= 0 && video.IsOver);

    private int ObserveNumOf(ObserveVideo video)
    {
        lock (gate)
        {
            return video.ObserveNum;
        }
    }

    private static pbSCGetVideoOfFrapsRsp Refused(PB_ENUM_RES_ERROR_CODE code) => new() { ResultInfo = new pbResultInfo { ResultId = (int)code } };

    private sealed record Watcher(ObserveVideo Video, RelayTicket? Ticket)
    {
        public ObserveVideoStage? Stage { get; set; }

        public int NextPos { get; set; }
    }
}
