using System.Security.Cryptography;
using System.Text;

namespace SpiritUNITE.Auth;

public sealed record AccountPassword(byte[] Hash, byte[] Salt, int Iterations)
{
    public const int CurrentIterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    public static AccountPassword Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return new AccountPassword(Derive(password, salt, CurrentIterations), salt, CurrentIterations);
    }

    public bool IsOutdated => Iterations < CurrentIterations;

    public bool Matches(string password) =>
        CryptographicOperations.FixedTimeEquals(Derive(password, Salt, Iterations), Hash);

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithm, HashBytes);
}
