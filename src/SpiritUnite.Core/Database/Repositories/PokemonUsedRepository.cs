using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.Linq;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed class PokemonUsedRepository(DataOptions options)
{
    private const int NoRow = 0;

    public async Task<IReadOnlyList<UsedPokemonInfo>> LoadAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RolePokemonUsed>()
            .Where(used => used.GameUlongUID == gameUlongUID)
            .OrderBy(used => used.PokemonId)
            .ToArrayAsync();
        return [.. rows.Select(ToUsedPokemonInfo)];
    }

    public async Task<UsedPokemonInfo> FindAsync(ulong gameUlongUID, int pokemonId)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RolePokemonUsed>()
            .FirstOrDefaultAsync(used => used.GameUlongUID == gameUlongUID && used.PokemonId == pokemonId);
        return row is null ? UsedPokemonInfo.Unused(pokemonId) : ToUsedPokemonInfo(row);
    }

    public Task<UsedPokemonInfo> SaveHeldItemIDAsync(ulong gameUlongUID, int pokemonId, IReadOnlyList<int> heldItemID)
    {
        var heldItemDesignInfo = new pbCOMDT_HELDITEM_DESIGN_INFO { HeldItemID = { heldItemID } }.ToByteArray();
        return SaveLobbyChoiceAsync(
            gameUlongUID,
            UsedPokemonInfo.Unused(pokemonId) with { HeldItemID = [.. heldItemID] },
            used => used.Set(row => row.HeldItemDesignInfo, heldItemDesignInfo));
    }

    public Task<UsedPokemonInfo> SaveLastCarryTrainerItemIDAsync(ulong gameUlongUID, int pokemonId, int lastCarryTrainerItemID) =>
        SaveLobbyChoiceAsync(
            gameUlongUID,
            UsedPokemonInfo.Unused(pokemonId) with { LastCarryTrainerItemID = lastCarryTrainerItemID },
            used => used.Set(row => row.LastCarryTrainerItemID, lastCarryTrainerItemID));

    public Task<UsedPokemonInfo> SaveBadgeSchedueAsync(ulong gameUlongUID, int pokemonId, int lastSchedueId, int battleNotUseBadge) =>
        SaveLobbyChoiceAsync(
            gameUlongUID,
            UsedPokemonInfo.Unused(pokemonId) with { LastSchedueId = lastSchedueId, BattleNotUseBadge = battleNotUseBadge },
            used => used.Set(row => row.LastSchedueId, lastSchedueId).Set(row => row.BattleNotUseBadge, battleNotUseBadge));

    public Task<UsedPokemonInfo> SaveWearPasterIDAsync(ulong gameUlongUID, int pokemonId, int wearPasterID) =>
        SaveLobbyChoiceAsync(
            gameUlongUID,
            UsedPokemonInfo.Unused(pokemonId) with { WearPasterID = wearPasterID },
            used => used.Set(row => row.WearPasterID, wearPasterID));

    public Task<UsedPokemonInfo> SaveSkillRecommendInfoAsync(ulong gameUlongUID, int pokemonId, pbCOMDT_SKILLRECOMMENDINFO skillRecommendInfo)
    {
        var savedSkillRecommendInfo = skillRecommendInfo.ToByteArray();
        return SaveLobbyChoiceAsync(
            gameUlongUID,
            UsedPokemonInfo.Unused(pokemonId) with { SkillRecommendInfo = skillRecommendInfo.Clone() },
            used => used.Set(row => row.SkillRecommendInfo, savedSkillRecommendInfo));
    }

    public async Task<bool> CreateAsync(ulong gameUlongUID, int pokemonId)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(NewRow(gameUlongUID, UsedPokemonInfo.Unused(pokemonId)));
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task<UsedPokemonInfo> AddPokemonPointAsync(ulong gameUlongUID, int pokemonId, int point)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var emptyHeldItemDesign = UsedPokemonInfo.Unused(pokemonId).BuildHeldItemDesignInfo().ToByteArray();
        await database.GetTable<RolePokemonUsed>().InsertOrUpdateAsync(
            () => new RolePokemonUsed
            {
                GameUlongUID = gameUlongUID,
                PokemonId = pokemonId,
                HeldItemDesignInfo = emptyHeldItemDesign,
                PokemonTotalPoint = point,
                PokemonSeasonPoint = point,
            },
            saved => new RolePokemonUsed
            {
                PokemonTotalPoint = saved.PokemonTotalPoint + point,
                PokemonSeasonPoint = saved.PokemonSeasonPoint + point,
            });
        var row = await database.GetTable<RolePokemonUsed>()
            .FirstAsync(used => used.GameUlongUID == gameUlongUID && used.PokemonId == pokemonId);
        await transaction.CommitAsync();
        return ToUsedPokemonInfo(row);
    }

    private async Task<UsedPokemonInfo> SaveLobbyChoiceAsync(
        ulong gameUlongUID,
        UsedPokemonInfo firstUse,
        Func<IQueryable<RolePokemonUsed>, IUpdatable<RolePokemonUsed>> setLobbyChoice)
    {
        await using var database = new DataConnection(options);
        if (await UpdateLobbyChoiceAsync() == NoRow)
        {
            try
            {
                await database.InsertAsync(NewRow(gameUlongUID, firstUse));
            }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                await UpdateLobbyChoiceAsync();
            }
        }

        var row = await database.GetTable<RolePokemonUsed>()
            .FirstAsync(used => used.GameUlongUID == gameUlongUID && used.PokemonId == firstUse.PokemonId);
        return ToUsedPokemonInfo(row);

        Task<int> UpdateLobbyChoiceAsync() => setLobbyChoice(database.GetTable<RolePokemonUsed>()
                .Where(used => used.GameUlongUID == gameUlongUID && used.PokemonId == firstUse.PokemonId))
            .UpdateAsync();
    }

    internal static UsedPokemonInfo ToUsedPokemonInfo(RolePokemonUsed row) => new(
        row.PokemonId,
        [.. pbCOMDT_HELDITEM_DESIGN_INFO.Parser.ParseFrom(row.HeldItemDesignInfo).HeldItemID],
        row.LastCarryTrainerItemID,
        row.LastSchedueId,
        row.BattleNotUseBadge,
        row.WearPasterID,
        row.PokemonTotalPoint,
        row.PokemonSeasonPoint,
        row.LastBattleWinTime,
        new UsedPokemonStatistics(
            ParseStatistic(row.StatisticInfo),
            row.CurrentSeasonID,
            ParseStatistic(row.CurrentSeasonStatisticInfo),
            row.CurrentWeekStart,
            ParseStatistic(row.CurrentWeekStatisticInfo),
            row.LastBattleTime,
            pbCOMDT_RANKED_MATCH_USED_POKEMON_INFO.Parser.ParseFrom(row.RankedMatchInfoInSeason)),
        row.SkillRecommendInfo.Length > 0 ? pbCOMDT_SKILLRECOMMENDINFO.Parser.ParseFrom(row.SkillRecommendInfo) : null,
        row.UseCount,
        row.LastUseTime);

    private static pbCOMDT_USED_POKEMON_STATISTIC_INFO ParseStatistic(byte[] statistic) =>
        pbCOMDT_USED_POKEMON_STATISTIC_INFO.Parser.ParseFrom(statistic);

    private static RolePokemonUsed NewRow(ulong gameUlongUID, UsedPokemonInfo used) => new()
    {
        GameUlongUID = gameUlongUID,
        PokemonId = used.PokemonId,
        HeldItemDesignInfo = used.BuildHeldItemDesignInfo().ToByteArray(),
        LastCarryTrainerItemID = used.LastCarryTrainerItemID,
        LastSchedueId = used.LastSchedueId,
        BattleNotUseBadge = used.BattleNotUseBadge,
        WearPasterID = used.WearPasterID,
        PokemonTotalPoint = used.PokemonTotalPoint,
        PokemonSeasonPoint = used.PokemonSeasonPoint,
        LastBattleWinTime = used.LastBattleWinTime,
        StatisticInfo = used.BattleStatistics.Total.ToByteArray(),
        CurrentSeasonID = used.BattleStatistics.CurrentSeasonID,
        CurrentSeasonStatisticInfo = used.BattleStatistics.CurrentSeason.ToByteArray(),
        CurrentWeekStart = used.BattleStatistics.CurrentWeekStart,
        CurrentWeekStatisticInfo = used.BattleStatistics.CurrentWeek.ToByteArray(),
        LastBattleTime = used.BattleStatistics.LastBattleTime,
        RankedMatchInfoInSeason = used.BattleStatistics.RankedMatchInfoInSeason.ToByteArray(),
        SkillRecommendInfo = used.SkillRecommendInfo?.ToByteArray() ?? [],
        UseCount = used.UseCount,
        LastUseTime = used.LastUseTime,
    };
}
