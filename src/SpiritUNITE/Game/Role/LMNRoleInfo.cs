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
    public const int DefaultLobbyBgId = 1007600001;

    private const int BitsPerWord = 64;
    private const int MaxNewbie =
        (int)PB_MACRO_COM_MAX_NEWBIE_STATUS_BITS_NUM.PbComMaxNewbieStatusBitsNum * BitsPerWord - 1;
    private const int TutorialSkipID = 1;
    private const int CreateRoleNewbie = (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeCreaterole;
    private const int TutorialRewardNewbie = (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeTutorialReward;

    private static readonly int[] TutorialBattleNewbies =
    [
        (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeTutorialRound1,
        (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeTutorialRound2,
    ];
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

    public PB_ENUM_RES_ERROR_CODE SetRoleCosmetic(int gender, pbCOMDT_ROLE_AVATAR_INFO roleAvatarInfo)
    {
        if (!RoleImage.IsCreationChoice(gender, roleAvatarInfo.RoleBaseModelID, roleAvatarInfo.SkinColorID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        if (!CreateRoleShowAvatar.IsCreationChoice(roleAvatarInfo.AvatarIDList))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrAvatarIdNotInCfg;
        }

        lock (gate)
        {
            if (HasNewbieBit(CreateRoleNewbie))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            }

            Gender = gender;
            RoleAvatarInfo = roleAvatarInfo;
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public pbCOMDT_AVATAR_INFO[] BuildAvatarList()
    {
        lock (gate)
        {
            return
            [
                .. RoleAvatarInfo.AvatarIDList
                    .Where(id => id != CreateRoleShowAvatar.EmptyAvatarSlot)
                    .Distinct()
                    .Select(id => new pbCOMDT_AVATAR_INFO { AvatarID = id }),
            ];
        }
    }

    public static pbCOMDT_LOBBY_BACKGROUND_INFO BuildLobbyBackgroundInfo() => new()
    {
        UsingLobbyBgId = DefaultLobbyBgId,
        LobbyBgList = { new pbCOMDT_ONE_LOBBY_BACKGROUND_DATA { LobbyBgId = DefaultLobbyBgId } },
    };

    public bool SetNewbieState(int newbie)
    {
        if (newbie is < 0 or > MaxNewbie)
        {
            return false;
        }

        lock (gate)
        {
            SetNewbieBit(newbie);
        }

        return true;
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

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public int[] GetOwnedPokemonIds()
    {
        lock (gate)
        {
            return [.. pokemonIds];
        }
    }

    public pbCOMDT_POKEMON_INFO[] BuildPokemonList()
    {
        lock (gate)
        {
            return [.. pokemonIds.Order().Select(BuildPokemonInfo)];
        }
    }

    public static pbCOMDT_POKEMON_INFO BuildPokemonInfo(int pokemonId) => new() { PokemonId = pokemonId };

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
