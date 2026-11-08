using System.Globalization;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Admin;

public sealed record AdminReportRow(
    ulong AccusationId,
    long AccusationTime,
    AdminTrainer Reporter,
    AdminTrainer Accused,
    string Scene,
    string Category,
    IReadOnlyList<string> Reasons,
    string Desc,
    string Content,
    bool CreditPunished);

public sealed record AdminReportPage(IReadOnlyList<AdminReportRow> Reports, ulong? Next);

public sealed class AdminReports(AccusationRepository accusations, RoleRepository roles)
{
    public const int PageSize = 50;

    private const string ScenePrefix = "PbEnmAccusation";
    private const string CategoryPrefix = "PbEnmAccusation";
    private const string GameReasonPrefix = "PbEnmGameAccusation";
    private const string ChatReasonPrefix = "PbEnmChatAccusation";
    private const string GuildReasonPrefix = "PbEnmGuildAccusation";
    private const int NoCreditPunish = 0;

    public async Task<AdminReportPage> ListAsync(ulong? before)
    {
        var rows = await accusations.ListAsync(before ?? 0, PageSize);
        var names = await AdminNames.FindAsync(roles, [.. rows.SelectMany(row => new[] { row.GameUlongUID, row.AccusedUid })]);
        return new AdminReportPage(
            [
                .. rows.Select(row => new AdminReportRow(
                    row.AccusationId,
                    row.AccusationTime,
                    names.Trainer(row.GameUlongUID),
                    new AdminTrainer(row.AccusedUid, names.NameOf(row.AccusedUid) is { Length: > 0 } name ? name : row.AccusedRoleName),
                    AdminNames.FromEnum((PB_ENUM_ENM_ACCUSATION_SCENE)row.Scene, ScenePrefix),
                    AdminNames.FromEnum((PB_ENUM_ENM_ACCUSATION_CATEGORY)row.Category, CategoryPrefix),
                    [.. ReasonsOf(row.Reason)],
                    row.Desc,
                    row.Content,
                    row.CreditPunish != NoCreditPunish)),
            ],
            rows.Length == PageSize ? rows[^1].AccusationId : null);
    }

    private static IEnumerable<string> ReasonsOf(string reasons) =>
        reasons.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(reason => int.TryParse(reason, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? ReasonName(id) : reason);

    private static string ReasonName(int reasonId) =>
        Enum.IsDefined((PB_ENUM_ENM_GAME_ACCUSATION_REASON)reasonId) ? AdminNames.FromEnum((PB_ENUM_ENM_GAME_ACCUSATION_REASON)reasonId, GameReasonPrefix)
        : Enum.IsDefined((PB_ENUM_ENM_CHAT_ACCUSATION_REASON)reasonId) ? AdminNames.FromEnum((PB_ENUM_ENM_CHAT_ACCUSATION_REASON)reasonId, ChatReasonPrefix)
        : Enum.IsDefined((PB_ENUM_ENM_GUILD_ACCUSATION_REASON)reasonId) ? AdminNames.FromEnum((PB_ENUM_ENM_GUILD_ACCUSATION_REASON)reasonId, GuildReasonPrefix)
        : reasonId.ToString(CultureInfo.InvariantCulture);
}
