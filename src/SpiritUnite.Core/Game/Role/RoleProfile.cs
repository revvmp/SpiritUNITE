using SpiritUnite.Databin;

namespace SpiritUnite.Game.Role;

public static class RoleProfile
{
    private const string ShortIdAlphabet = "0123456789ACEFGHKLMNPQRTXY";
    private const int MaxShortIdLength = 7;
    private const ulong ShortIdRadix = 26;
    private const ulong ShortIdSpace = 8_031_810_176;
    private const ulong ShortIdMultiplier = 4_919_457_521;
    private const ulong ShortIdInverseMultiplier = 4_125_944_209;
    private const ulong ShortIdOffset = 2_718_281_828;

    public static int GetLevel(GameData gameData, LMNRoleInfo role) => gameData.TrainerLevel.Rows
        .Where(row => row.UpgradeExp <= role.RoleTotalExp)
        .Max(row => row.Level);

    public static int GetNextLevelTotalExp(GameData gameData, LMNRoleInfo role) => gameData.TrainerLevel.Rows
        .Where(row => row.UpgradeExp > role.RoleTotalExp)
        .Select(row => row.UpgradeExp)
        .DefaultIfEmpty(gameData.TrainerLevel.Rows.Max(row => row.UpgradeExp))
        .Min();

    public static bool HasReachedMaxLevel(GameData gameData, LMNRoleInfo role) =>
        role.RoleTotalExp >= gameData.TrainerLevel.Rows.Max(row => row.UpgradeExp);

    public static string BuildShortId(LMNRoleInfo role) => BuildShortId(role.GameUlongUID);

    public static bool HasShortId(ulong uid) => uid < ShortIdSpace;

    public static string BuildShortId(ulong uid)
    {
        if (uid >= ShortIdSpace)
        {
            throw new InvalidDataException($"Uid {uid} has no short id");
        }

        var value = (ulong)((((UInt128)uid * ShortIdMultiplier) + ShortIdOffset) % ShortIdSpace);
        return string.Create(CountShortIdDigits(value), value, static (characters, remaining) =>
        {
            for (var index = characters.Length - 1; index >= 0; index--)
            {
                characters[index] = ShortIdAlphabet[(int)(remaining % ShortIdRadix)];
                remaining /= ShortIdRadix;
            }
        });
    }

    private static int CountShortIdDigits(ulong value)
    {
        var digits = 1;
        while (value >= ShortIdRadix)
        {
            value /= ShortIdRadix;
            digits++;
        }

        return digits;
    }

    public static bool TryParseShortId(string text, out ulong uid)
    {
        uid = 0;
        if (text.Length is 0 or > MaxShortIdLength || (text.Length > 1 && text[0] == ShortIdAlphabet[0]))
        {
            return false;
        }

        ulong value = 0;
        foreach (var character in text)
        {
            var digit = ShortIdAlphabet.IndexOf(char.ToUpperInvariant(character), StringComparison.Ordinal);
            if (digit < 0)
            {
                return false;
            }

            value = (value * ShortIdRadix) + (ulong)digit;
        }

        uid = (ulong)((((UInt128)value + ShortIdSpace - ShortIdOffset) * ShortIdInverseMultiplier) % ShortIdSpace);
        return true;
    }
}
