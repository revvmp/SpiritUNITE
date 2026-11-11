using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Profile;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed class ProfileStatisticRepository(DataOptions options)
{
    public async Task<ProfileStatistics> LoadAsync(ulong uid, int seasonID)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleStatistic>().FirstOrDefaultAsync(info => info.GameUlongUID == uid);
        return new ProfileStatistics(
            row is null ? new() : pbCOMDT_STATISTIC_INFO.Parser.ParseFrom(row.StatisticInfo),
            row is null || row.SeasonID != seasonID ? new() : pbCOMDT_STATISTIC_INFO.Parser.ParseFrom(row.SeasonStatisticInfo));
    }

    public static async Task RecordAsync(DataConnection database, ulong uid, UsedPokemonBattle battle, RoleFightHistory history)
    {
        var row = await database.GetTable<RoleStatistic>().FirstOrDefaultAsync(info => info.GameUlongUID == uid)
            ?? new RoleStatistic { GameUlongUID = uid };
        var total = pbCOMDT_STATISTIC_INFO.Parser.ParseFrom(row.StatisticInfo);
        var season = row.SeasonID == battle.SeasonId ? pbCOMDT_STATISTIC_INFO.Parser.ParseFrom(row.SeasonStatisticInfo) : new();
        var record = pbCOMDT_FIGHT_HISTORY_RECORD.Parser.ParseFrom(history.FightHistoryRecord);
        ProfileStatistics.Record(total, battle, record, uid);
        ProfileStatistics.Record(season, battle, record, uid);
        row.StatisticInfo = total.ToByteArray();
        row.SeasonID = battle.SeasonId;
        row.SeasonStatisticInfo = season.ToByteArray();
        await database.InsertOrReplaceAsync(row);
    }
}
