using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Accounts;

public sealed class Account(ulong uid, string name)
{
    private const int BitsPerWord = 64;

    private readonly Lock gate = new();
    private readonly List<long> newbieWords = [];

    public ulong Uid { get; } = uid;
    public string Name { get; } = name;
    public int Gender { get; private set; }
    public pbCOMDT_ROLE_AVATAR_INFO Avatar { get; private set; } = new();

    public void SetAppearance(int gender, pbCOMDT_ROLE_AVATAR_INFO avatar)
    {
        lock (gate)
        {
            Gender = gender;
            Avatar = avatar;
        }
    }

    public void MarkNewbieStep(int step)
    {
        lock (gate)
        {
            var word = step / BitsPerWord;
            while (newbieWords.Count <= word)
            {
                newbieWords.Add(0);
            }

            newbieWords[word] |= 1L << (step % BitsPerWord);
        }
    }

    public pbCOMDT_NEWBIE_STATE_INFO BuildNewbieState()
    {
        lock (gate)
        {
            return new pbCOMDT_NEWBIE_STATE_INFO { BitsDetail = { newbieWords } };
        }
    }
}
