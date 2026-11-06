using Microsoft.Extensions.Options;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Pokemon;

public sealed class PokemonManager(
    GameData gameData,
    BagManager bag,
    PokemonUsedRepository usedPokemon,
    IOptions<PokemonOptions> options)
{
    public const int NoPaster = 0;
    public const int NoTrainerItem = 0;
    public const int RegistUsedPokemonId = 4;

    public async Task<pbSCGetPokemonListRsp> BuildGetPokemonListRspAsync(LMNRoleInfo role)
    {
        var used = await usedPokemon.LoadAsync(role.GameUlongUID);
        return new pbSCGetPokemonListRsp
        {
            ResultInfo = new pbResultInfo(),
            PokemonList = { role.BuildPokemonList() },
            UsedPokemonList = { used.Select(BuildUsedPokemonInfo) },
            PasterInfo = new pbCOMDT_POKEMON_PASTER_INFO(),
            FreePokemonList = { options.Value.FreePokemonList },
        };
    }

    public pbCOMDT_USED_POKEMON_INFO BuildUsedPokemonInfo(UsedPokemonInfo used) =>
        used.ToUsedPokemonInfo(gameData.PokemonBase.Find(used.PokemonId)?.RecommendEquips ?? []);

    public Task CreateRegistUsedPokemonAsync(ulong gameUlongUID) =>
        usedPokemon.CreateAsync(gameUlongUID, RegistUsedPokemonId);

    public async Task<pbSCUpdateUsedPokemonNtf> AddPokemonPointAsync(LMNRoleInfo role, int pokemonId, int point)
    {
        if (point <= 0 || !role.OwnsPokemon(pokemonId))
        {
            throw new InvalidOperationException($"Pokemon {pokemonId} cannot gain {point} points");
        }

        var used = await usedPokemon.AddPokemonPointAsync(role.GameUlongUID, pokemonId, point);
        return BuildUpdateUsedPokemonNtf(used);
    }

    public pbSCUpdateUsedPokemonNtf BuildUpdateUsedPokemonNtf(UsedPokemonInfo used) => new()
    {
        UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeInfoUpdate,
        UsedPokemonInfo = BuildUsedPokemonInfo(used),
    };

    public IReadOnlyList<int> FreePokemonList => options.Value.FreePokemonList;

    public bool CanBattleWith(LMNRoleInfo role, int pokemonId) =>
        role.CanUsePokemon(pokemonId) || FreePokemonList.Contains(pokemonId);

    public Task<IReadOnlyList<UsedPokemonInfo>> LoadUsedPokemonAsync(LMNRoleInfo role) => usedPokemon.LoadAsync(role.GameUlongUID);

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

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, UsedPokemonInfo Used)> WearPokemonPasterAsync(
        LMNRoleInfo role,
        int pokemonId,
        int pasterID)
    {
        var used = await usedPokemon.FindAsync(role.GameUlongUID, pokemonId);
        if (!role.OwnsPokemon(pokemonId))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrPasterWearPokemonIdinvalid, used);
        }

        if (pasterID != NoPaster)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrPasterNotExists, used);
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
