using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Bag;
using SpiritUnite.Databin;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.ServerContent;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Pokemon;

public sealed class PokemonManager(
    GameData gameData,
    BagManager bag,
    PokemonUsedRepository usedPokemon,
    CarryEquipmentRepository carryEquipment,
    PokemonAvatarManager pokemonAvatars,
    ServerLists lists)
{
    public const int NoPaster = 0;
    public const int NoTrainerItem = 0;
    public const int RegistUsedPokemonId = 4;
    public const int RecommendCarryEquipmentPlanID = 0;

    private const int MaxSkillSlotNum = (int)PB_MACRO_COM_MAX_SKILLSLOT_NUM.PbComMaxSkillslotNum;
    private const int NoSkillRoute = 0;
    private const int MaxSkillRoute = (int)PB_MACRO_MAX_POKEMON_BASE_BRANCHRECOMMEND_CFG_NUM.PbMaxPokemonBaseBranchrecommendCfgNum;

    public async Task<pbSCGetPokemonListRsp> BuildGetPokemonListRspAsync(LMNRoleInfo role)
    {
        var used = await LoadUsedPokemonAsync(role);
        var carryEquipmentPlans = await carryEquipment.LoadAsync(role.GameUlongUID);
        return new pbSCGetPokemonListRsp
        {
            ResultInfo = new pbResultInfo(),
            PokemonList = { role.BuildPokemonList() },
            UsedPokemonList =
            {
                used.Select(info => info.ToUsedPokemonInfo(
                    carryEquipmentPlans.GetValueOrDefault(info.PokemonId) ?? BuildRecommendCarryEquipmentPlan(info.PokemonId))),
            },
            PasterInfo = await pokemonAvatars.BuildPasterInfoAsync(role),
            FreePokemonList = { lists.Current.FreePokemonList },
        };
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> ChgPokemonCarryEquipmentAsync(
        LMNRoleInfo role,
        int pokemonId,
        pbCOMDT_CARRYEQUIPMENT_PLAN_INFO? plan)
    {
        if (!role.OwnsPokemon(pokemonId) || gameData.PokemonBase.Find(pokemonId) is not { } pokemonBase)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists;
        }

        if (plan is null || plan.CarryEquipmentPlanID != RecommendCarryEquipmentPlanID)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCarryEquipmentPlanIdinvalid;
        }

        if (plan.EquipmentID.Count != pokemonBase.RecommendEquips.Count
            || plan.EquipmentID.Any(equipmentID => !gameData.PokemonEquipment.Contains(equipmentID)))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCarryEquipmentNumInvalid;
        }

        if (plan.EquipmentID.Distinct().Count() != plan.EquipmentID.Count)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCarryEquipmentRepeatedId;
        }

        await carryEquipment.SaveAsync(role.GameUlongUID, pokemonId, plan);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<pbCOMDT_CARRYEQUIPMENT_PLAN_INFO> FindCarryEquipmentPlanAsync(ulong gameUlongUID, int pokemonId) =>
        (await carryEquipment.LoadAsync(gameUlongUID)).GetValueOrDefault(pokemonId) ?? BuildRecommendCarryEquipmentPlan(pokemonId);

    private pbCOMDT_CARRYEQUIPMENT_PLAN_INFO BuildRecommendCarryEquipmentPlan(int pokemonId) => new()
    {
        CarryEquipmentPlanID = RecommendCarryEquipmentPlanID,
        EquipmentID = { gameData.PokemonBase.Find(pokemonId)?.RecommendEquips ?? [] },
    };

    public Task CreateRegistUsedPokemonAsync(ulong gameUlongUID) =>
        usedPokemon.CreateAsync(gameUlongUID, RegistUsedPokemonId);

    public async Task<pbSCUpdateUsedPokemonNtf> AddPokemonPointAsync(LMNRoleInfo role, int pokemonId, int point)
    {
        if (point <= 0 || !role.OwnsPokemon(pokemonId))
        {
            throw new InvalidOperationException($"Pokemon {pokemonId} cannot gain {point} points");
        }

        var used = await usedPokemon.AddPokemonPointAsync(role.GameUlongUID, pokemonId, point);
        return await BuildUpdateUsedPokemonNtfAsync(role.GameUlongUID, used);
    }

    public async Task<pbSCUpdateUsedPokemonNtf> BuildUpdateUsedPokemonNtfAsync(ulong gameUlongUID, UsedPokemonInfo used) => new()
    {
        UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeInfoUpdate,
        UsedPokemonInfo = used.ToUsedPokemonInfo(await FindCarryEquipmentPlanAsync(gameUlongUID, used.PokemonId)),
    };

    public IReadOnlyList<int> FreePokemonList => lists.Current.FreePokemonList;

    public bool CanBattleWith(LMNRoleInfo role, int pokemonId) =>
        role.CanUsePokemon(pokemonId) || FreePokemonList.Contains(pokemonId);

    public async Task<IReadOnlyList<UsedPokemonInfo>> LoadUsedPokemonAsync(LMNRoleInfo role)
    {
        var usablePasterIDs = await pokemonAvatars.LoadUsablePasterIDsAsync(role);
        return
        [
            .. (await usedPokemon.LoadAsync(role.GameUlongUID))
                .Select(used => usablePasterIDs.Contains(used.WearPasterID) ? used : used with { WearPasterID = NoPaster }),
        ];
    }

    public async Task<pbCOMDT_POKEMON_FOLLOW_INFO> BuildFollowInfoAsync(LMNRoleInfo role)
    {
        var followInfo = role.BuildFollowInfo();
        followInfo.WearPasterID = (await LoadUsedPokemonAsync(role))
            .FirstOrDefault(used => used.PokemonId == followInfo.PokemonId)?.WearPasterID ?? NoPaster;
        return followInfo;
    }

    public Task<pbCOMDT_POKEMON_PASTER_LIST> BuildUsablePasterListAsync(LMNRoleInfo role) =>
        pokemonAvatars.BuildUsablePasterListAsync(role);

    public Task<UsedPokemonInfo> FindUsedPokemonAsync(LMNRoleInfo role, int pokemonId) =>
        usedPokemon.FindAsync(role.GameUlongUID, pokemonId);

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, UsedPokemonInfo Used)> CarryTrainerItemAsync(
        LMNRoleInfo role,
        int pokemonId,
        int trainerItemID)
    {
        var used = await usedPokemon.FindAsync(role.GameUlongUID, pokemonId);
        if (!CanBattleWith(role, pokemonId))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrTrainerItemPokemonIdinvalid, used);
        }

        if (trainerItemID != NoTrainerItem)
        {
            if (gameData.OutSideItemBase.Find(trainerItemID) is not { OutSideItemCategory: BagManager.TrainerItemCategory })
            {
                return (PB_ENUM_RES_ERROR_CODE.PbErrTrainerItemIdinvalid, used);
            }

            if (await bag.FindOwnedOutSideItemAsync(role.GameUlongUID, trainerItemID) is null)
            {
                return (PB_ENUM_RES_ERROR_CODE.PbErrTrainerItemNotExists, used);
            }
        }

        used = used with { LastCarryTrainerItemID = trainerItemID };
        await usedPokemon.SaveAsync(role.GameUlongUID, used);
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, used);
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, UsedPokemonInfo Used)> SaveSkillRecommendTypeAsync(
        LMNRoleInfo role,
        int pokemonId,
        int skillRecommendType)
    {
        var used = await usedPokemon.FindAsync(role.GameUlongUID, pokemonId);
        if (!Enum.IsDefined((PB_ENUM_ENM_SKILL_RECOMMEND_TYPE)skillRecommendType))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrSkillRecommendTypeInvalid, used);
        }

        var saved = used.SkillRecommendInfo?.Clone() ?? new pbCOMDT_SKILLRECOMMENDINFO();
        saved.SkillRecommendType = skillRecommendType;
        return await SaveSkillRecommendAsync(role, used, saved);
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, UsedPokemonInfo Used)> SaveUserDefineSkillRecommendAsync(
        LMNRoleInfo role,
        int pokemonId,
        IReadOnlyList<int> skillSlotRecommend)
    {
        var used = await usedPokemon.FindAsync(role.GameUlongUID, pokemonId);
        if (!IsValidSkillSlotRecommend(skillSlotRecommend))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrSkillRecommendTypeInvalid, used);
        }

        return await SaveSkillRecommendAsync(role, used, new pbCOMDT_SKILLRECOMMENDINFO
        {
            SkillRecommendType = (int)PB_ENUM_ENM_SKILL_RECOMMEND_TYPE.PbEnmSkillRecommendTypeUserdefine,
            SkillSlotRecommend = { skillSlotRecommend },
        });
    }

    public static bool IsValidSkillSlotRecommend(IReadOnlyList<int> skillSlotRecommend) =>
        skillSlotRecommend.Count <= MaxSkillSlotNum && skillSlotRecommend.All(option => option is >= NoSkillRoute and <= MaxSkillRoute);

    private async Task<(PB_ENUM_RES_ERROR_CODE Result, UsedPokemonInfo Used)> SaveSkillRecommendAsync(
        LMNRoleInfo role,
        UsedPokemonInfo used,
        pbCOMDT_SKILLRECOMMENDINFO skillRecommendInfo)
    {
        if (!CanBattleWith(role, used.PokemonId))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists, used);
        }

        used = used with { SkillRecommendInfo = skillRecommendInfo };
        await usedPokemon.SaveAsync(role.GameUlongUID, used);
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, used);
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, UsedPokemonInfo Used)> WearPokemonPasterAsync(
        LMNRoleInfo role,
        int pokemonId,
        int pasterID)
    {
        var used = await usedPokemon.FindAsync(role.GameUlongUID, pokemonId);
        if (!CanBattleWith(role, pokemonId))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrPasterWearPokemonIdinvalid, used);
        }

        var result = await pokemonAvatars.ValidateWearAsync(role, pokemonId, pasterID);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return (result, used);
        }

        used = used with { WearPasterID = pasterID };
        await usedPokemon.SaveAsync(role.GameUlongUID, used);
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, used);
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, UsedPokemonInfo Used)> SelectBadgeSchedueAsync(
        LMNRoleInfo role,
        int pokemonId,
        int badgeSchedueId,
        int notUseBadge)
    {
        var used = await usedPokemon.FindAsync(role.GameUlongUID, pokemonId);
        if (!role.OwnsPokemon(pokemonId))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists, used);
        }

        used = used with { LastSchedueId = badgeSchedueId, BattleNotUseBadge = notUseBadge };
        await usedPokemon.SaveAsync(role.GameUlongUID, used);
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, used);
    }
}
