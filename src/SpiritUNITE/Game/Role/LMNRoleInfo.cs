using SpiritUNITE.Databin;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public sealed class LMNRoleInfo(
    ulong gameUlongUID,
    string inGameUsrName,
    int gender,
    pbCOMDT_ROLE_AVATAR_INFO roleAvatarInfo,
    IEnumerable<long> newbieBitsDetail,
    IEnumerable<RolePokemonInfo> ownedPokemon,
    IEnumerable<RoleItemInfo> ownedItems,
    int followPokemonId,
    int usingLobbyBgId,
    int roleTotalExp,
    int roleHistoryMaxTotalExp,
    int firstPokemonId = LMNRoleInfo.NoFirstPokemonId,
    IEnumerable<RolePokemonInfo>? timeLimitedPokemon = null)
{
    public const int NoFirstPokemonId = 0;
    public const int NoFollowPokemonId = 0;
    public const int NoUsingLobbyBgId = 0;
    public const int NoGender = 0;

    private const int BitsPerWord = 64;
    private const int MaxNewbie =
        (int)PB_MACRO_COM_MAX_NEWBIE_STATUS_BITS_NUM.PbComMaxNewbieStatusBitsNum * BitsPerWord - 1;
    private const int CreateRoleNewbie = (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeCreaterole;
    private const int TutorialRewardNewbie = (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeTutorialReward;
    private const int TutorialPickRewardType = 2;

    private static readonly int[] TutorialBattleNewbies =
    [
        (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeTutorialRound1,
        (int)PB_ENUM_RES_NEWBIE_STATUS_TYPE.PbResNewbieStatusTypeTutorialRound2,
    ];

    private readonly Lock gate = new();
    private readonly List<long> bitsDetail = [.. newbieBitsDetail];
    private readonly Dictionary<int, RolePokemonInfo> pokemon = ownedPokemon.ToDictionary(owned => owned.PokemonId);
    private readonly Dictionary<int, RolePokemonInfo> timeLimitedPokemonById =
        (timeLimitedPokemon ?? []).ToDictionary(owned => owned.PokemonId);
    private int mFollowPokemonId = followPokemonId;
    private int mUsingLobbyBgId = usingLobbyBgId;

    private readonly Dictionary<(RoleItemType ItemType, int ItemId), RoleItemInfo> items =
        ownedItems.ToDictionary(item => (item.ItemType, item.ItemId));

    public LMNRoleInfo(ulong gameUlongUID, string inGameUsrName)
        : this(gameUlongUID, inGameUsrName, 0, new pbCOMDT_ROLE_AVATAR_INFO(), [], [], [], NoFollowPokemonId, NoUsingLobbyBgId, 0, 0)
    {
    }

    public ulong GameUlongUID { get; } = gameUlongUID;
    public string InGameUsrName { get; private set; } = inGameUsrName;
    public int Gender { get; private set; } = gender;
    public int RoleTotalExp { get; private set; } = roleTotalExp >= 0
        ? roleTotalExp
        : throw new InvalidDataException("RoleTotalExp cannot be negative");
    public int RoleHistoryMaxTotalExp { get; private set; } = roleHistoryMaxTotalExp >= roleTotalExp
        ? roleHistoryMaxTotalExp
        : throw new InvalidDataException("RoleHistoryMaxTotalExp cannot be below RoleTotalExp");

    public int AddRoleTotalExp(int chgExp)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chgExp);
        lock (gate)
        {
            RoleTotalExp = checked(RoleTotalExp + chgExp);
            RoleHistoryMaxTotalExp = Math.Max(RoleHistoryMaxTotalExp, RoleTotalExp);
            return RoleTotalExp;
        }
    }
    public pbCOMDT_ROLE_AVATAR_INFO RoleAvatarInfo { get; private set; } = roleAvatarInfo;
    public int FirstPokemonId { get; private set; } = firstPokemonId;

    public PB_ENUM_RES_ERROR_CODE SetRoleCosmetic(
        GameData gameData,
        int gender,
        pbCOMDT_ROLE_AVATAR_INFO roleAvatarInfo,
        long gainTime,
        out RoleItemInfo[] grantedSuitItems)
    {
        grantedSuitItems = [];
        if (!RoleImage.IsCreationChoice(gameData, gender, roleAvatarInfo.RoleBaseModelID, roleAvatarInfo.SkinColorID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        if (!CreateRoleShowAvatar.IsCreationChoice(gameData, roleAvatarInfo.AvatarIDList))
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
            grantedSuitItems =
            [
                .. WornAvatarIDs(roleAvatarInfo)
                    .Where(avatarID => CreateRoleShowAvatar.IsSuitItem(gameData, avatarID))
                    .Order()
                    .Select(avatarID => new RoleItemInfo(
                        RoleItemType.AvatarItem,
                        avatarID,
                        gainTime,
                        RoleItemInfo.Permanent,
                        RoleItemInfo.NoNewTag))
                    .Where(AddItem),
            ];
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public RoleItemInfo? GrantItem(
        RoleItemType itemType,
        int itemId,
        long gainTime,
        long timeLimited = RoleItemInfo.Permanent,
        int newTag = RoleItemInfo.ShowNewTag)
    {
        var item = new RoleItemInfo(itemType, itemId, gainTime, timeLimited, newTag);
        lock (gate)
        {
            return AddItem(item) ? item : null;
        }
    }

    public bool OwnsItem(RoleItemType itemType, int itemId)
    {
        lock (gate)
        {
            return items.ContainsKey((itemType, itemId));
        }
    }

    public bool OwnsLobbyBg(GameData gameData, int lobbyBgId) =>
        lobbyBgId == gameData.DefaultLobbyBgId || OwnsItem(RoleItemType.LobbyBg, lobbyBgId);

    public RoleItemInfo[] GetOwnedItems()
    {
        lock (gate)
        {
            return [.. items.Values];
        }
    }

    public RoleItemInfo[] GetOwnedItems(RoleItemType itemType)
    {
        lock (gate)
        {
            return [.. items.Values.Where(item => item.ItemType == itemType).OrderBy(item => item.ItemId)];
        }
    }

    public bool HasRoleAppearance => Gender != NoGender;

    public int GetAvatarSuitSeqID(GameData gameData) => GetOwnedItems(RoleItemType.AvatarItem)
        .Select(item => CreateRoleShowAvatar.GetSuitSeq(gameData, item.ItemId))
        .FirstOrDefault(suitSeq => suitSeq != CreateRoleShowAvatar.NoSuitSeq);

    public pbCOMDT_AVATAR_INFO[] BuildAvatarList() =>
        [.. GetOwnedItems(RoleItemType.AvatarItem).Select(item => item.ToAvatarInfo())];

    public int UsingLobbyBgId
    {
        get
        {
            lock (gate)
            {
                return mUsingLobbyBgId;
            }
        }
    }

    public int GetUsingLobbyBgId(GameData gameData)
    {
        var usingLobbyBgId = UsingLobbyBgId;
        return OwnsLobbyBg(gameData, usingLobbyBgId) ? usingLobbyBgId : gameData.DefaultLobbyBgId;
    }

    public PB_ENUM_RES_ERROR_CODE ChgLobbyBackground(GameData gameData, int lobbyBgId)
    {
        if (!gameData.LobbyBg.Contains(lobbyBgId))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrLobbyBgIdinvalid;
        }

        if (!OwnsLobbyBg(gameData, lobbyBgId))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrLobbyBgCanNotUse;
        }

        lock (gate)
        {
            mUsingLobbyBgId = lobbyBgId;
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public PB_ENUM_RES_ERROR_CODE SetRoleAvatar(
        GameData gameData,
        IReadOnlyCollection<int> avatarIDList,
        long now,
        out RoleItemInfo[] wornAvatarItems)
    {
        wornAvatarItems = [];
        if (!RoleAvatar.IsWearableList(avatarIDList))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        lock (gate)
        {
            if (!HasNewbieBit(CreateRoleNewbie))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrAvatarNotInit;
            }

            if (!avatarIDList.All(avatarID => IsWearable(gameData, avatarID, now)))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrAvatarUnexists;
            }

            RoleAvatarInfo = new pbCOMDT_ROLE_AVATAR_INFO
            {
                AvatarIDList = { avatarIDList },
                RoleBaseModelID = RoleAvatarInfo.RoleBaseModelID,
                SkinColorID = RoleAvatarInfo.SkinColorID,
            };
            wornAvatarItems =
            [
                .. avatarIDList.Distinct()
                    .Select(avatarID => items.GetValueOrDefault((RoleItemType.AvatarItem, avatarID)))
                    .OfType<RoleItemInfo>()
                    .Select(item => item with { NewTag = RoleItemInfo.NoNewTag }),
            ];
            foreach (var item in wornAvatarItems)
            {
                items[(item.ItemType, item.ItemId)] = item;
            }
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public PB_ENUM_RES_ERROR_CODE ClearAvatarNewTag(IReadOnlyCollection<int> avatarIDList, out RoleItemInfo[] cleared)
    {
        cleared = [];
        if (!RoleAvatar.IsNewTagClearList(avatarIDList))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        lock (gate)
        {
            if (!avatarIDList.All(avatarID => items.ContainsKey((RoleItemType.AvatarItem, avatarID))))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrAvatarUnexists;
            }

            cleared =
            [
                .. avatarIDList.Distinct()
                    .Select(avatarID => items[(RoleItemType.AvatarItem, avatarID)])
                    .Where(item => item.NewTag != RoleItemInfo.NoNewTag)
                    .Select(item => item with { NewTag = RoleItemInfo.NoNewTag }),
            ];
            foreach (var item in cleared)
            {
                items[(item.ItemType, item.ItemId)] = item;
            }
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public RoleItemInfo[] ClearNewTags(RoleItemType itemType, IEnumerable<int> itemIds)
    {
        lock (gate)
        {
            RoleItemInfo[] cleared =
            [
                .. itemIds.Distinct()
                    .Select(itemId => items.GetValueOrDefault((itemType, itemId)))
                    .OfType<RoleItemInfo>()
                    .Where(item => item.NewTag != RoleItemInfo.NoNewTag)
                    .Select(item => item with { NewTag = RoleItemInfo.NoNewTag }),
            ];
            foreach (var item in cleared)
            {
                items[(item.ItemType, item.ItemId)] = item;
            }

            return cleared;
        }
    }

    public bool TryClearOwnedNewTag(RoleItemType itemType, int itemId, out RoleItemInfo[] cleared)
    {
        lock (gate)
        {
            if (!items.ContainsKey((itemType, itemId)))
            {
                cleared = [];
                return false;
            }

            cleared = ClearNewTags(itemType, [itemId]);
            return true;
        }
    }

    public int CountNewTags(RoleItemType itemType) =>
        GetOwnedItems(itemType).Count(item => item.NewTag != RoleItemInfo.NoNewTag);

    public pbCOMDT_LOBBY_BACKGROUND_INFO BuildLobbyBackgroundInfo(GameData gameData)
    {
        var owned = GetOwnedItems(RoleItemType.LobbyBg);
        var info = new pbCOMDT_LOBBY_BACKGROUND_INFO { UsingLobbyBgId = GetUsingLobbyBgId(gameData) };
        if (owned.All(item => item.ItemId != gameData.DefaultLobbyBgId))
        {
            info.LobbyBgList.Add(new pbCOMDT_ONE_LOBBY_BACKGROUND_DATA { LobbyBgId = gameData.DefaultLobbyBgId });
        }

        info.LobbyBgList.Add(owned.Select(item => item.ToLobbyBackgroundData()));
        return info;
    }

    public static bool IsNewbieInRange(int newbie) => newbie is >= 0 and <= MaxNewbie;

    public bool IsNewbieStateSet(int newbie)
    {
        lock (gate)
        {
            return HasNewbieBit(newbie);
        }
    }

    public bool TrySetNewbieState(int newbie)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newbie);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(newbie, MaxNewbie);
        lock (gate)
        {
            if (HasNewbieBit(newbie))
            {
                return false;
            }

            SetNewbieBit(newbie);
            return true;
        }
    }

    public PB_ENUM_RES_ERROR_CODE GetTutorialReward(GameData gameData, int pokemonId, long gainTime, int gainChannel)
    {
        if (!IsTutorialRewardPokemon(gameData, pokemonId))
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
            pokemon.TryAdd(pokemonId, new RolePokemonInfo(pokemonId, gainTime, gainChannel, RolePokemonInfo.HasNewTag));
            FirstPokemonId = pokemonId;
            ResolveFollowPokemonId();
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public int FollowPokemonId
    {
        get
        {
            lock (gate)
            {
                return ResolveFollowPokemonId();
            }
        }
    }

    public void ChgInGameUsrName(string inGameUsrName)
    {
        lock (gate)
        {
            InGameUsrName = inGameUsrName;
        }
    }

    public PB_ENUM_RES_ERROR_CODE SetPokemonFollow(int pokemonId)
    {
        lock (gate)
        {
            if (!pokemon.ContainsKey(pokemonId))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists;
            }

            mFollowPokemonId = pokemonId;
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public pbCOMDT_POKEMON_FOLLOW_INFO BuildFollowInfo() => new() { PokemonId = FollowPokemonId };

    public int[] GetOwnedPokemonIds()
    {
        lock (gate)
        {
            return [.. pokemon.Keys];
        }
    }

    public bool OwnsPokemon(int pokemonId)
    {
        lock (gate)
        {
            return pokemon.ContainsKey(pokemonId);
        }
    }

    public bool GrantPokemon(int pokemonId, long gainTime, int gainChannel)
    {
        lock (gate)
        {
            var added = pokemon.TryAdd(pokemonId, new RolePokemonInfo(pokemonId, gainTime, gainChannel, RolePokemonInfo.HasNewTag));
            ResolveFollowPokemonId();
            return added;
        }
    }

    public RolePokemonInfo[] GetOwnedPokemon()
    {
        lock (gate)
        {
            return [.. pokemon.Values];
        }
    }

    public RolePokemonInfo[] GetTimeLimitedPokemon()
    {
        lock (gate)
        {
            return [.. timeLimitedPokemonById.Values];
        }
    }

    public void GrantTimeLimitedPokemon(RolePokemonInfo timeLimited)
    {
        lock (gate)
        {
            timeLimitedPokemonById[timeLimited.PokemonId] = timeLimited;
        }
    }

    public bool CanUsePokemon(int pokemonId)
    {
        lock (gate)
        {
            return pokemon.ContainsKey(pokemonId) || timeLimitedPokemonById.ContainsKey(pokemonId);
        }
    }

    public int CountNewTagPokemon()
    {
        lock (gate)
        {
            return ListUsablePokemon().Count(owned => owned.NewTag == RolePokemonInfo.HasNewTag);
        }
    }

    public RolePokemonInfo? FindPokemon(int pokemonId)
    {
        lock (gate)
        {
            return pokemon.GetValueOrDefault(pokemonId) ?? timeLimitedPokemonById.GetValueOrDefault(pokemonId);
        }
    }

    public RolePokemonInfo? ClearPokemonNewTag(int pokemonId)
    {
        lock (gate)
        {
            var store = pokemon.ContainsKey(pokemonId) ? pokemon : timeLimitedPokemonById;
            if (!store.TryGetValue(pokemonId, out var owned))
            {
                return null;
            }

            var cleared = owned with { NewTag = RolePokemonInfo.NoNewTag };
            store[pokemonId] = cleared;
            return cleared;
        }
    }

    public pbCOMDT_POKEMON_INFO[] BuildPokemonList()
    {
        lock (gate)
        {
            return
            [
                .. ListUsablePokemon()
                    .OrderBy(owned => owned.GainTime)
                    .ThenBy(owned => owned.PokemonId)
                    .Select(owned => owned.ToPokemonInfo()),
            ];
        }
    }

    public pbCOMDT_POKEMON_INFO BuildPokemonInfo(int pokemonId) =>
        FindPokemon(pokemonId)?.ToPokemonInfo() ?? new pbCOMDT_POKEMON_INFO { PokemonId = pokemonId };

    public pbCOMDT_NEWBIE_STATE_INFO BuildNewbieState()
    {
        lock (gate)
        {
            return new pbCOMDT_NEWBIE_STATE_INFO { BitsDetail = { bitsDetail } };
        }
    }

    private IEnumerable<RolePokemonInfo> ListUsablePokemon() =>
        pokemon.Values.Concat(timeLimitedPokemonById.Values.Where(timeLimited => !pokemon.ContainsKey(timeLimited.PokemonId)));

    private int ResolveFollowPokemonId()
    {
        if (!pokemon.ContainsKey(mFollowPokemonId))
        {
            mFollowPokemonId = pokemon.Count == 0 ? NoFollowPokemonId : pokemon.Keys.Min();
        }

        return mFollowPokemonId;
    }

    private static bool IsTutorialRewardPokemon(GameData gameData, int pokemonId) =>
        gameData.NewbieRewardHero.Find(pokemonId)?.RewardType == TutorialPickRewardType;

    private static IEnumerable<int> WornAvatarIDs(pbCOMDT_ROLE_AVATAR_INFO roleAvatarInfo) =>
        roleAvatarInfo.AvatarIDList.Where(id => id != CreateRoleShowAvatar.EmptyAvatarSlot).Distinct();

    private bool AddItem(RoleItemInfo item) => items.TryAdd((item.ItemType, item.ItemId), item);

    private bool IsWearable(GameData gameData, int avatarID, long now) =>
        gameData.DefaultAvatarItem.Contains(avatarID)
        || CreateRoleShowAvatar.IsBodyFeature(gameData, avatarID)
        || (items.TryGetValue((RoleItemType.AvatarItem, avatarID), out var item) && !item.IsExpired(now));

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
