using System.Collections.Frozen;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.HeldItem;

public sealed class HeldItemManager(
    GameData gameData,
    BagManager bag,
    BagRepository bagRepository,
    PokemonUsedRepository usedPokemon,
    HeldItemRepository heldItems,
    LobbyNotices notices,
    TimeProvider clock)
{
    public const int HeldItemLevelUpConsumableId = 1002001001;

    private const int HeldItemSystemConfigId = 1;
    private const int FirstHeldItemSlotIndex = 0;
    private const int GroupIdTypeParam = 1;
    private const int BindPokemonIdTypeParam = 2;
    private const int MegaPokemonIdBase = 1000;
    private const int NoBindPokemon = 0;
    private const int Enabled = 1;
    private const int NoNewTag = 0;
    private const int ShowNewTag = 1;

    private static readonly PB_ENUM_RES_SYSTEM_UNLOCK_TYPE[] HeldItemSlotUnlockTypes =
    [
        PB_ENUM_RES_SYSTEM_UNLOCK_TYPE.PbResSystemUnlockTypeHeldItem,
        PB_ENUM_RES_SYSTEM_UNLOCK_TYPE.PbResSystemUnlockTypeSecondCarry,
        PB_ENUM_RES_SYSTEM_UNLOCK_TYPE.PbResSystemUnlockTypeThirdCarry,
    ];

    private readonly FrozenDictionary<(int GroupId, int Level), pbHeldItemLevelUpDesign> levelUpDesigns =
        gameData.HeldItemLevelUpDesign.Rows.ToFrozenDictionary(design => (design.GroupId, design.Level));

    public int GetHeldItemSlotUnlockNum(LMNRoleInfo role) =>
        HeldItemSlotUnlockTypes.Count(unlockType =>
            gameData.SystemUnlock.Find(pbSystemUnlock.Key(
                (int)unlockType,
                notices.GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeSystemUnlock))) is { } unlock
            && role.RoleTotalExp >= unlock.TrainerExp);

    public async Task<pbCOMDT_HELDITEM_SLOT_UNLOCK_INFO> BuildHeldItemSlotUnlockInfoAsync(LMNRoleInfo role)
    {
        var cleared = await heldItems.LoadClearedSlotRedDotsAsync(role.GameUlongUID);
        return new pbCOMDT_HELDITEM_SLOT_UNLOCK_INFO
        {
            HeldItemSlotUnlockInfo =
            {
                Enumerable.Range(FirstHeldItemSlotIndex, GetHeldItemSlotUnlockNum(role))
                    .Select(slotIndex => new pbCOMDT_HELDITEM_SLOT_UNLOCK
                    {
                        NewTag = cleared.Contains(slotIndex) ? NoNewTag : ShowNewTag,
                    }),
            },
        };
    }

    public async Task<GamePacket[]> BuildHeldItemSlotUnlockPacketsAsync(LMNRoleInfo role, int previousSlotUnlockNum)
    {
        var slotUnlockNum = GetHeldItemSlotUnlockNum(role);
        if (slotUnlockNum <= previousSlotUnlockNum)
        {
            return [];
        }

        return
        [
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidHeldItemSlotNtf, new pbSCHeldItemSlotNtf
            {
                HeldItemSlotUnlockInfo = await BuildHeldItemSlotUnlockInfoAsync(role),
            }),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
            {
                RedDotInfo = new pbCOMDT_RED_DOT_INFO
                {
                    RedDotDetail =
                    {
                        new pbCOMDT_RED_DOT_DETAIL
                        {
                            RedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdUnlockHelditem,
                            RedDotBadgeNum = slotUnlockNum - previousSlotUnlockNum,
                        },
                    },
                },
            }),
        ];
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ClearHeldItemSlotRedotAsync(LMNRoleInfo role, int heldItemSlotIndex)
    {
        if (heldItemSlotIndex < FirstHeldItemSlotIndex || heldItemSlotIndex >= GetHeldItemSlotUnlockNum(role))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await heldItems.ClearSlotRedDotAsync(role.GameUlongUID, heldItemSlotIndex);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ValidateHeldItemDesignAsync(
        LMNRoleInfo role,
        int pokemonId,
        IReadOnlyCollection<int> heldItemIds)
    {
        if (!role.OwnsPokemon(pokemonId))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists;
        }

        if (heldItemIds.Count > GetHeldItemSlotUnlockNum(role))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrHeldItemSlotNotEnough;
        }

        if (heldItemIds.Distinct().Count() != heldItemIds.Count)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemRepeated;
        }

        foreach (var heldItemId in heldItemIds)
        {
            if (gameData.OutSideItemBase.Find(heldItemId) is not { OutSideItemCategory: BagManager.HeldItemCategory } config)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg;
            }

            if (!IsUsableBy(config, pokemonId))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemExclusive;
            }

            if (await bag.FindOwnedOutSideItemAsync(role.GameUlongUID, heldItemId) is null)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemNotFoundResInAcnt;
            }
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, UsedPokemonInfo Used)> ChgHeldItemAsync(
        LMNRoleInfo role,
        int pokemonId,
        IReadOnlyList<int> heldItemIds)
    {
        var used = await usedPokemon.FindAsync(role.GameUlongUID, pokemonId);
        var result = await ValidateHeldItemDesignAsync(role, pokemonId, heldItemIds);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return (result, used);
        }

        used = used with { HeldItemID = [.. heldItemIds] };
        await usedPokemon.SaveAsync(role.GameUlongUID, used);
        return (result, used);
    }

    public async Task<BagOutcome> LevelUpHeldItemAsync(LMNRoleInfo role, int resID)
    {
        if (gameData.OutSideItemBase.Find(resID) is not { OutSideItemCategory: BagManager.HeldItemCategory } config)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg);
        }

        if (config.IsCanLevelUp != Enabled || config.OutSideItemTypeParam.Count <= GroupIdTypeParam)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemCanNotBeLevelUp);
        }

        if (await bag.FindOwnedOutSideItemAsync(role.GameUlongUID, resID) is not { } heldItem)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemNotFoundResInAcnt);
        }

        var groupId = config.OutSideItemTypeParam[GroupIdTypeParam];
        var maxLevel = gameData.HeldItemSystemConfig.Find(HeldItemSystemConfigId)?.MaxLevel ?? 0;
        if (heldItem.Level >= maxLevel
            || !levelUpDesigns.TryGetValue((groupId, heldItem.Level), out var design)
            || !levelUpDesigns.ContainsKey((groupId, heldItem.Level + 1)))
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemLevelMax);
        }

        var consumableId = design.LevelUpCostID != 0 ? design.LevelUpCostID : HeldItemLevelUpConsumableId;
        var consumable = await bag.FindOwnedOutSideItemAsync(role.GameUlongUID, consumableId);
        if (consumable is null || consumable.Cnt < design.LevelUpCost)
        {
            return BagOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrResourceNotEnough);
        }

        var change = new BagChange
        {
            Costs = design.LevelUpCost > 0 ? [new BagCost(RoleItemType.OutSideItem, consumableId, design.LevelUpCost)] : [],
            LevelUp = new BagLevelUp(resID, heldItem.Level),
        };
        return BagOutcome.From(await bagRepository.CommitAsync(
            role.GameUlongUID,
            change,
            clock.GetUtcNow().ToUnixTimeSeconds()));
    }

    private static bool IsUsableBy(pbOutSideItem_Base config, int pokemonId)
    {
        var bindPokemonId = config.OutSideItemTypeParam.Count > BindPokemonIdTypeParam
            ? config.OutSideItemTypeParam[BindPokemonIdTypeParam]
            : NoBindPokemon;
        return bindPokemonId == NoBindPokemon
            || bindPokemonId == pokemonId
            || bindPokemonId % MegaPokemonIdBase == pokemonId;
    }
}
