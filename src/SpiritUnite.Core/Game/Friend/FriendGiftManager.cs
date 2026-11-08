using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Friend;

public sealed record FriendGiftOutcome(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<GamePacket> SenderPushes);

public sealed class FriendGiftManager(
    FriendGiftRepository repository,
    MailManager mail,
    LobbyRoles lobbyRoles,
    GameData gameData,
    TimeProvider clock,
    ILogger<FriendGiftManager> logger)
{
    private const long SecondsPerDay = 86400;
    private const long NeverGifted = 0;
    private const int FriendDailyGiftCoinMailID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidTypeFriendDailyGiftCoin;
    private const string FriendDailyGiftCoinMailSubject = "System_Friends_Email_Subject_9223_001";
    private const string FriendDailyGiftCoinMailContent = "System_Friends_Email_Content_9223_001";

    public async Task<pbCOMDT_DAILY_GIFT_COIN> GetDailyGiftCoinInfoAsync(ulong uid)
    {
        var gifts = await repository.GetDailyGiftCoinsAsync(uid, GiftDayOf(clock.GetUtcNow().ToUnixTimeSeconds()));
        return new pbCOMDT_DAILY_GIFT_COIN
        {
            LastGiftTime = gifts.Length > 0 ? gifts.Max(gift => gift.GiftTime) : NeverGifted,
            DailyGiftCoinUid = { gifts.Select(gift => gift.DailyGiftCoinUid) },
        };
    }

    public async Task<FriendGiftOutcome> DailyGiftCoinAsync(ulong uid, ulong friendUid)
    {
        if (FindGiftCoinAward() is not { } award || FindRules() is not { } rules)
        {
            logger.LogWarning("Uid {Uid} cannot give a present to uid {FriendUid}: pbIntimacySetting is incomplete", uid, friendUid);
            return new FriendGiftOutcome(PB_ENUM_RES_ERROR_CODE.PbErrFriendDailyGiftCoinErrorConfig, []);
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var present = new SystemMail(
            Subject: FriendDailyGiftCoinMailSubject,
            Content: FriendDailyGiftCoinMailContent,
            Attachments: [award],
            MailID: FriendDailyGiftCoinMailID);
        var commit = uid == friendUid
            ? new FriendGiftCommit(PB_ENUM_RES_ERROR_CODE.PbErrFriendNotFriend)
            : await repository.DailyGiftCoinAsync(uid, friendUid, GiftDayOf(now), now, rules, mail.CreateSystemMail(friendUid, present));
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || commit.Mail is not { } saved || commit.SenderFriend is not { } senderFriend)
        {
            logger.LogInformation("Uid {Uid} present to uid {FriendUid} refused: {Result}", uid, friendUid, commit.Result);
            return new FriendGiftOutcome(commit.Result, []);
        }

        mail.LogSystemMailSent(saved, present);
        logger.LogInformation(
            "Uid {Uid} gave uid {FriendUid} a present of {ResCnt} x {ResID} by mail {MailIndex}; friendship +{IntimacyValue} to {SenderIntimacy} and {ReceiverIntimacy}",
            uid,
            friendUid,
            award.ResCnt,
            award.ResID,
            saved.MailIndex,
            rules.GiftCoinIntimacyValue,
            senderFriend.IntimacyValue,
            commit.ReceiverFriend?.IntimacyValue);
        await PushToReceiverAsync(friendUid, commit, rules);
        return new FriendGiftOutcome(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            rules.GiftCoinIntimacyValue > 0 ? [FriendIntimacy.BuildFriendInteractionNtf(gameData, [senderFriend])] : []);
    }

    private async Task PushToReceiverAsync(ulong friendUid, FriendGiftCommit commit, FriendGiftRules rules)
    {
        if (!lobbyRoles.IsOnline(friendUid) || commit.Mail is not { } saved)
        {
            return;
        }

        await mail.PushMailNewAsync(saved);
        await lobbyRoles.PushAsync(friendUid, await mail.BuildUnreadRedDotPacketsAsync(friendUid, saved.MailType));
        if (rules.GiftCoinIntimacyValue > 0 && commit.ReceiverFriend is { } receiverFriend)
        {
            await lobbyRoles.PushAsync(friendUid, FriendIntimacy.BuildFriendInteractionNtf(gameData, [receiverFriend]));
        }
    }

    private pbCOMDT_ONE_RESOURCE_AWARD_INFO? FindGiftCoinAward()
    {
        if (FindSetting(PB_ENUM_RES_INTIMACY_SETTING_ID.PbResIntimacySettingResId) is not { } resID
            || FindSetting(PB_ENUM_RES_INTIMACY_SETTING_ID.PbResIntimacySettingResNum) is not { } resNum
            || !CoinID.IsGrantableCoin(resID))
        {
            return null;
        }

        var award = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
        {
            ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin,
            ResID = resID,
            ResCnt = resNum,
        };
        return mail.CanAttach(award) ? award : null;
    }

    private FriendGiftRules? FindRules() =>
        FindSetting(PB_ENUM_RES_INTIMACY_SETTING_ID.PbResIntimacySettingResGiftCountMax) is { } giftCountMax
        && FindSetting(PB_ENUM_RES_INTIMACY_SETTING_ID.PbResIntimacySettingResGiftRecvCountMax) is { } giftRecvCountMax
            ? new FriendGiftRules(
                giftCountMax,
                giftRecvCountMax,
                FriendIntimacy.FindRuleValue(gameData, PB_ENUM_RES_INTIMACY_RULE_ID.PbResIntimacyRuleGiftCoinId) ?? 0)
            : null;

    private int? FindSetting(PB_ENUM_RES_INTIMACY_SETTING_ID settingId) =>
        gameData.IntimacySetting.Find((long)settingId) is { Value: > 0 } setting ? setting.Value : null;

    private static long GiftDayOf(long unixSeconds) => unixSeconds / SecondsPerDay;
}
