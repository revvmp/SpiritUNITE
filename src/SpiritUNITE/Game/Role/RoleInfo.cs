using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public sealed class RoleInfo(
    ulong gameUlongUID,
    string inGameUsrName,
    int gender,
    pbCOMDT_ROLE_AVATAR_INFO roleAvatarInfo,
    IEnumerable<long> newbieBitsDetail)
{
    private const int BitsPerWord = 64;

    private readonly Lock gate = new();
    private readonly List<long> bitsDetail = [.. newbieBitsDetail];

    public RoleInfo(ulong gameUlongUID, string inGameUsrName)
        : this(gameUlongUID, inGameUsrName, 0, new pbCOMDT_ROLE_AVATAR_INFO(), [])
    {
    }

    public ulong GameUlongUID { get; } = gameUlongUID;
    public string InGameUsrName { get; } = inGameUsrName;
    public int Gender { get; private set; } = gender;
    public pbCOMDT_ROLE_AVATAR_INFO RoleAvatarInfo { get; private set; } = roleAvatarInfo;

    public void SetRoleCosmetic(int gender, pbCOMDT_ROLE_AVATAR_INFO roleAvatarInfo)
    {
        lock (gate)
        {
            Gender = gender;
            RoleAvatarInfo = roleAvatarInfo;
        }
    }

    public void SetNewbieState(int newbie)
    {
        lock (gate)
        {
            var word = newbie / BitsPerWord;
            while (bitsDetail.Count <= word)
            {
                bitsDetail.Add(0);
            }

            bitsDetail[word] |= 1L << (newbie % BitsPerWord);
        }
    }

    public pbCOMDT_NEWBIE_STATE_INFO BuildNewbieState()
    {
        lock (gate)
        {
            return new pbCOMDT_NEWBIE_STATE_INFO { BitsDetail = { bitsDetail } };
        }
    }
}
