using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Role;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Guild;

public sealed record GuildCommentPost(PB_ENUM_RES_ERROR_CODE Result, GamePacket? CommentNtf);

public sealed class GuildComments(
    GuildRepository repository,
    GuildInfoBuilder builder,
    RoleInfoStore roles,
    GameData gameData,
    LobbyRoles lobbyRoles,
    TimeProvider clock,
    ILogger<GuildComments> logger)
{
    public const int ChatCommentType = (int)PB_ENUM_ENM_GUILD_COMMENT_TYPE.PbEnmGuildCommentTypeChat;
    public const int QuickChatCommentType = (int)PB_ENUM_ENM_GUILD_COMMENT_TYPE.PbEnmGuildCommentTypeQuickChat;
    public const int MemberQuitCommentType = (int)PB_ENUM_ENM_GUILD_COMMENT_TYPE.PbEnmGuildCommentTypeEventMemberQuit;
    public const int MasterTransferCommentType = (int)PB_ENUM_ENM_GUILD_COMMENT_TYPE.PbEnmGuildCommentTypeEventMasterTransfer;
    public const int MemberJoinCommentType = (int)PB_ENUM_ENM_GUILD_COMMENT_TYPE.PbEnmGuildCommentTypeEventJoin;

    private const int FullList = 0;
    private const int Appended = 1;
    private const long NoCommentIndex = 0;

    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<GuildCommentPost> LeaveCommentAsync(GuildMember member, LMNRoleInfo role, pbCSGuildLeaveCommentReq request)
    {
        var comment = new GuildComment
        {
            GuildID = member.GuildID,
            CommentType = request.CommentType,
            GameUlongUID = member.GameUlongUID,
            MemberName = role.InGameUsrName,
        };
        switch (request.CommentType)
        {
            case ChatCommentType when request.CommentInfo?.Chat?.Content is { } content && GuildRules.IsValidCommentContent(content):
                comment.Content = content;
                break;
            case QuickChatCommentType when request.CommentInfo?.QuickChat?.TextID is > 0 and var textID:
                comment.TextID = textID;
                break;
            default:
                return new GuildCommentPost(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, null);
        }

        var saved = await AddAsync(comment);
        await repository.SaveCommentReadIndexAsync(member.GameUlongUID, member.GuildID, saved.CommentIndex);
        logger.LogInformation("Uid {Uid} left a type {CommentType} comment {CommentIndex} in guild {GuildID}",
            member.GameUlongUID, saved.CommentType, saved.CommentIndex, saved.GuildID);
        var ntf = await BuildAppendedNtfAsync(saved);
        await PushToMembersAsync(member.GuildID, ntf, member.GameUlongUID);
        return new GuildCommentPost(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, ntf);
    }

    public async Task<GamePacket?> OpenCommentsAsync(GuildMember member, long lastReceivedIndex)
    {
        var comments = await repository.GetCommentsAsync(member.GuildID, NoCommentIndex);
        var lastIndex = comments.Length == 0 ? NoCommentIndex : comments[^1].CommentIndex;
        await repository.SaveCommentReadIndexAsync(member.GameUlongUID, member.GuildID, lastIndex);
        var append = lastReceivedIndex > NoCommentIndex && lastReceivedIndex <= lastIndex;
        var sent = append ? comments.Where(comment => comment.CommentIndex > lastReceivedIndex).ToArray() : comments;
        if (sent.Length == 0)
        {
            return null;
        }

        return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGuildCommentNtf, new pbSCGuildCommentNtf
        {
            IsAppend = append ? Appended : FullList,
            LastReceivedIndex = lastIndex,
            CommentList = { await BuildCommentInfosAsync(sent) },
        });
    }

    public async Task AddEventAsync(ulong guildID, int commentType, LMNRoleInfo subject, LMNRoleInfo? other = null)
    {
        var saved = await AddAsync(new GuildComment
        {
            GuildID = guildID,
            CommentType = commentType,
            GameUlongUID = subject.GameUlongUID,
            MemberName = subject.InGameUsrName,
            OtherUid = other?.GameUlongUID ?? 0,
            OtherName = other?.InGameUsrName ?? "",
        });
        await PushToMembersAsync(guildID, await BuildAppendedNtfAsync(saved), null);
    }

    private async Task<GuildComment> AddAsync(GuildComment comment)
    {
        comment.CommentTime = clock.GetUtcNow().ToUnixTimeSeconds();
        await gate.WaitAsync();
        try
        {
            return await repository.AddCommentAsync(comment, GuildRules.MaxCommentNum(gameData));
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<GamePacket> BuildAppendedNtfAsync(GuildComment comment) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGuildCommentNtf, new pbSCGuildCommentNtf
        {
            IsAppend = Appended,
            LastReceivedIndex = comment.CommentIndex,
            CommentList = { await BuildCommentInfoAsync(comment) },
        });

    private async Task<pbCOMDT_GUILD_COMMENT_INFO[]> BuildCommentInfosAsync(IEnumerable<GuildComment> comments)
    {
        var infos = new List<pbCOMDT_GUILD_COMMENT_INFO>();
        foreach (var comment in comments)
        {
            infos.Add(await BuildCommentInfoAsync(comment));
        }

        return [.. infos];
    }

    private async Task<pbCOMDT_GUILD_COMMENT_INFO> BuildCommentInfoAsync(GuildComment comment)
    {
        var union = new pbCOMDT_GUILD_COMMENT_UNION();
        switch (comment.CommentType)
        {
            case ChatCommentType:
                {
                    var (headInfo, office, curHeadIconInfo, userPlantInfo) = await BuildSpeakerAsync(comment);
                    union.Chat = new pbCOMDT_GUILD_COMMENT_CHAT
                    {
                        MemberName = comment.MemberName,
                        HeadInfo = headInfo,
                        Office = office,
                        Content = comment.Content,
                        Time = comment.CommentTime,
                        Uid = comment.GameUlongUID,
                        CurHeadIconInfo = curHeadIconInfo,
                        UserPlantInfo = userPlantInfo,
                    };
                    break;
                }
            case QuickChatCommentType:
                {
                    var (headInfo, office, curHeadIconInfo, userPlantInfo) = await BuildSpeakerAsync(comment);
                    union.QuickChat = new pbCOMDT_GUILD_COMMENT_QUICK_CHAT
                    {
                        TextID = comment.TextID,
                        MemberName = comment.MemberName,
                        HeadInfo = headInfo,
                        Office = office,
                        Time = comment.CommentTime,
                        Uid = comment.GameUlongUID,
                        CurHeadIconInfo = curHeadIconInfo,
                        UserPlantInfo = userPlantInfo,
                    };
                    break;
                }
            case MemberQuitCommentType:
                union.MemberQuit = new pbCOMDT_GUILD_COMMENT_EVENT_MEMBER_QUIT
                {
                    MemberName = comment.MemberName,
                    Time = comment.CommentTime,
                    Uid = comment.GameUlongUID,
                };
                break;
            case MemberJoinCommentType:
                union.MemberJoin = new pbCOMDT_GUILD_COMMENT_EVENT_MEMBER_JOIN
                {
                    MemberName = comment.MemberName,
                    Time = comment.CommentTime,
                    Uid = comment.GameUlongUID,
                };
                break;
            case MasterTransferCommentType:
                union.MasterTransfer = new pbCOMDT_GUILD_COMMENT_EVENT_MASTER_TRANSFER
                {
                    OldMasterName = comment.MemberName,
                    NewMasterName = comment.OtherName,
                    Time = comment.CommentTime,
                    OldMasterUid = comment.GameUlongUID,
                    NewMasterUid = comment.OtherUid,
                };
                break;
        }

        return new pbCOMDT_GUILD_COMMENT_INFO
        {
            CommentType = comment.CommentType,
            CommentInfo = union,
            Index = (int)comment.CommentIndex,
        };
    }

    private async Task<(pbCOMDT_HEAD_INFO HeadInfo, int Office, pbCOMDT_HEAD_ICON_BASE_INFO CurHeadIconInfo, pbCOMDT_ROLE_HEAD_URL_INFO UserPlantInfo)>
        BuildSpeakerAsync(GuildComment comment)
    {
        var member = await repository.FindMemberAsync(comment.GameUlongUID);
        var office = member?.GuildID == comment.GuildID ? member.Office : GuildRules.NormalOffice;
        if (await roles.FindAsync(comment.GameUlongUID) is not { } role)
        {
            return (GuildInfoBuilder.BuildUnknownHeadInfo(), office, new pbCOMDT_HEAD_ICON_BASE_INFO(), EmptyChildren.Fill(new pbCOMDT_ROLE_HEAD_URL_INFO()));
        }

        return (await builder.BuildHeadInfoAsync(role), office, await builder.BuildCurHeadIconInfoAsync(role), await builder.BuildUserPlantInfoAsync(role));
    }

    private async Task PushToMembersAsync(ulong guildID, GamePacket packet, ulong? skippedUid)
    {
        foreach (var member in await repository.GetMembersAsync(guildID))
        {
            if (member.GameUlongUID != skippedUid)
            {
                await lobbyRoles.PushAsync(member.GameUlongUID, packet);
            }
        }
    }
}
