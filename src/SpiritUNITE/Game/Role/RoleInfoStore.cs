using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public sealed class RoleInfoStore(RoleRepository repository)
{
    private const int MaximumNameBytes = 32;
    private const int MaximumNameWidth = 12;
    private const int MaximumDigits = 5;
    private const int NarrowCharacterWidth = 1;
    private const int WideCharacterWidth = 2;
    private const int FirstWideCharacter = 0x0800;
    private const int ThaiBlockStart = 0x0E00;
    private const int ThaiBlockEnd = 0x0E7F;

    private readonly ConcurrentDictionary<ulong, LMNRoleInfo> roles = new();

    public async Task<LMNRoleInfo?> FindAsync(ulong uid)
    {
        if (roles.TryGetValue(uid, out var cached))
        {
            return cached;
        }

        var role = await repository.FindAsync(uid);
        if (role is null)
        {
            return role;
        }

        return roles.GetOrAdd(uid, role);
    }

    public void Forget(ulong uid) => roles.TryRemove(uid, out _);

    public LMNRoleInfo? Find(ulong uid) => roles.GetValueOrDefault(uid);

    public LMNRoleInfo Require(ulong uid) =>
        Find(uid) ?? throw new InvalidDataException("Message requires a logged in role");

    public Task SaveAsync(LMNRoleInfo role) => repository.SaveAsync(role);

    public async Task<PB_ENUM_RES_ERROR_CODE> RegisterAsync(ulong uid, string userName)
    {
        if (uid == 0)
        {
            throw new InvalidDataException("Registration requires a login identity");
        }

        var error = ValidateName(userName);
        return error != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc ? error : await repository.RegisterAsync(uid, userName);
    }

    public static PB_ENUM_RES_ERROR_CODE ValidateName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrRegisterNameNull;
        }

        if (userName.Length > MaximumNameBytes || Encoding.UTF8.GetByteCount(userName) > MaximumNameBytes)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrRegisterNameInvalid;
        }

        var width = 0;
        var digits = 0;
        foreach (var character in userName)
        {
            if (!IsNameCharacter(character))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrRegisterNameInvalid;
            }

            if (char.IsAsciiDigit(character))
            {
                digits++;
            }

            if (char.GetUnicodeCategory(character) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark))
            {
                width += character < FirstWideCharacter || (int)character is >= ThaiBlockStart and <= ThaiBlockEnd
                    ? NarrowCharacterWidth
                    : WideCharacterWidth;
            }
        }

        return width > MaximumNameWidth || digits > MaximumDigits || width == 0
            ? PB_ENUM_RES_ERROR_CODE.PbErrRegisterNameInvalid
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static bool IsNameCharacter(char character) => character is
        >= '\u0020' and <= '\u007e' or
        >= '\u00a0' and <= '\u02af' or
        >= '\u0400' and <= '\u052f' or
        >= '\u0900' and <= '\u097f' or
        >= '\u0e00' and <= '\u0e7f' or
        >= '\u1100' and <= '\u11ff' or
        >= '\u1e00' and <= '\u1eff' or
        >= '\u3040' and <= '\u30ff' or
        >= '\u3130' and <= '\u318f' or
        >= '\u31f0' and <= '\u31ff' or
        >= '\u3400' and <= '\u4dbf' or
        >= '\u4e00' and <= '\u9fbf' or
        >= '\uac00' and <= '\ud7af';
}
