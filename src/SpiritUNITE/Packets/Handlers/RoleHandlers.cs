using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class RoleHandlers(RoleInfoStore roles)
{
    private const int MaxNewbie = 4095;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetRoleCosmeticReq)]
    public async Task<GamePacket[]> OnSetRoleCosmeticReq(GamePacket request)
    {
        var role = RequireRole(request);
        var cosmetic = pbCSSetRoleCosmeticReq.Parser.ParseFrom(request.PkgData);
        role.SetRoleCosmetic(cosmetic.Gender, cosmetic.RoleAvatarInfo ?? new pbCOMDT_ROLE_AVATAR_INFO());
        await roles.SaveAsync(role);

        var response = new pbSCSetRoleCosmeticRsp
        {
            ResultInfo = new pbResultInfo(),
            RoleAvatarInfo = role.RoleAvatarInfo,
            Gender = role.Gender,
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetRoleCosmeticRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetNewbieStateInfoReq)]
    public async Task<GamePacket[]> OnSetNewbieStateInfoReq(GamePacket request)
    {
        var role = RequireRole(request);
        var newbie = pbCSSetNewbieStateInfoReq.Parser.ParseFrom(request.PkgData).Newbie;
        if (newbie is < 0 or > MaxNewbie)
        {
            throw new InvalidDataException($"Newbie {newbie} is out of range");
        }

        role.SetNewbieState(newbie);
        await roles.SaveAsync(role);
        var response = new pbSCNewbieStateInfoNtf { NewbieState = role.BuildNewbieState() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSkipNewbieStepReq)]
    public async Task<GamePacket[]> OnSkipNewbieStepReq(GamePacket request)
    {
        var role = RequireRole(request);
        var skipID = pbCSSkipNewbieStepReq.Parser.ParseFrom(request.PkgData).SkipID;
        if (!role.SkipNewbieStep(skipID))
        {
            return [BuildSkipNewbieStepRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid)];
        }

        await roles.SaveAsync(role);
        var state = new pbSCNewbieStateInfoNtf { NewbieState = role.BuildNewbieState() };
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, state),
            BuildSkipNewbieStepRsp(request, 0),
        ];
    }

    private static GamePacket BuildSkipNewbieStepRsp(GamePacket request, PB_ENUM_RES_ERROR_CODE result) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSkipNewbieStepRsp, new pbSCSkipNewbieStepRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        });

    private LMNRoleInfo RequireRole(GamePacket request) =>
        roles.Find(request.Uid) ?? throw new InvalidDataException("Message requires a logged in role");
}
