using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

public sealed class SettleFabulous(
    FabulousRepository repository,
    LobbyRoles lobbyRoles,
    TimeProvider clock,
    ILogger<SettleFabulous> logger)
{
    private static readonly TimeSpan SettleBoardRetention = TimeSpan.FromMinutes(5);

    private readonly Lock gate = new();
    private readonly Dictionary<ulong, SettleBoard> boardsByUid = [];

    public void Open(BattleDesk desk, ulong uid)
    {
        lock (gate)
        {
            var board = boardsByUid.Values.FirstOrDefault(open => open.Desk == desk) ?? new SettleBoard(desk);
            boardsByUid[uid] = board;
        }

        _ = ForgetAsync(uid, desk);
    }

    public async Task<pbSCSettleFabulousRsp> FabulousAsync(ulong uid, ulong beFabulousUid)
    {
        SettleBoard? board;
        int beFabulousCnt;
        lock (gate)
        {
            board = boardsByUid.GetValueOrDefault(uid);
            if (board is null || beFabulousUid == uid || !board.Has(beFabulousUid) || !board.Viewers.Contains(uid))
            {
                logger.LogInformation("Uid {Uid} may not like uid {BeFabulousUid}: no shared settlement", uid, beFabulousUid);
                return Refused(beFabulousUid, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
            }

            if (!board.TryLike(uid, beFabulousUid, out beFabulousCnt))
            {
                logger.LogInformation("Uid {Uid} already liked uid {BeFabulousUid} on desk {DeskID}", uid, beFabulousUid, board.Desk.DeskID);
                return Refused(beFabulousUid, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
            }
        }

        if (board.Desk.FindHuman(beFabulousUid) is not null)
        {
            var total = await repository.AddBeFabulousAsync(beFabulousUid, clock.GetUtcNow().ToUnixTimeSeconds());
            logger.LogInformation("Uid {Uid} liked uid {BeFabulousUid} on desk {DeskID}: {BeFabulousCnt} here, {Total} in all", uid, beFabulousUid, board.Desk.DeskID, beFabulousCnt, total);
        }

        var ntf = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidSettleFabulousNtf, new pbSCSettleFabulousNtf
        {
            FabulousUid = uid,
            BeFabulousUid = beFabulousUid,
            BeFabulousCnt = beFabulousCnt,
        });
        foreach (var viewer in board.Viewers.Where(viewer => viewer != uid))
        {
            await lobbyRoles.PushAsync(viewer, ntf);
        }

        return new pbSCSettleFabulousRsp
        {
            ResultInfo = new pbResultInfo(),
            BeFabulousUid = beFabulousUid,
            BeFabulousCnt = beFabulousCnt,
        };
    }

    public Task<int> BeFabulousCntAsync(ulong uid) => repository.FindBeFabulousCntAsync(uid);

    private static pbSCSettleFabulousRsp Refused(ulong beFabulousUid, PB_ENUM_RES_ERROR_CODE code) => new()
    {
        ResultInfo = new pbResultInfo { ResultId = (int)code },
        BeFabulousUid = beFabulousUid,
    };

    private async Task ForgetAsync(ulong uid, BattleDesk desk)
    {
        await Task.Delay(SettleBoardRetention, clock);
        lock (gate)
        {
            if (boardsByUid.GetValueOrDefault(uid)?.Desk == desk)
            {
                boardsByUid.Remove(uid);
            }
        }
    }

    private sealed class SettleBoard(BattleDesk desk)
    {
        private readonly HashSet<(ulong FabulousUid, ulong BeFabulousUid)> likes = [];
        private readonly Dictionary<ulong, int> beFabulousCnts = [];

        public BattleDesk Desk { get; } = desk;
        public HashSet<ulong> Viewers { get; } = [.. desk.HumanUids];

        public bool Has(ulong uid) => Desk.Players.Any(player => player.Uid == uid);

        public bool TryLike(ulong fabulousUid, ulong beFabulousUid, out int beFabulousCnt)
        {
            if (!likes.Add((fabulousUid, beFabulousUid)))
            {
                beFabulousCnt = beFabulousCnts.GetValueOrDefault(beFabulousUid);
                return false;
            }

            beFabulousCnt = beFabulousCnts.GetValueOrDefault(beFabulousUid) + 1;
            beFabulousCnts[beFabulousUid] = beFabulousCnt;
            return true;
        }
    }
}
