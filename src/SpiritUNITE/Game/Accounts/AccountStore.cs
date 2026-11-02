using System.Collections.Concurrent;

namespace SpiritUNITE.Game.Accounts;

public sealed class AccountStore
{
    private const ulong DisplayNameRange = 100000;

    private readonly ConcurrentDictionary<ulong, Account> accounts = new();

    public Account GetOrCreate(ulong uid) =>
        accounts.GetOrAdd(uid, id => new Account(id, $"Trainer{id % DisplayNameRange}"));

    public Account? Find(ulong uid) => accounts.GetValueOrDefault(uid);
}
