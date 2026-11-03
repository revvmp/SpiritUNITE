using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public sealed record RoleItemInfo(RoleItemType ItemType, int ItemId, long GainTime, long TimeLimited, int NewTag)
{
    public const int NoNewTag = 0;
    public const int ShowNewTag = 1;
    public const long Permanent = 0;
    public const long UnknownGainTime = 0;

    private const int AwardedCount = 1;
    private const int NotTimeLimited = 0;
    private const int TimeLimitedFlag = 1;

    public bool IsExpired(long now) => TimeLimited != Permanent && now > TimeLimited;

    public pbCOMDT_AVATAR_INFO ToAvatarInfo() => new()
    {
        AvatarID = ItemId,
        GainTime = GainTime,
        NewTag = NewTag,
        TimeLimited = TimeLimited,
    };

    public pbCOMDT_ONE_RESOURCE_AWARD_INFO ToResourceAwardInfo() => new()
    {
        ResID = ItemId,
        ResCnt = AwardedCount,
        ResType = (int)(ItemType switch
        {
            RoleItemType.AvatarItem => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar,
            RoleItemType.LobbyBg => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesLobbyBackground,
            _ => throw new InvalidOperationException($"Item type {ItemType} has no resource type"),
        }),
    };

    public pbCOMDT_ONE_LOBBY_BACKGROUND_DATA ToLobbyBackgroundData() => new()
    {
        LobbyBgId = ItemId,
        GetTime = GainTime,
        NewTag = NewTag,
        IsTimeLimit = TimeLimited == Permanent ? NotTimeLimited : TimeLimitedFlag,
    };
}
