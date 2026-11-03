using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed class RoleRepository(DataOptions options) : IRoleRepository
{
    public async Task<RoleInfo?> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var role = await database.GetTable<Role>().FirstOrDefaultAsync(role => role.GameUlongUID == gameUlongUID);
        return role is null ? null : ToRoleInfo(role);
    }

    public async Task SaveAsync(RoleInfo role)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(ToEntity(role));
    }

    private static RoleInfo ToRoleInfo(Role role) => new(
        role.GameUlongUID,
        role.InGameUsrName,
        role.Gender,
        pbCOMDT_ROLE_AVATAR_INFO.Parser.ParseFrom(role.RoleAvatarInfo),
        pbCOMDT_NEWBIE_STATE_INFO.Parser.ParseFrom(role.NewbieState).BitsDetail);

    private static Role ToEntity(RoleInfo role) => new()
    {
        GameUlongUID = role.GameUlongUID,
        InGameUsrName = role.InGameUsrName,
        Gender = role.Gender,
        RoleAvatarInfo = role.RoleAvatarInfo.ToByteArray(),
        NewbieState = role.BuildNewbieState().ToByteArray(),
    };
}
