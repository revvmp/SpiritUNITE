using System.Text;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;
using GuildEntity = SpiritUNITE.Database.Models.Guild;

namespace SpiritUNITE.Game.Guild;

public static class GuildRules
{
    public const string ShortIdPrefix = "#";
    public const int MasterOffice = (int)PB_ENUM_ENM_GUILD_OFFICE.PbEnmGuildOfficeMaster;
    public const int NormalOffice = (int)PB_ENUM_ENM_GUILD_OFFICE.PbEnmGuildOfficeNormal;
    public const int NeedAgreeJoinType = (int)PB_ENUM_ENM_GUILD_JOIN_SET_TYPE.PbEnmGuildJoinSetTypeNeedAgree;
    public const int AutoJoinJoinType = (int)PB_ENUM_ENM_GUILD_JOIN_SET_TYPE.PbEnmGuildJoinSetTypeAutoJoin;
    public const int MaxLabelNum = (int)PB_MACRO_MAX_GUILD_LABEL_NUM.PbMaxGuildLabelNum;
    public const int MaxSearchResultNum = (int)PB_MACRO_MAX_GUILD_SEARCH_RESULT_NUM.PbMaxGuildSearchResultNum;
    public const int MaxDailyFreeQuitCnt = (int)PB_MACRO_COM_MAX_DAILY_FREE_QUIT_GUILD_CNT.PbComMaxDailyFreeQuitGuildCnt;
    public const int NoLabel = 0;

    private const int MaxGuildNameBytes = (int)PB_MACRO_MAX_GUILD_NAME_LEN.PbMaxGuildNameLen;
    private const int MaxGuildNoticeBytes = (int)PB_MACRO_MAX_GUILD_NOTICE_LEN.PbMaxGuildNoticeLen;
    private const int MaxGuildCommentBytes = (int)PB_MACRO_MAX_GUILD_COMMENT_LEN.PbMaxGuildCommentLen;
    private const int MaxGuildMemberHardCap = (int)PB_MACRO_MAX_GUILD_MEMBER_NUM.PbMaxGuildMemberNum;
    private const int MaxJoinRequestHardCap = (int)PB_MACRO_MAX_GUILD_JOIN_REQUEST_NUM.PbMaxGuildJoinRequestNum;
    private const int MaxCommentHardCap = (int)PB_MACRO_MAX_GUILD_COMMENT_NUM.PbMaxGuildCommentNum;

    public static int MaxMemberNum(GameData gameData) =>
        Math.Min(gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalMaxGuildMemberNum), MaxGuildMemberHardCap);

    public static int MaxJoinRequestNum(GameData gameData) =>
        Math.Min(gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalMaxGuildJoinRequestNum), MaxJoinRequestHardCap);

    public static int MaxCommentNum(GameData gameData) =>
        Math.Min(gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalMaxGuildCommentNum), MaxCommentHardCap);

    public static TimeSpan JoinGuildCdTime(GameData gameData) =>
        TimeSpan.FromMinutes(gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalJoinGuildCdTime));

    public static bool IsValidName(string guildName) =>
        guildName.Length > 0
        && guildName.Trim().Length == guildName.Length
        && Encoding.UTF8.GetByteCount(guildName) < MaxGuildNameBytes
        && !guildName.Any(char.IsControl);

    public static bool IsValidNotice(string notice) =>
        notice.Trim().Length > 0
        && Encoding.UTF8.GetByteCount(notice) < MaxGuildNoticeBytes
        && !notice.Any(char.IsControl);

    public static bool IsValidCommentContent(string content) =>
        content.Trim().Length > 0
        && Encoding.UTF8.GetByteCount(content) < MaxGuildCommentBytes
        && !content.Any(char.IsControl);

    public static bool IsValidJoinType(int joinType) => joinType is NeedAgreeJoinType or AutoJoinJoinType;

    public static bool AreValidLabels(GameData gameData, IReadOnlyList<int> labels) =>
        labels.Count is > 0 and <= MaxLabelNum
        && labels.Distinct().Count() == labels.Count
        && labels.All(label => gameData.GuildLabel.Contains(label));

    public static string BuildShortId(ulong guildID) => ShortIdPrefix + RoleProfile.BuildShortId(guildID);

    public static bool TryParseShortId(string shortId, out ulong guildID)
    {
        guildID = 0;
        return shortId.StartsWith(ShortIdPrefix, StringComparison.Ordinal)
            && RoleProfile.TryParseShortId(shortId[ShortIdPrefix.Length..], out guildID);
    }

    public static int[] LabelsOf(GuildEntity guild) =>
        [.. new[] { guild.Label1, guild.Label2, guild.Label3 }.Where(label => label != NoLabel)];

    public static void SetLabels(GuildEntity guild, IReadOnlyList<int> labels)
    {
        guild.Label1 = labels.ElementAtOrDefault(0);
        guild.Label2 = labels.ElementAtOrDefault(1);
        guild.Label3 = labels.ElementAtOrDefault(2);
    }
}
