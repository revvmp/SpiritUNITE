using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.PokemonAvatar;

public sealed record SkillEffectsExpandPackUse(
    PB_ENUM_RES_ERROR_CODE Result,
    int NewSkillEffectsExpandPackID,
    pbSCUpdateSkillEffectsExpandPackNtf? UpdateNtf = null);

public sealed record PasterSprayUse(
    PB_ENUM_RES_ERROR_CODE Result,
    pbCOMDT_PASTER_USED_SPRAYER_INFO PasterUseSprayerInfo,
    IReadOnlyList<BagItem> ChangedItems,
    pbSCUpdatePokemonAvatarNtf? UpdateNtf = null);

public sealed class PokemonAvatarManager(GameData gameData, BagManager bag, PasterRepository repository, TimeProvider clock)
{
    public const int NoPasterSpray = 0;

    private const int SprayerDaysParamIndex = 0;
    private const int SprayerPasterSprayParamIndex = 1;
    private const int TimeLimitedSprayType = (int)PB_ENUM_RES_PASTER_SPRAY_TYPE.PbResPasterSprayTypeTimeLimit;
    private const int NoPokemonLimit = 0;
    private const int NoSkillEffectsExpandPack = 0;
    private const int SkillEffectsExpandPackUsing = 1;
    private const int SkillEffectsExpandPackNotUsing = 0;
    private const int SkillEffectsExpandPackModify = (int)PB_ENUM_ENM_POKEMON_AVATAR_UPDATE_TYPE.PbEnmPokemonAvatarUpdateTypeModify;
    private const long SecondsPerDay = 86400;

    public static pbCOMDT_SPECIALEFFECT_INFO BuildSpecialEffectInfo() => new();

    public static PB_ENUM_RES_ERROR_CODE ChangeSpecialEffects(
        LMNRoleInfo role,
        IReadOnlyCollection<pbCSDT_TAKE_SPECIALEFFECT> specialEffectList,
        out pbCSDT_TAKE_SPECIALEFFECT[] specialEffectFailList)
    {
        specialEffectFailList = [.. specialEffectList.Select(specialEffect => specialEffect.Clone())];
        return specialEffectList.Count == 0 || specialEffectList.Any(specialEffect => !role.OwnsPokemon(specialEffect.PokemonID))
            ? PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
    }

    public static PB_ENUM_RES_ERROR_CODE PasterSprayerClearExclusiveRedDot(LMNRoleInfo role, int pokemonID) =>
        role.OwnsPokemon(pokemonID) ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists;

    public static pbSCUpdatePokemonAvatarNtf? BuildGainNtf(IEnumerable<RoleItemInfo> items)
    {
        RoleItemInfo[] pasters = [.. items.Where(item => item.ItemType == RoleItemType.Paster)];
        return pasters.Length == 0
            ? null
            : BuildUpdatePokemonAvatarNtf(
                PB_ENUM_ENM_POKEMON_AVATAR_UPDATE_TYPE.PbEnmPokemonAvatarUpdateTypeAdd,
                pasters.Select(paster => BuildPaster(paster, NoPasterSpray)));
    }

