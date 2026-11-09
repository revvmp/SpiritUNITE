using System.Globalization;
using SpiritUnite.Database.Models;
using SpiritUnite.Databin;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Friend;

public static class FriendIntimacy
{
    private const int NoIntimacyLevel = 0;

    public static pbCOMDT_FRIEND_INTIMACY BuildIntimacyInfo(GameData gameData, int intimacyValue)
    {
        var levels = gameData.IntimacyLevel.Rows.OrderBy(row => row.Level).ToArray();
        var maxLevel = levels[^1].Level;
        var intimacyLevel = NoIntimacyLevel;
        var reachedTotalExp = 0;
        foreach (var level in levels.Where(level => level.Level < maxLevel && intimacyValue >= level.UpgradeTotalExp))
        {
            intimacyLevel = level.Level + 1;
            reachedTotalExp = level.UpgradeTotalExp;
        }

        return new pbCOMDT_FRIEND_INTIMACY
        {
            IntimacyLevel = intimacyLevel,
            IntimacyExp = intimacyValue - reachedTotalExp,
        };
    }

    public static pbCOMDT_BATTLE_INTIMACY_INFO BuildBattleIntimacyInfo(GameData gameData, IEnumerable<RoleFriend> friendsInBattle) =>
        new()
        {
            IntimacyList =
            {
                friendsInBattle
                    .OrderBy(friend => friend.FriendUid)
                    .Select(friend => (friend.FriendUid, Intimacy: BuildIntimacyInfo(gameData, friend.IntimacyValue)))
                    .Where(entry => entry.Intimacy.IntimacyLevel > NoIntimacyLevel)
                    .Select(entry => new pbCOMDT_ONE_INTIMACY_INFO
                    {
                        FriendUid = entry.FriendUid,
                        IntimacyLevel = entry.Intimacy.IntimacyLevel,
                        IntimacyExp = entry.Intimacy.IntimacyExp,
                    }),
            },
        };

    public static int? FindRuleValue(GameData gameData, PB_ENUM_RES_INTIMACY_RULE_ID ruleId) =>
        gameData.IntimacyRule.Find((long)ruleId) is { } rule
        && int.TryParse(rule.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
        && value > 0
            ? value
            : null;

    public static pbCOMDT_FRIEND_INTERACTION BuildFriendInteraction(GameData gameData, RoleFriend friend) =>
        EmptyChildren.Fill(new pbCOMDT_FRIEND_INTERACTION
        {
            FriendUid = friend.FriendUid,
            BanChatFlag = friend.BanChatFlag,
            IntimacyInfo = BuildIntimacyInfo(gameData, friend.IntimacyValue),
            TogetherBattleCnt = friend.TogetherBattleCnt,
        });

    public static GamePacket BuildFriendInteractionNtf(GameData gameData, IEnumerable<RoleFriend> friends) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidFriendInteractionNtf, new pbSCFriendInteractionNtf
        {
            InterActionList = { friends.Select(friend => BuildFriendInteraction(gameData, friend)) },
        });
}
