using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed record FirstChargeQuery(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_FIRST_CHARGE_INFO? FirstChargeInfo);

public sealed record FirstChargeAwardOutcome(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<GamePacket> BeforeRsp)
{
    public static FirstChargeAwardOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed class FirstChargeManager(
    GameData gameData,
    ActivitySchedule schedule,
    FirstChargeRepository repository,
    BagManager bag,
    TimeProvider clock)
{
    public const int AwardStatusCanReceive = 0;
    public const int AwardStatusReceived = 1;

    private const int NotCharged = 0;
    private const int OpenAward = 1;
    private const int NotOpenAward = 0;
    private const int FirstLoginSeq = 1;

    public bool IsOpen(DateTimeOffset now) => schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeFirstCharge, now) is not null;

    public async Task<FirstChargeQuery> QueryAsync(LMNRoleInfo role)
    {
        var now = clock.GetUtcNow();
        if (!IsOpen(now))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, null);
        }

        var state = await repository.LoadAsync(role.GameUlongUID);
        var info = new pbCOMDT_FIRST_CHARGE_INFO
        {
            LastFaceShotTime = state.LastFaceShotTime,
            FaceShotCnt = state.FaceShotCnt,
            IsOpenAward = state.FirstChargeTime == NotCharged ? NotOpenAward : OpenAward,
            LastAwardedTime = state.AwardTimes.Count == 0 ? 0 : state.AwardTimes.Values.Max(),
            IsBanFaceShot = state.IsBanFaceShot,
        };
        info.AwardInfo.AddRange(UnlockedAwards(state, now).Select(award => new pbCOMDT_FIRST_CHARGE_AWARD_INFO
        {
            LoginSeq = award.LoginSeq,
            AwardStatus = state.AwardTimes.ContainsKey(award.LoginSeq) ? AwardStatusReceived : AwardStatusCanReceive,
        }));
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, info);
    }

    public async Task<FirstChargeAwardOutcome> AwardAsync(LMNRoleInfo role, int loginSeq)
    {
        var now = clock.GetUtcNow();
        if (!IsOpen(now))
        {
            return FirstChargeAwardOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit);
        }

        var state = await repository.LoadAsync(role.GameUlongUID);
        if (state.FirstChargeTime == NotCharged)
        {
            return FirstChargeAwardOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFirstPayNotFinish);
        }

        if (UnlockedAwards(state, now).FirstOrDefault(award => award.LoginSeq == loginSeq) is not { } award)
        {
            return FirstChargeAwardOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        if (state.AwardTimes.ContainsKey(loginSeq))
        {
            return FirstChargeAwardOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFirstPayHadRewarded);
        }

        var drops = schedule.FindDrops(award.DropID).ToArray();
        var coinGrants = new Dictionary<int, long>();
        var grants = new List<BagGrant>();
        foreach (var drop in drops)
        {
            if (CoinID.IsGrantableCoin(drop.ResID))
            {
                coinGrants[drop.ResID] = coinGrants.GetValueOrDefault(drop.ResID) + drop.ResNum;
            }
            else if (bag.CreateOutSideItemGrant(drop.ResID, drop.ResNum) is { } grant)
            {
                grants.Add(grant);
            }
            else
            {
                return FirstChargeAwardOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
            }
        }

        var commit = await repository.CommitAwardAsync(
            role.GameUlongUID,
            loginSeq,
            new BagChange { Grants = grants, CoinGrants = coinGrants },
            now.ToUnixTimeSeconds());
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || commit.Wallet is not { } wallet)
        {
            return FirstChargeAwardOutcome.Refused(commit.Result);
        }

        var packets = new List<GamePacket>();
        if (commit.ChangedItems.Count > 0)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(commit.ChangedItems)));
        }

        if (coinGrants.Keys.Any(CoinID.WalletCoins.Contains))
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() }));
        }

        if (coinGrants.ContainsKey(CoinID.Coupons))
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp()));
        }

        packets.AddRange(coinGrants.Keys.Where(CoinID.IsSpecialCoin).Select(coinID => GamePacket.Create(
            PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
            SpecialCoin.BuildSpecialCoinChangeNtf(coinID, commit.BeforeCoinNums.GetValueOrDefault(coinID), wallet.GetCoinVal(coinID)))));
        packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonFirstChargeReward,
            ResourcesList =
            {
                drops.Select(drop => BagManager.BuildAward(drop.ResID, drop.ResNum, CoinID.IsGrantableCoin(drop.ResID)
                    ? CoinID.GetCoinResourceType(drop.ResID)
                    : PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem)),
            },
        }));
        return new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, packets);
    }

    public Task BanFaceShotAsync(LMNRoleInfo role) => repository.BanFaceShotAsync(role.GameUlongUID);

    public Task RecordFaceShotShowedAsync(LMNRoleInfo role) =>
        repository.RecordFaceShotShowedAsync(role.GameUlongUID, clock.GetUtcNow().ToUnixTimeSeconds());

    private IEnumerable<pbFirstChargeAward> UnlockedAwards(FirstChargeState state, DateTimeOffset now)
    {
        if (state.FirstChargeTime == NotCharged)
        {
            return [];
        }

        var chargeDay = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(state.FirstChargeTime).UtcDateTime);
        var loginSeqReached = DateOnly.FromDateTime(now.UtcDateTime).DayNumber - chargeDay.DayNumber + FirstLoginSeq;
        return gameData.FirstChargeAward.Rows
            .Where(award => award.LoginSeq <= loginSeqReached)
            .OrderBy(award => award.LoginSeq);
    }
}
