using System.Threading.RateLimiting;

namespace SpiritUNITE.Auth;

public enum PasswordCheck
{
    Matched,
    Mismatched,
    Busy,
}

public sealed class PasswordHashing(int concurrentHashes, int queuedHashes) : IDisposable
{
    private readonly ConcurrencyLimiter slots = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = concurrentHashes,
        QueueLimit = queuedHashes,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
    });

    public async Task<AccountPassword?> TryCreateAsync(string password, CancellationToken cancellationToken)
    {
        using var slot = await slots.AcquireAsync(1, cancellationToken);
        return slot.IsAcquired ? AccountPassword.Create(password) : null;
    }

    public async Task<PasswordCheck> CheckAsync(AccountPassword stored, string password, CancellationToken cancellationToken)
    {
        using var slot = await slots.AcquireAsync(1, cancellationToken);
        if (!slot.IsAcquired)
        {
            return PasswordCheck.Busy;
        }

        return stored.Matches(password) ? PasswordCheck.Matched : PasswordCheck.Mismatched;
    }

    public void Dispose() => slots.Dispose();
}
