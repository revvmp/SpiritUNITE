using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace SpiritUNITE.Auth;

public sealed record AccountPassword(byte[] Hash, byte[] Salt)
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int MemoryKibibytes = 19 * 1024;
    private const int Passes = 2;
    private const int Lanes = 1;

    public static AccountPassword Create(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return new AccountPassword(Derive(password, salt), salt);
    }

    public bool Matches(string password) =>
        password.Length > 0 && CryptographicOperations.FixedTimeEquals(Derive(password, Salt), Hash);

    private static byte[] Derive(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = MemoryKibibytes,
            Iterations = Passes,
            DegreeOfParallelism = Lanes,
        };
        return argon2.GetBytes(HashBytes);
    }
}
