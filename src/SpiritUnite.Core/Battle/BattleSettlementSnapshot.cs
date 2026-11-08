using System.Text.Json;
using Google.Protobuf;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

internal sealed record SettlementPlayerSnapshot(ulong Uid, bool IsNpc, bool IsGuestAccount, byte[] CampPlayerInfo)
{
    public static SettlementPlayerSnapshot From(DeskPlayer player) =>
        new(player.Uid, player.IsNpc, player.IsGuestAccount, player.CampPlayerInfo.ToByteArray());

    public DeskPlayer Restore() => new(Uid, IsNpc, pbCSDT_CAMPPLAYERINFO.Parser.ParseFrom(CampPlayerInfo), [], new Dictionary<int, int>(), null)
    {
        IsGuestAccount = IsGuestAccount,
    };
}

internal sealed record BattleSettlementSnapshot(
    int Version,
    Guid SettlementID,
    int DeskID,
    long DeskCrtSeq,
    int RelayEntity,
    byte[] DeskInfo,
    byte[]? PveNode,
    SettlementPlayerSnapshot[] Players,
    SettlementPlayerSnapshot[] Judges,
    ulong Uid,
    byte[] Gameover,
    long GameStartTime,
    int GameTime,
    int HangupSeconds)
{
    public static byte[] Serialize(BattleResult result) => JsonSerializer.SerializeToUtf8Bytes(new BattleSettlementSnapshot(
        1,
        result.Desk.SettlementID,
        result.Desk.DeskID,
        result.Desk.DeskCrtSeq,
        result.Desk.RelayEntity,
        result.Desk.DeskInfo.ToByteArray(),
        result.Desk.PveChallengeSelectNode?.ToByteArray(),
        [.. result.Desk.Players.Select(SettlementPlayerSnapshot.From)],
        [.. result.Desk.Judges.Select(SettlementPlayerSnapshot.From)],
        result.Human.Uid,
        result.Gameover.ToByteArray(),
        result.GameStartTime,
        result.GameTime,
        result.HangupSeconds));

    public static BattleResult Deserialize(byte[] payload)
    {
        var snapshot = JsonSerializer.Deserialize<BattleSettlementSnapshot>(payload)
            ?? throw new InvalidDataException("Settlement snapshot is empty");
        if (snapshot.Version != 1)
        {
            throw new InvalidDataException($"Unsupported settlement snapshot version {snapshot.Version}");
        }

        var desk = new BattleDesk(
            snapshot.DeskID,
            snapshot.DeskCrtSeq,
            pbCOMDT_DESKINFO.Parser.ParseFrom(snapshot.DeskInfo),
            snapshot.PveNode is null ? null : pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE.Parser.ParseFrom(snapshot.PveNode),
            [.. snapshot.Players.Select(player => player.Restore())],
            0,
            0)
        {
            SettlementID = snapshot.SettlementID,
            RelayEntity = snapshot.RelayEntity,
            Judges = [.. snapshot.Judges.Select(player => player.Restore())],
            Phase = DeskPhase.Relay,
        };
        return new BattleResult(
            desk,
            desk.FindAttendee(snapshot.Uid) ?? throw new InvalidDataException("Settlement player is missing from the desk"),
            pbCSMultgameGameoverReq.Parser.ParseFrom(snapshot.Gameover),
            snapshot.GameStartTime,
            snapshot.GameTime,
            snapshot.HangupSeconds);
    }
}
