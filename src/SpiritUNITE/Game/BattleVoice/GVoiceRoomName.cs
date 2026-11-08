using System.Globalization;
using System.Text.RegularExpressions;
using SpiritUNITE.Battle;
using SpiritUNITE.Game.Room;

namespace SpiritUNITE.Game.BattleVoice;

public readonly record struct GVoiceRoomName(int RoomId, int Seq, int Entity, int Client)
{
    private static readonly Regex NameParts = new(@"^(-?\d{1,11})-(-?\d{1,11})-(-?\d{1,11})-(-?\d{1,11})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    public static bool TryParse(string roomName, out GVoiceRoomName name)
    {
        name = default;
        var match = NameParts.Match(roomName);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var roomId)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seq)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var entity)
            || !int.TryParse(match.Groups[4].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var client))
        {
            return false;
        }

        name = new GVoiceRoomName(roomId, seq, entity, client);
        return true;
    }

    public bool AdmitsMember(ulong uid, RoomManager rooms, DeskManager desks) =>
        rooms.IsGVoiceRoomMember(uid, RoomId, Seq, Entity, Client)
        || desks.IsGVoiceCampMember(uid, RoomId, Seq, Entity, Client);
}
