using SpiritUNITE.Game.Accounts;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class AccountHandlers(AccountStore accounts)
{
    private const int MaxNewbieStep = 4095;

    [PacketHandler(Connector.Lobby, PB_ENUM_CS_MSGID_DEF.PbCsidSetRoleCosmeticReq)]
    public GamePacket[] SetAppearance(GamePacket request)
    {
        var account = RequireAccount(request);
        var change = pbCSSetRoleCosmeticReq.Parser.ParseFrom(request.Payload);
        account.SetAppearance(change.Gender, change.RoleAvatarInfo ?? new pbCOMDT_ROLE_AVATAR_INFO());

        var response = new pbSCSetRoleCosmeticRsp
        {
            ResultInfo = new pbResultInfo(),
            RoleAvatarInfo = account.Avatar,
            Gender = account.Gender,
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetRoleCosmeticRsp, response)];
    }

    [PacketHandler(Connector.Lobby, PB_ENUM_CS_MSGID_DEF.PbCsidSetNewbieStateInfoReq)]
    public GamePacket[] SetNewbieStep(GamePacket request)
    {
        var account = RequireAccount(request);
        var step = pbCSSetNewbieStateInfoReq.Parser.ParseFrom(request.Payload).Newbie;
        if (step is < 0 or > MaxNewbieStep)
        {
            throw new InvalidDataException($"Newbie step {step} is out of range");
        }

        account.MarkNewbieStep(step);
        var response = new pbSCNewbieStateInfoNtf { NewbieState = account.BuildNewbieState() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, response)];
    }

    private Account RequireAccount(GamePacket request) =>
        accounts.Find(request.Uid) ?? throw new InvalidDataException("Message requires a logged in account");
}
