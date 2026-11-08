using Google.Protobuf;
using Microsoft.Extensions.Options;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Credit;
using SpiritUNITE.Tools;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Lobby;

public sealed class LobbyNotices(GameData gameData, ActivitySchedule schedule, IOptions<LobbyOptions> options, TimeProvider clock)
{
    public const int PbScidPasterSprayerListNtf = 127750;
    public const int PbScidPasterSprayerAcntInfoNtf = 127752;
    public const int PbScidSubscriptionInterfaceRsp = 130652;

    private const int DefaultSchemeId = 1;
    private const int NoNationGroup = 0;
    private const int NewSaleTag = 1;
    private const int ThemeSprayType = (int)PB_ENUM_RES_PASTER_SPRAY_TYPE.PbResPasterSprayTypeForever;
    private const int PasterSprayInitialized = 1;
    private const int PasterSprayListField = 1;
    private const int AcntPasterSprayInfoField = 1;
    private const int TowerDefenseOpen = 1;
    private const int TowerDefenseClosed = 2;
    private const int TowerDefenseEnded = 3;
    private const int NoSubscriptionPeriod = 0;
    private const int TaskCompletedOnce = 1;
    private const int TaskNewTag = 1;
    private const int RedDotBadgeOne = 1;
    private const int OfficialAcntSysPlat = 1;
    private const int RegistBubTipID = 10;
    private const int RegistBubTipBadgeNum = 1;
    private const long CfgTimeOfDayBase = 1_000_000;

    private static readonly (int FieldNumber, long Value)[] SubscriptionInterfaceUnnamedVarints = [(9, 200), (10, 30)];

    private readonly LobbyOptions rules = options.Value;

    public pbCOMDT_USER_SCHEME_SELECT_INFO BuildUserSchemeSelectInfo() => new()
    {
        UserSchemeInfo =
        {
            rules.UserSchemesOrOfficial.Select(scheme => new pbCOMDT_USER_SCHEME_INFO
            {
                BusinessType = scheme.BusinessType,
                SchemeId = scheme.SchemeId,
            }),
        },
    };

