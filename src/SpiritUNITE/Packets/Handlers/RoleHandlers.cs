using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class RoleHandlers(RoleInfoStore roles)
{
    private const int MaxNewbie = 4095;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetRoleCosmeticReq)]
    public GamePacket[] OnSetRoleCosmeticReq(GamePacket request)
    {
        var role = RequireRole(request);
        var cosmetic = pbCSSetRoleCosmeticReq.Parser.ParseFrom(request.PkgData);
        role.SetRoleCosmetic(cosmetic.Gender, cosmetic.RoleAvatarInfo ?? new pbCOMDT_ROLE_AVATAR_INFO());

        var response = new pbSCSetRoleCosmeticRsp
        {
            ResultInfo = new pbResultInfo(),
            RoleAvatarInfo = role.RoleAvatarInfo,
            Gender = role.Gender,
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetRoleCosmeticRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetNewbieStateInfoReq)]
    public GamePacket[] OnSetNewbieStateInfoReq(GamePacket request)
    {
        var role = RequireRole(request);
        var newbie = pbCSSetNewbieStateInfoReq.Parser.ParseFrom(request.PkgData).Newbie;
        if (newbie is < 0 or > MaxNewbie)
        {
            throw new InvalidDataException($"Newbie {newbie} is out of range");
        }

        role.SetNewbieState(newbie);
        var response = new pbSCNewbieStateInfoNtf { NewbieState = role.BuildNewbieState() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, response)];
    }

    private RoleInfo RequireRole(GamePacket request) =>
        roles.Find(request.Uid) ?? throw new InvalidDataException("Message requires a logged in role");
}
