using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("ten_games_team_invite")]
public sealed class TenGamesTeamInvite
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("team_id")]
    public ulong TeamID { get; set; }

    [Column("invite_uid")]
    public ulong InviteUid { get; set; }

    [Column("invited_time")]
    public long InvitedTime { get; set; }
}