    public int GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE businessType) =>
        rules.UserSchemesOrOfficial.FirstOrDefault(scheme => scheme.BusinessType == (int)businessType)?.SchemeId ?? DefaultSchemeId;

    public static pbSCBanPokemonListNtf BuildBanPokemonListNtf() => new();

    public pbSCCompetitionOpenInfoNtf BuildCompetitionOpenInfoNtf() => new()
    {
        IsOpen = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalCompetitionSwitch),
        BeginTime = GlobalCfgTime(
            PB_ENUM_RES_RESGLOBAL.PbResgolbalstrMapCompetitionBeginTimeYymmdd,
            PB_ENUM_RES_RESGLOBAL.PbResgolbalstrMapCompetitionBeginTimeHhmmss),
        EndTime = GlobalCfgTime(
            PB_ENUM_RES_RESGLOBAL.PbResgolbalstrMapCompetitionEndTimeYymmdd,
            PB_ENUM_RES_RESGLOBAL.PbResgolbalstrMapCompetitionEndTimeHhmmss),
    };

    private long GlobalCfgTime(PB_ENUM_RES_RESGLOBAL yymmdd, PB_ENUM_RES_RESGLOBAL hhmmss) =>
        UTCTimeUtil.CfgTime2ServerUnixTime((gameData.GetGlobalIntData(yymmdd) * CfgTimeOfDayBase) + gameData.GetGlobalIntData(hhmmss));

    public static pbSCLimitTimeReddotNtf BuildLimitTimeReddotNtf() => new();

    public pbCOMDT_SHOP_NEW_SALE_INFO_LIST BuildShopNewSaleInfo() => new()
    {
        NewSaleList =
        {
            rules.NewSaleShopIDs
                .Where(shopID => gameData.Shop.Contains(shopID))
                .Select(shopID => new pbCOMDT_SHOP_NEW_SALE_INFO { ShopID = shopID, NewTag = NewSaleTag }),
        },
    };

    public pbSCShopNewSaleInfoNtf BuildShopNewSaleInfoNtf() => new() { ShopNewSaleInfo = BuildShopNewSaleInfo() };

    public pbSCShopNewSaleInfoNtf? BuildLevelUpShopNewSaleInfoNtf(int levelBefore, int levelAfter)
    {
        var now = UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());
        var unlockedShopIDs = gameData.Shop.Rows
            .Where(shop => shop.ShopType == (int)PB_ENUM_RES_SHOP_TYPE.PbResShopTypeGeneralStore
                && shop.Level > levelBefore
                && shop.Level <= levelAfter
                && UTCTimeUtil.IsInTime(shop.OnTime, shop.OffTime, now))
            .Select(shop => shop.ShopID)
            .ToArray();
        if (unlockedShopIDs.Length == 0)
        {
            return null;
        }

        var info = BuildShopNewSaleInfo();
        info.NewSaleList.Add(unlockedShopIDs.Select(shopID => new pbCOMDT_SHOP_NEW_SALE_INFO { ShopID = shopID, NewTag = NewSaleTag }));
        info.PromotionList.Add(unlockedShopIDs.Select(shopID => new pbCOMDT_SHOP_PROMOTION_INFO { ShopID = shopID, NewTag = NewSaleTag }));
        return new pbSCShopNewSaleInfoNtf { ShopNewSaleInfo = info };
    }

    public pbSCQueryTowerDefenseNtf BuildQueryTowerDefenseNtf()
    {
        var now = UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());
        var period = gameData.TowerDefenseBase.Rows
            .Where(towerDefense => towerDefense.StartTime <= now)
            .MaxBy(towerDefense => towerDefense.StartTime);
        if (period is null)
        {
            return new pbSCQueryTowerDefenseNtf();
        }

        var status = now > period.DisplayEndTime ? TowerDefenseEnded
            : now > period.EndTime ? TowerDefenseClosed
            : TowerDefenseOpen;
        return new pbSCQueryTowerDefenseNtf { PeriodID = period.PeriodID, Status = status };
    }

    public pbSCSubscriptionStatusNtf BuildSubscriptionStatusNtf() => new()
    {
        IsOpen = rules.SubscriptionIsOpen,
        AcntSysPlat = OfficialAcntSysPlat,
    };

    public pbSCActivityInfoNtf BuildActivityInfoNtf(int countryCode) => new()
    {
        OpenActivityList = new pbCOMDT_ACTIVITY_OPEN_LIST
        {
            OpenActivityList =
            {
                schedule.FindActive(clock.GetUtcNow())
                    .Where(activity => IsOpenInNation(activity.Row, countryCode))
                    .OrderBy(activity => activity.Row.ActivityType)
                    .Select(activity => new pbCOMDT_ACTIVITY_OPEN_INFO { ActivityID = activity.Row.ActivityID }),
            },
        },
    };

    public static pbSCWeeklySettleCookieNtf BuildWeeklySettleCookieNtf() => new();

    public GamePacket[] BuildRefreshPackets(int countryCode) =>
    [
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewActivityTask)),
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidActivityInfoNtf, BuildActivityInfoNtf(countryCode)),
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidSubscriptionStatusNtf, BuildSubscriptionStatusNtf()),
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidQueryTowerDefenseNtf, BuildQueryTowerDefenseNtf()),
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdActivityExchangeReward)),
    ];

    public static pbSCMonthUseCouponsNtf BuildMonthUseCouponsNtf(long firstPayTime) => new()
    {
        MonthUseCouponsInfo = BuildMonthUseCouponsInfo(firstPayTime),
    };

    public static pbCOMDT_MONTH_USE_COUPONS_INFO BuildMonthUseCouponsInfo(long firstPayTime) => new() { FirstPayTime = firstPayTime };

    public pbSCUpdateTaskInfoNtf BuildCreditPointTaskInfoNtf(long recvTime) => new()
    {
        TaskInfoList =
        {
            gameData.TaskBase.Rows
                .Select(task => (Task: task, Condition: task.FinishCondition.FirstOrDefault(condition =>
                    condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdCreditpoint)))
                .Where(credit => credit.Condition is not null)
                .Select(credit => BuildCreditPointTaskInfo(credit.Task.TaskID, credit.Condition!.TargetProcess, recvTime)),
        },
    };

    public static pbCOMDT_BUB_TIP_DETAIL[] BuildRegistBubTips() =>
    [
        new pbCOMDT_BUB_TIP_DETAIL { BubTipID = RegistBubTipID, BubTipBadgeNum = RegistBubTipBadgeNum },
    ];

    public pbCOMDT_RED_DOT_DETAIL[] BuildRegistRedDots(int newEntrycardEditItemCount)
    {
        var newSaleCount = BuildShopNewSaleInfo().NewSaleList.Count;
        return
        [
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewEntrycardEditItem, newEntrycardEditItemCount),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdShop, newSaleCount),
            .. BuildRedDot(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdShopAvatar, newSaleCount),
        ];
    }

    public GamePacket[] BuildCouponsRefreshPackets(pbSCAcntCouponsRsp coupons, bool grantSubscriptionOpen) =>
    [
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidSubscriptionStatusNtf, BuildSubscriptionStatusNtf()),
        .. BuildSubscriptionInterfacePackets(),
        .. grantSubscriptionOpen && BuildSubscriptionOpenRedDotNtf() is { } subscriptionOpen
            ? [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, subscriptionOpen)]
            : Array.Empty<GamePacket>(),
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, coupons),
    ];

    public GamePacket[] BuildSubscriptionInterfaceReplies(pbSCAcntCouponsRsp coupons) =>
    [
        .. BuildSubscriptionInterfacePackets(),
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdSubscriptionOpen)),
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdSubscriptionPaster)),
        .. BuildCouponsRefreshPackets(coupons, grantSubscriptionOpen: false),
    ];

    public pbSCRedDotReplaceNtf? BuildSubscriptionOpenRedDotNtf() => rules.SubscriptionIsOpen == 0
        ? null
        : BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdSubscriptionOpen, RedDotBadgeOne);

    public static pbSCRedDotReplaceNtf BuildRedDotReplaceNtf(PB_ENUM_RES_RED_DOT_ID redDotID, int redDotBadgeNum) => new()
    {
        RedDotInfo = new pbCOMDT_RED_DOT_INFO { RedDotDetail = { BuildRedDot(redDotID, redDotBadgeNum) } },
    };

    public byte[] BuildSubscriptionInterfaceRsp()
    {
        var known = new pbSCSubscriptionInterfaceRspKnownFields(
            FindCurSubscriptionPeriodID(),
            new pbCOMDT_SUBSCRIPTION_BASE_INFO
            {
                AcntSysPlat = OfficialAcntSysPlat,
                IsFirstSubscription = (int)PB_ENUM_COM_FIRST_SUBSCRIPTION_TYPE.PbComFirstSubscriptionTypeNever,
                CanCollectCoupons = (int)PB_ENUM_COM_SUBSCRIPTION_COUPONS_TYPE.PbComSubscriptionCouponsCanCollect,
            },
            new pbCOMDT_ACNT_SUBSCRIPTION_FREE_INFO
            {
                FreePokemonList = { rules.SubscriptionFree.FreePokemonList },
                FreePasterList = { rules.SubscriptionFree.FreePasterList },
            });
        using var buffer = new MemoryStream();
        using (var output = new CodedOutputStream(buffer))
        {
            known.WriteTo(output);
            foreach (var (fieldNumber, value) in SubscriptionInterfaceUnnamedVarints)
            {
                output.WriteTag(fieldNumber, WireFormat.WireType.Varint);
                output.WriteInt64(value);
            }
        }

        return buffer.ToArray();
    }

    public static pbSCRedDotClearNtf BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID redDotID) => new() { RedDotID = (int)redDotID };

    public static (PB_ENUM_CS_MSGID_DEF MsgID, IMessage Body) BuildRedDotCountNtf(PB_ENUM_RES_RED_DOT_ID redDotID, int redDotBadgeNum) =>
        redDotBadgeNum > 0
            ? (PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
            {
                RedDotInfo = new pbCOMDT_RED_DOT_INFO
                {
                    RedDotDetail = { new pbCOMDT_RED_DOT_DETAIL { RedDotID = (int)redDotID, RedDotBadgeNum = redDotBadgeNum } },
                },
            })
            : (PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, BuildRedDotClearNtf(redDotID));

    public static byte[] BuildPasterSprayerListNtf()
    {
        using var buffer = new MemoryStream();
        using (var output = new CodedOutputStream(buffer))
        {
            output.WriteTag(PasterSprayListField, WireFormat.WireType.LengthDelimited);
            output.WriteLength(0);
        }

        return buffer.ToArray();
    }

    public byte[] BuildPasterSprayerAcntInfoNtf(LMNRoleInfo role, long initTime)
    {
        var now = UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());
        var info = new pbCOMDT_ACNT_PASTER_SPRAY_INFO
        {
            InitStatus = PasterSprayInitialized,
            InitTime = initTime,
            ExclusiveInvalidList = new pbCOMDT_PASTER_SPRAY_EXCLUSIVE_INVALID_LIST(),
            ThemeRewardList = new pbCOMDT_PASTER_SPRAY_THEME_LIST
            {
                ThemeSprayInfo =
                {
                    gameData.PasterSpray.Rows
                        .Where(spray => spray.SprayType == ThemeSprayType && spray.OnTime < now)
                        .Select(spray => new pbCOMDT_PASTER_SPRAY_THEME_INFO
                        {
                            PasterSprayID = spray.PasterSprayID,
                            LastUpdateTime = initTime,
                            RewardList = new pbCOMDT_PASTER_SPRAY_THEME_REWARD_LIST(),
                        }),
                },
            },
            ExclusiveGainList = new pbCOMDT_PASTER_SPRAY_EXCLUSIVE_GAIN_LIST(),
            ExclusiveGainRedDotList = new pbCOMDT_PASTER_SPRAY_EXCLUSIVE_GAIN_RED_DOT_LIST(),
            ExclusiveGainStatusList = new pbCOMDT_PASTER_SPRAY_EXCLUSIVE_GAIN_STATUS_LIST
            {
                PokemonList =
                {
                    role.GetOwnedPokemonIds()
                        .Order()
                        .Select(pokemonId => new pbCOMDT_PASTER_SPRAY_EXCLUSIVE_GAIN_STATUS_INFO { PokemonID = pokemonId }),
                },
            },
        };
        return WrapMessage(AcntPasterSprayInfoField, info);
    }

    private GamePacket[] BuildSubscriptionInterfacePackets() =>
    [
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdSubscriptionCoupons)),
        new GamePacket(PbScidSubscriptionInterfaceRsp, 0, 0, BuildSubscriptionInterfaceRsp()),
    ];

    private int FindCurSubscriptionPeriodID()
    {
        var now = UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());
        return gameData.SubScriptionPeriod.Rows
            .Where(period => period.StartTime <= now && now < period.EndTime)
            .MaxBy(period => period.StartTime)?.ID ?? NoSubscriptionPeriod;
    }

    private pbCOMDT_TASK_INFO BuildCreditPointTaskInfo(int taskID, int targetProcess, long recvTime)
    {
        var completed = CreditManager.StartingCreditPoint >= targetProcess;
        var taskInfo = new pbCOMDT_TASK_INFO
        {
            TaskId = taskID,
            TaskSystem = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemCreditpoint,
            TaskProcess = { Math.Min(CreditManager.StartingCreditPoint, targetProcess) },
            Status = (int)(completed ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete : PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved),
            RecvTime = recvTime,
            NewTag = TaskNewTag,
        };
        if (completed)
        {
            taskInfo.CompleteNum = TaskCompletedOnce;
            taskInfo.CompleteTime = recvTime;
        }

        return taskInfo;
    }

    private static pbCOMDT_RED_DOT_DETAIL[] BuildRedDot(PB_ENUM_RES_RED_DOT_ID redDotID, int redDotBadgeNum) =>
        redDotBadgeNum > 0 ? [new pbCOMDT_RED_DOT_DETAIL { RedDotID = (int)redDotID, RedDotBadgeNum = redDotBadgeNum }] : [];

    private sealed record pbSCSubscriptionInterfaceRspKnownFields(
        int CurPeriodID,
        pbCOMDT_SUBSCRIPTION_BASE_INFO BaseInfo,
        pbCOMDT_ACNT_SUBSCRIPTION_FREE_INFO FreeInfo)
    {
        private const int CurPeriodIDField = 1;
        private const int BaseInfoField = 2;
        private const int FreeInfoField = 4;
        private const int ResultInfoField = 5;

        public void WriteTo(CodedOutputStream output)
        {
            if (CurPeriodID != 0)
            {
                output.WriteTag(CurPeriodIDField, WireFormat.WireType.Varint);
                output.WriteInt32(CurPeriodID);
            }

            WriteMessage(output, BaseInfoField, BaseInfo);
            WriteMessage(output, FreeInfoField, FreeInfo);
            WriteMessage(output, ResultInfoField, new pbResultInfo());
        }

        private static void WriteMessage(CodedOutputStream output, int fieldNumber, IMessage message)
        {
            output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            output.WriteMessage(message);
        }
    }

    private bool IsOpenInNation(pbActivity_Base activity, int countryCode)
    {
        var groups = activity.NationGroupList.Where(group => group != NoNationGroup).ToArray();
        return groups.Length == 0
            || groups.Any(group => gameData.NationGroup.Find(group)?.CountryCode.Contains(countryCode) == true);
    }

    private static byte[] WrapMessage(int fieldNumber, IMessage message)
    {
        using var buffer = new MemoryStream();
        using (var output = new CodedOutputStream(buffer))
        {
            output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
            output.WriteMessage(message);
        }

        return buffer.ToArray();
    }
}
