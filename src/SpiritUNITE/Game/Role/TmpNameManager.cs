using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public sealed class TmpNameManager(RoleTmpNameRepository repository)
{
    public const int CompetitionTmpNameType = 1;
    public const int InUse = 1;
    public const int Valid = 1;

    private const int NameSuffixDigits = 8;

    public async Task<PB_ENUM_RES_ERROR_CODE> ChgRoleTmpNameAsync(ulong gameUlongUID, pbCSChgRoleTmpNameReq request)
    {
        if (request.TmpNameType != CompetitionTmpNameType || request.IsValid != Valid)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        var nameResult = RoleInfoStore.ValidateName(request.TmpName);
        if (nameResult != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return nameResult;
        }

        await repository.SaveAsync(gameUlongUID, new pbCOMDT_TMP_NAME_INFO
        {
            TmpNameType = CompetitionTmpNameType,
            IsUse = InUse,
            TmpName = request.TmpName,
            IsValid = Valid,
        });
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<pbCOMDT_TMP_NAME_LIST> BuildTmpNameListAsync(ulong gameUlongUID) => new()
    {
        TmpNameInfo = { await repository.LoadAsync(gameUlongUID) },
    };

    public static string GenerateNewNameSuffix() =>
        string.Concat(Enumerable.Range(0, NameSuffixDigits).Select(_ => (char)('0' + Random.Shared.Next(10))));
}
