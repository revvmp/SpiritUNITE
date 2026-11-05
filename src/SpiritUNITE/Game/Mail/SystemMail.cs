using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Mail;

public sealed record SystemMail(
    string Subject,
    string Content,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_INFO> Attachments,
    int MailType = (int)PB_ENUM_RES_MAIL_TYPE.PbResMailTypeSys,
    int MailID = 0,
    string FromName = MailBox.SysFromName,
    ulong FromUid = 0,
    int ValidDays = MailBox.MaxMailDays);
