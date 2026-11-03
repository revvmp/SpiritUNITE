using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed class RoleRepository(DataOptions options) : IRoleRepository
{
    public async Task<LMNRoleInfo?> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var role = await database.GetTable<Role>().FirstOrDefaultAsync(role => role.GameUlongUID == gameUlongUID);
        return role is null ? null : ToLMNRoleInfo(role);
    }

    public async Task SaveAsync(LMNRoleInfo role)
    {
        await using var database = new DataConnection(options);
        if (await database.UpdateAsync(ToEntity(role)) != 1)
        {
            throw new InvalidOperationException("Cannot save a role that does not exist");
        }
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> RegisterAsync(ulong gameUlongUID, string userName)
    {
        await using var database = new DataConnection(options);
        if (await database.GetTable<Role>().AnyAsync(role => role.GameUlongUID == gameUlongUID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrRegisterNotNeed;
        }

        try
        {
            await database.InsertAsync(new Role
            {
                GameUlongUID = gameUlongUID,
                InGameUsrName = userName,
            });
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            return await database.GetTable<Role>().AnyAsync(role => role.GameUlongUID == gameUlongUID)
                ? PB_ENUM_RES_ERROR_CODE.PbErrRegisterNotNeed
                : PB_ENUM_RES_ERROR_CODE.PbErrRegisterNameDup;
        }

        return 0;
    }

    private static LMNRoleInfo ToLMNRoleInfo(Role role) => new(
        role.GameUlongUID,
        role.InGameUsrName,
        role.Gender,
        pbCOMDT_ROLE_AVATAR_INFO.Parser.ParseFrom(role.RoleAvatarInfo),
        pbCOMDT_NEWBIE_STATE_INFO.Parser.ParseFrom(role.NewbieState).BitsDetail);

    private static Role ToEntity(LMNRoleInfo role) => new()
    {
        GameUlongUID = role.GameUlongUID,
        InGameUsrName = role.InGameUsrName,
        Gender = role.Gender,
        RoleAvatarInfo = role.RoleAvatarInfo.ToByteArray(),
        NewbieState = role.BuildNewbieState().ToByteArray(),
    };
}
