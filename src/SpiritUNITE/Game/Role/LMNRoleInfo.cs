using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public sealed class LMNRoleInfo(
    ulong gameUlongUID,
    string inGameUsrName,
    int gender,
    pbCOMDT_ROLE_AVATAR_INFO roleAvatarInfo,
    IEnumerable<long> newbieBitsDetail,
    IEnumerable<int> ownedPokemonIds)
{
    private const int BitsPerWord = 64;
    private const int TutorialSkipID = 1;
    private const int TutorialRewardNewbie = (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeTutorialReward;

    private static readonly int[] TutorialBattleNewbies = [1, 2];
    private static readonly int[] TutorialRewardPokemonIds = [724, 4, 661];

    private readonly Lock gate = new();
    private readonly List<long> bitsDetail = [.. newbieBitsDetail];
    private readonly HashSet<int> pokemonIds = [.. ownedPokemonIds];

    public LMNRoleInfo(ulong gameUlongUID, string inGameUsrName)
        : this(gameUlongUID, inGameUsrName, 0, new pbCOMDT_ROLE_AVATAR_INFO(), [], [])
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
            SetNewbieBit(newbie);
        }
    }

    public bool SkipNewbieStep(int skipID)
    {
        if (skipID != TutorialSkipID)
        {
            return false;
        }

        lock (gate)
        {
            foreach (var newbie in TutorialBattleNewbies)
            {
                SetNewbieBit(newbie);
            }
        }

        return true;
    }

    public PB_ENUM_RES_ERROR_CODE GetTutorialReward(int pokemonId)
    {
        if (!TutorialRewardPokemonIds.Contains(pokemonId))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        lock (gate)
        {
            if (!TutorialBattleNewbies.All(HasNewbieBit))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            }

            if (HasNewbieBit(TutorialRewardNewbie))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrRewardAlreadyReceived;
            }

            SetNewbieBit(TutorialRewardNewbie);
            pokemonIds.Add(pokemonId);
        }

        return 0;
    }

    public int[] GetOwnedPokemonIds()
    {
        lock (gate)
        {
            return [.. pokemonIds];
        }
    }

    public pbCOMDT_NEWBIE_STATE_INFO BuildNewbieState()
    {
        lock (gate)
        {
            return new pbCOMDT_NEWBIE_STATE_INFO { BitsDetail = { bitsDetail } };
        }
    }

    private bool HasNewbieBit(int newbie)
    {
        var word = newbie / BitsPerWord;
        return word < bitsDetail.Count && (bitsDetail[word] & (1L << (newbie % BitsPerWord))) != 0;
    }

    private void SetNewbieBit(int newbie)
    {
        var word = newbie / BitsPerWord;
        while (bitsDetail.Count <= word)
        {
            bitsDetail.Add(0);
        }

        bitsDetail[word] |= 1L << (newbie % BitsPerWord);
    }
}
