using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.StarRoad;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record StarRoadReceipt(PB_ENUM_RES_ERROR_CODE Result, long BeforeCoinNum, RoleWallet? Wallet);

public sealed class StarRoadRepository(DataOptions options)
{
    public async Task<StarRoadState> LoadAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var endSites = await database.GetTable<RoleStarRoadSite>()
            .Where(site => site.GameUlongUID == gameUlongUID)
            .ToDictionaryAsync(site => site.SiteID, site => site.CurPokemonID);
        var road = await database.GetTable<RoleStarRoad>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .FirstOrDefaultAsync();
        return new StarRoadState(endSites, road?.TipsSiteID ?? 0, road?.PokemonTipsCloseTime ?? 0);
    }

    public async Task<bool> TryEndSiteAsync(ulong gameUlongUID, int siteId, int curPokemonId, long now)
    {
        await using var database = new DataConnection(options);
        return await TryInsertEndSiteAsync(database, gameUlongUID, siteId, curPokemonId, now);
    }

    public async Task SetPokemonTipsCloseTimeAsync(ulong gameUlongUID, long pokemonTipsCloseTime)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleStarRoad>().InsertOrUpdateAsync(
            () => new RoleStarRoad { GameUlongUID = gameUlongUID, PokemonTipsCloseTime = pokemonTipsCloseTime },
            _ => new RoleStarRoad { PokemonTipsCloseTime = pokemonTipsCloseTime });
    }

    public async Task SetTipsSiteAsync(ulong gameUlongUID, int tipsSiteId)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleStarRoad>().InsertOrUpdateAsync(
            () => new RoleStarRoad { GameUlongUID = gameUlongUID, TipsSiteID = tipsSiteId },
            _ => new RoleStarRoad { TipsSiteID = tipsSiteId });
    }

    public async Task<StarRoadReceipt> CommitReceivePokemonAsync(
        ulong gameUlongUID,
        int siteId,
        int pokemonId,
        int needScore,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var before = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, CoinID.UNIVERSAL_SHARDS_TOKEN_ID);
        if (!await WalletRepository.TryDebitAsync(database, gameUlongUID, CoinID.UNIVERSAL_SHARDS_TOKEN_ID, needScore))
        {
            await transaction.RollbackAsync();
            return new StarRoadReceipt(PB_ENUM_RES_ERROR_CODE.PbErrNotEnoughCoin, before, null);
        }

        try
        {
            await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, new RolePokemonInfo(
                pokemonId,
                now,
                (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonStarRoadReceivePokemon,
                RolePokemonInfo.HasNewTag)));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return new StarRoadReceipt(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, before, null);
        }

        if (!await TryInsertEndSiteAsync(database, gameUlongUID, siteId, pokemonId, now))
        {
            await transaction.RollbackAsync();
            return new StarRoadReceipt(PB_ENUM_RES_ERROR_CODE.PbErrStarRoadSiteAlreadyEnd, before, null);
        }

        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new StarRoadReceipt(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, before, wallet);
    }

    private static async Task<bool> TryInsertEndSiteAsync(DataConnection database, ulong gameUlongUID, int siteId, int curPokemonId, long now)
    {
        try
        {
            await database.InsertAsync(new RoleStarRoadSite
            {
                GameUlongUID = gameUlongUID,
                SiteID = siteId,
                CurPokemonID = curPokemonId,
                EndTime = now,
            });
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }
}
