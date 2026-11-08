using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Mail;

public static class MailBox
{
    public const int Unread = (int)PB_ENUM_COM_MAIL_STATE.PbComMailUnread;
    public const int Haveread = (int)PB_ENUM_COM_MAIL_STATE.PbComMailHaveread;
    public const int AccessNone = (int)PB_ENUM_COM_MAILACCESSGET_TYPE.PbComMailaccessgetType0;
    public const int AccessUnget = (int)PB_ENUM_COM_MAILACCESSGET_TYPE.PbComMailaccessgetUnget;
    public const int AccessGeted = (int)PB_ENUM_COM_MAILACCESSGET_TYPE.PbComMailaccessgetGeted;
    public const int MaxMailNum = (int)PB_MACRO_COM_MAX_MAIL_NUM.PbComMaxMailNum;
    public const int MaxMailDays = (int)PB_MACRO_COM_MAX_MAIL_DAYS.PbComMaxMailDays;
    public const int MaxMailAccess = (int)PB_MACRO_COM_MAX_MAIL_ACCESS.PbComMaxMailAccess;
    public const int MaxMailSubject = (int)PB_MACRO_COM_MAX_MAIL_SUBJECT.PbComMaxMailSubject;
    public const int MaxMailContent = (int)PB_MACRO_COM_MAX_MAIL_CONTENT.PbComMaxMailContent;
    public const int MaxFromName = 64;
    public const int MaxMailParamLen = (int)PB_MACRO_COM_MAX_MAILPARAM_LEN.PbComMaxMailparamLen;
    public const int MaxMailContentParamNum = (int)PB_MACRO_COM_MAX_MAIL_CONTENT_PARAM_NUM.PbComMaxMailContentParamNum;
    public const int TextParamType = 1;
    public const int ItemParamType = 3;
    public const int MailTypeCount = (int)PB_ENUM_RES_MAIL_TYPE.PbResMailTypeMax;
    public const int Sys = (int)PB_ENUM_RES_MAIL_TYPE.PbResMailTypeSys;
    public const string SysFromName = "System";
    public const int UnreadAllMailRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdUnreadAllMail;

    public static int? FindUnreadRedDotID(int mailType) => (PB_ENUM_RES_MAIL_TYPE)mailType switch
    {
        PB_ENUM_RES_MAIL_TYPE.PbResMailTypeSys => (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdUnreadSysMail,
        PB_ENUM_RES_MAIL_TYPE.PbResMailTypePerson => (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdUnreadPersonalMail,
        PB_ENUM_RES_MAIL_TYPE.PbResMailTypeAccusation => (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdUnreadAccusationMail,
        _ => null,
    };

    public static bool IsMailType(int mailType) =>
        mailType > (int)PB_ENUM_RES_MAIL_TYPE.PbResMailTypeInvalid && mailType < (int)PB_ENUM_RES_MAIL_TYPE.PbResMailTypeMax;
}
