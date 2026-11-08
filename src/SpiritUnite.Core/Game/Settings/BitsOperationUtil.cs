namespace SpiritUnite.Game.Settings;

public static class BitsOperationUtil
{
    public const int BitsPerWord = 64;

    public static bool CheckBit(IList<long> words, int bit)
    {
        var word = bit / BitsPerWord;
        return word < words.Count && (words[word] & Mask(bit)) != 0;
    }

    public static bool SetBit(IList<long> words, int bit)
    {
        if (CheckBit(words, bit))
        {
            return false;
        }

        ReserveBit(words, bit);
        words[bit / BitsPerWord] |= Mask(bit);
        return true;
    }

    public static bool ReserveBit(IList<long> words, int bit)
    {
        var word = bit / BitsPerWord;
        var reserved = false;
        while (words.Count <= word)
        {
            words.Add(0);
            reserved = true;
        }

        return reserved;
    }

    public static bool ClearBit(IList<long> words, int bit)
    {
        if (!CheckBit(words, bit))
        {
            return false;
        }

        words[bit / BitsPerWord] &= ~Mask(bit);
        return true;
    }

    private static long Mask(int bit) => 1L << (bit % BitsPerWord);
}
