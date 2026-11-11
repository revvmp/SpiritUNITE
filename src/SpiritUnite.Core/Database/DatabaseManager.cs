using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Bag;

namespace SpiritUnite.Database;

public static class DatabaseManager
{
    public static async Task<DataOptions> ConnectAsync(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Database:ConnectionString is missing; copy appsettings.example.json to appsettings.json");
        }

        var options = new DataOptions().UseMySql(connectionString, MySqlVersion.MariaDB10, MySqlProvider.MySqlConnector);
        await EnsureReadyAsync(options);
        return options;
    }

    private static async Task EnsureReadyAsync(DataOptions options)
    {
        try
        {
            await using var database = new DataConnection(options);
            await database.GetTable<Role>().FirstOrDefaultAsync();
            await database.GetTable<RolePokemon>().AnyAsync();
            await database.GetTable<RolePokemonTimeLimited>().AnyAsync();
            await database.GetTable<RolePasterTimeLimited>().AnyAsync();
            await database.GetTable<RolePasterSpray>().AnyAsync();
            await database.GetTable<RoleSkillEffectsExpandPack>().AnyAsync();
            await database.GetTable<RoleEeveeChallenge>().AnyAsync();
            await database.GetTable<RoleItem>().FirstOrDefaultAsync();
            GridUniqueID.ContinueAfter(await database.GetTable<RoleItem>().MaxAsync(item => item.GridUniqueID) ?? GridUniqueID.None);
            await database.GetTable<RoleCoin>().FirstOrDefaultAsync();
            await database.GetTable<RoleShopBuy>().FirstOrDefaultAsync();
            await database.GetTable<Account>().FirstOrDefaultAsync();
            await database.GetTable<AccountToken>().AnyAsync();
            await database.GetTable<RoleLoginDay>().AnyAsync();
            await database.GetTable<RoleActivityTaskReward>().AnyAsync();
            await database.GetTable<RoleSocial>().AnyAsync();
            await database.GetTable<RoleFriend>().AnyAsync();
            await database.GetTable<RoleFriendRequest>().AnyAsync();
            await database.GetTable<RoleFriendBlack>().AnyAsync();
            await database.GetTable<RoleChatOffline>().AnyAsync();
            await database.GetTable<RoleMail>().FirstOrDefaultAsync();
            await database.GetTable<RoleProfileStyle>().AnyAsync();
            await database.GetTable<RoleStatistic>().AnyAsync();
            await database.GetTable<RoleCardFaceWear>().AnyAsync();
            await database.GetTable<RoleSetting>().FirstOrDefaultAsync();
            await database.GetTable<RoleBattlePass>().AnyAsync();
            await database.GetTable<RoleBattlePassLevelReward>().AnyAsync();
            await database.GetTable<RoleBattlePassTaskReward>().AnyAsync();
            await database.GetTable<RolePokemonUsed>().FirstOrDefaultAsync();
            await database.GetTable<RolePveChallengeNode>().AnyAsync();
            await database.GetTable<RolePveSettlement>().AnyAsync();
            await database.GetTable<BattleSettlementRecord>().AnyAsync();
            await database.GetTable<SettlementEffectRecord>().AnyAsync();
            await database.GetTable<SettlementOwnerRecord>().AnyAsync();
            await database.GetTable<TenGamesSettlement>().AnyAsync();
            await database.GetTable<RoleCreditRecoveryDay>().AnyAsync();
            await database.GetTable<RoleActivityExchange>().AnyAsync();
            await database.GetTable<RoleChallengeTrainerRecord>().FirstOrDefaultAsync();
            await database.GetTable<RoleNewbieChallenge>().AnyAsync();
            await database.GetTable<RoleNewbieChallengeTask>().AnyAsync();
            await database.GetTable<RoleTaskProcess>().AnyAsync();
            await database.GetTable<RoleFightHistory>().AnyAsync();
            await database.GetTable<RoleEnergy>().AnyAsync();
            await database.GetTable<RoleRanked>().FirstOrDefaultAsync();
            await database.GetTable<RoleRankedSeason>().FirstOrDefaultAsync();
            await database.GetTable<RoleRankedSeasonTaskAward>().FirstOrDefaultAsync();
            await database.GetTable<RoleWarmBattle>().AnyAsync();
            await database.GetTable<RoleBeFabulous>().AnyAsync();
            await database.GetTable<RoleMicTips>().AnyAsync();
            await database.GetTable<RoleBadgeShow>().AnyAsync();
            await database.GetTable<RoleTmpName>().AnyAsync();
            await database.GetTable<RolePokemonCarryEquipment>().AnyAsync();
            await database.GetTable<RoleActivationCode>().AnyAsync();
            await database.GetTable<ActivationCodeUse>().AnyAsync();
            await database.GetTable<Guild>().AnyAsync();
            await database.GetTable<GuildMember>().AnyAsync();
            await database.GetTable<GuildJoinRequest>().AnyAsync();
            await database.GetTable<GuildComment>().AnyAsync();
            await database.GetTable<GuildActivity>().AnyAsync();
            await database.GetTable<RoleGuild>().AnyAsync();
            await database.GetTable<RoleGuildActivityReward>().AnyAsync();
            await database.GetTable<RoleGuildActivityTask>().AnyAsync();
            await database.GetTable<AccountBan>().AnyAsync();
            await database.GetTable<AdminToken>().AnyAsync();
            await database.GetTable<AdminAudit>().AnyAsync();
            await database.GetTable<MsdkNotice>().AnyAsync();
            await database.GetTable<HostActivationCodeRow>().AnyAsync();
            await database.GetTable<GiftCodeRow>().AnyAsync();
            await database.GetTable<GiftCodeAward>().AnyAsync();
            await database.GetTable<GiftCodeUse>().AnyAsync();
            await database.GetTable<ServerList>().AnyAsync();
        }
        catch (MySqlException exception)
        {
            throw new InvalidOperationException(
                $"Database is not ready: {exception.Message} Check appsettings.json and run sql/spiritunite.sql from the repository root.",
                exception);
        }
    }
}
