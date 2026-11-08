using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed class RoleTmpNameRepository(DataOptions options)
{
    public async Task<pbCOMDT_TMP_NAME_INFO[]> LoadAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleTmpName>()
            .Where(tmpName => tmpName.GameUlongUID == gameUlongUID)
            .OrderBy(tmpName => tmpName.TmpNameType)
            .ToArrayAsync();
        return
        [
            .. rows.Select(row => new pbCOMDT_TMP_NAME_INFO
            {
                TmpNameType = row.TmpNameType,
                IsUse = row.IsUse,
                TmpName = row.TmpName,
                IsValid = row.IsValid,
            }),
        ];
    }

    public async Task SaveAsync(ulong gameUlongUID, pbCOMDT_TMP_NAME_INFO tmpName)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(new RoleTmpName
        {
            GameUlongUID = gameUlongUID,
            TmpNameType = tmpName.TmpNameType,
            TmpName = tmpName.TmpName,
            IsUse = tmpName.IsUse,
            IsValid = tmpName.IsValid,
        });
    }
}
