using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Mail;

public sealed record SystemMail(
    string Subject,
    string Content,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_INFO> Attachments,
    int MailType = (int)PB_ENUM_RES_MAIL_TYPE.PbResMailTypeSys,
    int MailID = 0,
    string FromName = MailBox.SysFromName,
    ulong FromUid = 0,
    int ValidDays = MailBox.MaxMailDays,
    IReadOnlyList<MailContentParam>? ContentParams = null);

public sealed record MailContentParam(string Param, int Type)
{
    public static MailContentParam Text(string text) => new(text, MailBox.TextParamType);

    public static MailContentParam Number(long number) => new(number.ToString(System.Globalization.CultureInfo.InvariantCulture), MailBox.TextParamType);

    public static MailContentParam Item(int resID) => new(resID.ToString(System.Globalization.CultureInfo.InvariantCulture), MailBox.ItemParamType);
}