    public static IEnumerable<GamePacket> BuildGainPushes(IEnumerable<RoleItemInfo> items) =>
        BuildGainNtf(items) is { } ntf ? [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonAvatarNtf, ntf)] : [];

    public static pbCOMDT_PASTER BuildPaster(RoleItemInfo paster, int wearPasterSprayID) => new()
    {
        PasterId = paster.ItemId,
        NewTag = paster.NewTag,
        TimeLimited = paster.TimeLimited,
        OwnedTime = paster.GainTime,
        WearPasterSprayID = wearPasterSprayID,
    };

    public static int GetPokemonIDForShare(pbPokemon_Base pokemon) =>
        pokemon.PokemonIDForShare is not 0 and var pokemonIDForShare ? pokemonIDForShare : pokemon.PokemonId;

    public int GetPokemonIDForShare(int pokemonId) =>
        gameData.PokemonBase.Find(pokemonId) is { } pokemon ? GetPokemonIDForShare(pokemon) : pokemonId;

    public async Task<RoleItemInfo[]> ListUsablePastersAsync(LMNRoleInfo role)
    {
        var now = Now;
        var permanent = role.GetOwnedItems(RoleItemType.Paster);
        var timeLimited = (await repository.LoadTimeLimitedAsync(role.GameUlongUID))
            .Where(paster => !paster.IsExpired(now) && !role.OwnsItem(RoleItemType.Paster, paster.ItemId));
        return
        [
            .. permanent.Concat(timeLimited)
                .Where(paster => gameData.PokemonAvatarBase.Contains(paster.ItemId))
                .OrderBy(paster => paster.ItemId),
        ];
    }

    public async Task<IReadOnlySet<int>> LoadUsablePasterIDsAsync(LMNRoleInfo role) =>
        (await ListUsablePastersAsync(role)).Select(paster => paster.ItemId).ToHashSet();

    public async Task<pbCOMDT_POKEMON_PASTER_INFO> BuildPasterInfoAsync(LMNRoleInfo role)
    {
        var wornSprays = await LoadWornSpraysAsync(role);
        var info = new pbCOMDT_POKEMON_PASTER_INFO();
        info.PokemonPasterList.Add((await ListUsablePastersAsync(role))
            .GroupBy(paster => gameData.PokemonAvatarBase.Find(paster.ItemId)!.PokemonId)
            .OrderBy(group => group.Key)
            .Select(group => new pbCOMDT_POKEMON_PASTER
            {
                PokemonID = group.Key,
                PasterList = { group.Select(paster => BuildPaster(paster, wornSprays.GetValueOrDefault(paster.ItemId))) },
            }));
        return info;
    }

    public async Task<pbCOMDT_POKEMON_PASTER_LIST> BuildUsablePasterListAsync(LMNRoleInfo role)
    {
        var wornSprays = await LoadWornSpraysAsync(role);
        return new pbCOMDT_POKEMON_PASTER_LIST
        {
            PasterList = { (await ListUsablePastersAsync(role)).Select(paster => BuildPaster(paster, wornSprays.GetValueOrDefault(paster.ItemId))) },
            SkillEffectsExpandPackInfo = await BuildSkillEffectsExpandPackListAsync(role),
        };
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ValidateWearAsync(LMNRoleInfo role, int pokemonId, int pasterID)
    {
        if (pasterID == PokemonManager.NoPaster)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        }

        var result = ValidatePasterOfPokemon(pokemonId, pasterID);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return result;
        }

        return (await LoadUsablePasterIDsAsync(role)).Contains(pasterID)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrPasterNotExists;
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, bool RoleChanged)> ClearPasterNewTagAsync(LMNRoleInfo role, int pokemonId, int pasterID)
    {
        if (!role.OwnsPokemon(pokemonId))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrPasterWearPokemonIdinvalid, false);
        }

        var result = ValidatePasterOfPokemon(pokemonId, pasterID);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return (result, false);
        }

        if (role.TryClearOwnedNewTag(RoleItemType.Paster, pasterID, out var cleared))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, cleared.Length > 0);
        }

        return await repository.ClearTimeLimitedNewTagAsync(role.GameUlongUID, pasterID)
            ? (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, false)
            : (PB_ENUM_RES_ERROR_CODE.PbErrPasterNotExists, false);
    }

    public async Task<pbCOMDT_SKILL_EFFECTS_EXPAND_PACK_LIST> BuildSkillEffectsExpandPackListAsync(LMNRoleInfo role)
    {
        var owned = OwnedSkillEffectsExpandPacks(role);
        var usingPacks = ResolveUsingSkillEffectsExpandPacks(owned, await repository.LoadUsingSkillEffectsExpandPacksAsync(role.GameUlongUID));
        return new pbCOMDT_SKILL_EFFECTS_EXPAND_PACK_LIST
        {
            SkillEffectsExpandPackList =
            {
                owned.OrderBy(pack => pack.Key).Select(pack => BuildSkillEffectsExpandPackInfo(pack.Key, pack.Value, usingPacks.Contains(pack.Key))),
            },
        };
    }

    public async Task<SkillEffectsExpandPackUse> UseSkillEffectsExpandPackAsync(LMNRoleInfo role, int skillEffectsExpandPackID)
    {
        if (gameData.SkillEffectExpandPackBase.Find(skillEffectsExpandPackID) is not { } pack)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrSkillEffectsExpandPackNotValid, NoSkillEffectsExpandPack);
        }

        if (!IsOnTime(pack.OnTime))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrSkillEffectsExpandPackNotOnTime, NoSkillEffectsExpandPack);
        }

        var owned = OwnedSkillEffectsExpandPacks(role);
        if (!owned.TryGetValue(skillEffectsExpandPackID, out var newTag))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrSkillEffectsExpandPackNotValid, NoSkillEffectsExpandPack);
        }

        var before = ResolveUsingSkillEffectsExpandPacks(owned, await repository.LoadUsingSkillEffectsExpandPacksAsync(role.GameUlongUID));
        await repository.UseSkillEffectsExpandPackAsync(
            role.GameUlongUID,
            pack.PokemonAvatarId,
            pack.SkillEffectsExpandPackType,
            skillEffectsExpandPackID);
        var replaced = before
            .Where(packID => packID != skillEffectsExpandPackID && IsSameSlot(gameData.SkillEffectExpandPackBase.Find(packID), pack))
            .Select(packID => BuildSkillEffectsExpandPackUpdate(packID, owned[packID], SkillEffectsExpandPackNotUsing));
        var ntf = new pbSCUpdateSkillEffectsExpandPackNtf
        {
            SkillEffectsExpandPackUpdateList =
            {
                replaced.Append(BuildSkillEffectsExpandPackUpdate(skillEffectsExpandPackID, newTag, SkillEffectsExpandPackUsing)),
            },
        };
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, skillEffectsExpandPackID, ntf);
    }

    public PB_ENUM_RES_ERROR_CODE ClearSkillEffectsExpandPackNewTag(LMNRoleInfo role, int skillEffectsExpandPackID, out bool roleChanged)
    {
        roleChanged = false;
        if (role.TryClearOwnedNewTag(RoleItemType.SkillEffectsExpandPack, skillEffectsExpandPackID, out var cleared))
        {
            roleChanged = cleared.Length > 0;
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        }

        return OwnedSkillEffectsExpandPacks(role).ContainsKey(skillEffectsExpandPackID)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrSkillEffectsExpandPackNotValid;
    }

    public async Task<pbCOMDT_PASTER_USED_SPRAYER_INFO_LIST> BuildPasterUseSprayerInfoAsync(LMNRoleInfo role)
    {
        var now = Now;
        var info = new pbCOMDT_PASTER_USED_SPRAYER_INFO_LIST();
        info.PasterUseSprayerList.Add((await repository.LoadSpraysAsync(role.GameUlongUID))
            .Where(spray => spray.IsUsable(now) && role.OwnsItem(RoleItemType.Paster, spray.PasterId))
            .GroupBy(spray => spray.PasterId)
            .Select(group => BuildPasterUsedSprayerInfo(group.Key, group)));
        return info;
    }

    public async Task<PasterSprayUse> UseSprayerToPasterAsync(
        LMNRoleInfo role,
        int pasterID,
        int pasterSprayID,
        IReadOnlyCollection<pbCOMDT_USE_SPRAYER_ITEM_TO_PASTER> sprayerItemList)
    {
        var emptyInfo = new pbCOMDT_PASTER_USED_SPRAYER_INFO { PasterId = pasterID };
        var result = ValidateSprayOnPaster(pasterID, pasterSprayID);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return new(result, emptyInfo, []);
        }

        if (sprayerItemList.Count == 0
            || sprayerItemList.Select(item => item.SprayerID).Distinct().Count() != sprayerItemList.Count
            || sprayerItemList.Any(item => item.UseNum <= 0))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum, emptyInfo, []);
        }

        if (sprayerItemList.Any(item => SprayerDays(item.SprayerID, pasterSprayID) is null))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg, emptyInfo, []);
        }

        if (!role.OwnsItem(RoleItemType.Paster, pasterID))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrPasterNotExists, emptyInfo, []);
        }

        foreach (var item in sprayerItemList)
        {
            if (await bag.FindOwnedOutSideItemAsync(role.GameUlongUID, item.SprayerID) is not { } owned || owned.Cnt < item.UseNum)
            {
                return new(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemDeleteNotEnough, emptyInfo, []);
            }
        }

        var durationSec = sprayerItemList.Sum(item => SprayerDays(item.SprayerID, pasterSprayID)!.Value * item.UseNum * SecondsPerDay);
        var commit = await repository.UseSprayerAsync(
            role.GameUlongUID,
            pasterID,
            pasterSprayID,
            [.. sprayerItemList.Select(item => new BagCost(RoleItemType.OutSideItem, item.SprayerID, item.UseNum))],
            durationSec,
            Now);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return new(commit.Result, emptyInfo, []);
        }

        return new(commit.Result, await BuildPasterUsedSprayerInfoAsync(role, pasterID), commit.ChangedItems);
    }

    public async Task<PasterSprayUse> BattlePasterUseSprayerAsync(LMNRoleInfo role, int pasterID, int pasterSprayID)
    {
        var emptyInfo = new pbCOMDT_PASTER_USED_SPRAYER_INFO { PasterId = pasterID };
        if (!gameData.PokemonAvatarBase.Contains(pasterID))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrPasterIdinvalid, emptyInfo, []);
        }

        if (pasterSprayID != NoPasterSpray && !gameData.PasterSpray.Contains(pasterSprayID))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrPasterSprayIdinvalid, emptyInfo, []);
        }

        if (!role.OwnsItem(RoleItemType.Paster, pasterID))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrPasterNotExists, emptyInfo, []);
        }

        if (pasterSprayID != NoPasterSpray)
        {
            var now = Now;
            if (!(await repository.LoadSpraysAsync(role.GameUlongUID))
                    .Any(spray => spray.PasterId == pasterID && spray.PasterSprayID == pasterSprayID && spray.IsUsable(now)))
            {
                return new(PB_ENUM_RES_ERROR_CODE.PbErrPasterSprayNotExist, emptyInfo, []);
            }
        }

        await repository.WearSprayAsync(role.GameUlongUID, pasterID, pasterSprayID);
        var paster = role.GetOwnedItems(RoleItemType.Paster).First(owned => owned.ItemId == pasterID);
        var ntf = BuildUpdatePokemonAvatarNtf(
            PB_ENUM_ENM_POKEMON_AVATAR_UPDATE_TYPE.PbEnmPokemonAvatarUpdateTypeModify,
            [BuildPaster(paster, pasterSprayID)]);
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, await BuildPasterUsedSprayerInfoAsync(role, pasterID), [], ntf);
    }

    private static pbSCUpdatePokemonAvatarNtf BuildUpdatePokemonAvatarNtf(
        PB_ENUM_ENM_POKEMON_AVATAR_UPDATE_TYPE updateType,
        IEnumerable<pbCOMDT_PASTER> pasters) => new()
        {
            PokemonAvatarUpdateList =
            {
                pasters.Select(paster => new pbCOMDT_POKEMON_AVATAR_UPDATE_NODE { UpdateType = (int)updateType, PokemonAvatar = paster }),
            },
        };

    private static pbCOMDT_SKILL_EFFECTS_EXPAND_PACK_INFO BuildSkillEffectsExpandPackInfo(int packID, int newTag, bool isUsing) => new()
    {
        SkillEffectsExpandPackID = packID,
        NewTag = newTag,
        IsUsing = isUsing ? SkillEffectsExpandPackUsing : SkillEffectsExpandPackNotUsing,
    };

    private static pbCOMDT_SKILL_EFFECTS_EXPAND_PACK_UPDATE_NODE BuildSkillEffectsExpandPackUpdate(int packID, int newTag, int isUsing) => new()
    {
        UpdateType = SkillEffectsExpandPackModify,
        SkillEffectsExpandPackInfo = new pbCOMDT_SKILL_EFFECTS_EXPAND_PACK_INFO
        {
            SkillEffectsExpandPackID = packID,
            NewTag = newTag,
            IsUsing = isUsing,
        },
    };

    private static bool IsSameSlot(pbSkillEffectExpandPack_Base? pack, pbSkillEffectExpandPack_Base other) =>
        pack is not null
        && pack.PokemonAvatarId == other.PokemonAvatarId
        && pack.SkillEffectsExpandPackType == other.SkillEffectsExpandPackType;

    private long Now => clock.GetUtcNow().ToUnixTimeSeconds();

    private bool IsOnTime(long onTime) => onTime <= UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());

    private PB_ENUM_RES_ERROR_CODE ValidatePasterOfPokemon(int pokemonId, int pasterID) =>
        gameData.PokemonAvatarBase.Find(pasterID) switch
        {
            null => PB_ENUM_RES_ERROR_CODE.PbErrPasterIdinvalid,
            { } paster when paster.PokemonId != GetPokemonIDForShare(pokemonId) => PB_ENUM_RES_ERROR_CODE.PbErrPasterNotForThisPokemon,
            _ => PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
        };

    private async Task<Dictionary<int, int>> LoadWornSpraysAsync(LMNRoleInfo role)
    {
        var now = Now;
        return (await repository.LoadSpraysAsync(role.GameUlongUID))
            .Where(spray => spray.IsWearing && spray.IsUsable(now))
            .GroupBy(spray => spray.PasterId)
            .ToDictionary(group => group.Key, group => group.First().PasterSprayID);
    }

    private Dictionary<int, int> OwnedSkillEffectsExpandPacks(LMNRoleInfo role)
    {
        var owned = new Dictionary<int, int>();
        foreach (var paster in role.GetOwnedItems(RoleItemType.Paster))
        {
            foreach (var packID in gameData.PokemonAvatarBase.Find(paster.ItemId)?.DefaultExpandEffectPackIDList ?? [])
            {
                if (gameData.SkillEffectExpandPackBase.Find(packID) is { } pack && IsOnTime(pack.OnTime))
                {
                    owned.TryAdd(packID, RoleItemInfo.NoNewTag);
                }
            }
        }

        foreach (var pack in role.GetOwnedItems(RoleItemType.SkillEffectsExpandPack))
        {
            if (gameData.SkillEffectExpandPackBase.Find(pack.ItemId) is { } config && role.OwnsItem(RoleItemType.Paster, config.PokemonAvatarId))
            {
                owned[pack.ItemId] = pack.NewTag;
            }
        }

        return owned;
    }

    private HashSet<int> ResolveUsingSkillEffectsExpandPacks(
        IReadOnlyDictionary<int, int> owned,
        IReadOnlyDictionary<(int PokemonAvatarId, int SkillEffectsExpandPackType), int> chosen)
    {
        var usingPacks = new HashSet<int>();
        foreach (var slot in owned.Keys
                     .Select(packID => gameData.SkillEffectExpandPackBase.Find(packID))
                     .OfType<pbSkillEffectExpandPack_Base>()
                     .GroupBy(pack => (pack.PokemonAvatarId, pack.SkillEffectsExpandPackType)))
        {
            var choice = chosen.TryGetValue(slot.Key, out var chosenPackID) && owned.ContainsKey(chosenPackID)
                ? chosenPackID
                : DefaultPackOf(slot.Key.PokemonAvatarId, slot.Key.SkillEffectsExpandPackType);
            if (choice != NoSkillEffectsExpandPack)
            {
                usingPacks.Add(choice);
            }
        }

        return usingPacks;
    }

    private int DefaultPackOf(int pokemonAvatarId, int skillEffectsExpandPackType) =>
        (gameData.PokemonAvatarBase.Find(pokemonAvatarId)?.DefaultExpandEffectPackIDList ?? [])
            .FirstOrDefault(packID => gameData.SkillEffectExpandPackBase.Find(packID)?.SkillEffectsExpandPackType == skillEffectsExpandPackType);

    private PB_ENUM_RES_ERROR_CODE ValidateSprayOnPaster(int pasterID, int pasterSprayID)
    {
        if (gameData.PasterSpray.Find(pasterSprayID) is not { } spray)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrPasterSprayIdinvalid;
        }

        if (!IsOnTime(spray.OnTime))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrPasterSprayNotRelease;
        }

        if (spray.SprayType != TimeLimitedSprayType)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrPasterSprayPasterLimit;
        }

        if (gameData.PokemonAvatarBase.Find(pasterID) is not { } paster)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrPasterIdinvalid;
        }

        return spray.PokemonLimit != NoPokemonLimit && spray.PokemonLimit != paster.PokemonId
            ? PB_ENUM_RES_ERROR_CODE.PbErrPasterSprayPasterLimit
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private long? SprayerDays(int sprayerID, int pasterSprayID) =>
        gameData.OutSideItemBase.Find(sprayerID) is { OutSideItemType: (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeSprayer } sprayer
        && sprayer.ItemParam.ElementAtOrDefault(SprayerPasterSprayParamIndex) == pasterSprayID
        && sprayer.ItemParam.ElementAtOrDefault(SprayerDaysParamIndex) is > 0 and var days
            ? days
            : null;

    private async Task<pbCOMDT_PASTER_USED_SPRAYER_INFO> BuildPasterUsedSprayerInfoAsync(LMNRoleInfo role, int pasterID)
    {
        var now = Now;
        var sprays = (await repository.LoadSpraysAsync(role.GameUlongUID))
            .Where(spray => spray.PasterId == pasterID && spray.IsUsable(now));
        return BuildPasterUsedSprayerInfo(pasterID, sprays);
    }

    private pbCOMDT_PASTER_USED_SPRAYER_INFO BuildPasterUsedSprayerInfo(int pasterID, IEnumerable<PasterSprayInfo> sprays)
    {
        PasterSprayInfo[] usable = [.. sprays];
        var info = new pbCOMDT_PASTER_USED_SPRAYER_INFO
        {
            PasterId = pasterID,
            SprayerList = { usable.Select(spray => spray.ToSprayerInfo(SprayBuffOf(spray.PasterSprayID))) },
        };
        if (usable.FirstOrDefault(spray => spray.IsWearing) is { } worn)
        {
            info.CurUsedSprayerInfo = worn.ToSprayerInfo(SprayBuffOf(worn.PasterSprayID));
        }

        return info;
    }

    private int SprayBuffOf(int pasterSprayID) => gameData.PasterSpray.Find(pasterSprayID)?.SprayBuffID ?? 0;
}
