namespace SpiritUNITE.Auth;

public interface IAccountTokenRepository
{
    Task AddAsync(byte[] tokenHash, ulong accountId, long expiresAt);

    Task<bool> ExtendAsync(byte[] tokenHash, ulong accountId, long now, long expiresAt);

    Task RemoveExpiredAsync(ulong accountId, long now);

    Task<ulong?> FindAccountIdAsync(byte[] tokenHash, long now);
}
