using System.Text.RegularExpressions;
using SpiritUNITE.Database.Repositories;

namespace SpiritUNITE.Game.Admin;

public sealed record AdminNameLookup(IReadOnlyDictionary<ulong, string> Names)
{
    public string NameOf(ulong uid) => Names.GetValueOrDefault(uid) ?? string.Empty;

    public AdminTrainer Trainer(ulong uid) => new(uid, NameOf(uid));
}

public static partial class AdminNames
{
    public static async Task<AdminNameLookup> FindAsync(RoleRepository roles, IReadOnlyCollection<ulong> uids)
    {
        var distinct = uids.Distinct().ToArray();
        var names = distinct.Length == 0 ? [] : await roles.FindNamesAsync(distinct);
        return new AdminNameLookup(names.ToDictionary(name => name.GameUlongUID, name => name.InGameUsrName));
    }

    public static string FromEnum<TEnum>(TEnum value, string prefix)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        if (!Enum.IsDefined(value) || !name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return name;
        }

        var words = WordBoundary().Replace(name[prefix.Length..], " $1").Trim().ToLowerInvariant();
        return words.Length == 0 ? name : char.ToUpperInvariant(words[0]) + words[1..];
    }

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();
}
