using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_energy")]
public sealed class RoleEnergy
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("energy")]
    public int Energy { get; set; }

    [Column("raffle_ticket")]
    public int RaffleTicket { get; set; }

    [Column("energy_free_capacity")]
    public int EnergyFreeCapacity { get; set; }

    [Column("next_flush_time")]
    public long NextFlushTime { get; set; }

    [Column("today_draw_num")]
    public int TodayDrawNum { get; set; }

    [Column("daily_free_draw_use_num")]
    public int DailyFreeDrawUseNum { get; set; }

    [Column("next_draw_reset_time")]
    public long NextDrawResetTime { get; set; }

    [Column("energy_pay_capacity")]
    public int EnergyPayCapacity { get; set; }

    [Column("energy_accelerate_ratio")]
    public int EnergyAccelerateRatio { get; set; }
}
