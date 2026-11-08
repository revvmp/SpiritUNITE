import argparse
import base64
import json
import re
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

DEFAULT_PROTO = Path("src/SpiritUnite.Core/Protobuf/pb_protocol.proto")
DEFAULT_OUTPUT = Path("src/SpiritUnite.Core/Databin/Tables")


@dataclass(frozen=True)
class Field:
    name: str
    type: str
    repeated: bool = False


@dataclass(frozen=True)
class Table:
    message: str
    named_fields: dict[int, Field] | None = None
    key_field: str | None = None
    kept_fields: tuple[str, ...] | None = None

    @property
    def drops_unnamed_fields(self):
        return self.named_fields is not None


LUA_ONLY_FIELDS = {
    "pbGuildLabel": {
        225525: Field("LableID", "int32"),
        246154: Field("LabelType", "int32"),
    },
    "pbGuildActivity_Base": {
        225340: Field("PeriodID", "int32"),
        201006: Field("PrepareTime", "int64"),
        288591: Field("StartTime", "int64"),
        294988: Field("EndTime", "int64"),
        278744: Field("RewardPoolID", "int32"),
    },
    "pbGuildActivity_RewardPool": {
        232271: Field("PoolID", "int32"),
        271458: Field("RewardID", "int32", repeated=True),
    },
    "pbGuildActivity_ScoreGear": {
        205430: Field("GearID", "int32"),
        223098: Field("Score", "int32"),
    },
    "pbGuildActivity_ScoreReward": {
        271458: Field("RewardID", "int32"),
        205430: Field("GearID", "int32"),
        256687: Field("SDropID", "int32"),
    },
    "pbGuildActivity_Task": {
        271458: Field("RewardID", "int32"),
        289808: Field("TaskID", "int32", repeated=True),
    },
    "pbPvePrivilegeActivity": {
        244760: Field("ActivityID", "int32"),
        265903: Field("WeekExtraRewardNum", "int32"),
        263714: Field("WeekRewardLimit", "int32"),
        235416: Field("CostCoupons", "int32"),
        242945: Field("UnlockBuffNum", "int32"),
        217633: Field("PrivilegeKeepTime", "int32"),
    },
    "pbPveChallengeShop": {
        225340: Field("PeriodID", "int32"),
        297445: Field("ShopID", "int32"),
        223098: Field("Score", "int32"),
        213727: Field("BuyNumLimit", "int32"),
        203263: Field("ResID", "int32"),
        205709: Field("IsBuff", "int32"),
        251718: Field("GroupId", "int32"),
        270334: Field("Level", "int32"),
        265356: Field("JumpLink", "int32"),
    },
    "pbPveBuffLevelUpDesign": {
        266095: Field("Id", "int32"),
        251718: Field("GroupId", "int32"),
        270334: Field("Level", "int32"),
    },
    "pbActivityExchange": {
        274344: Field("ExchangeID", "int32"),
        288083: Field("ExchangeGroupID", "int32"),
        294986: Field("FromRes", "pbFromRes", repeated=True),
        289336: Field("ToResID", "int32"),
        241047: Field("ToResNum", "int32"),
        205464: Field("ToDst", "pbToDst", repeated=True),
        201878: Field("MaxDoneNum", "int32"),
        271334: Field("CustomTime", "int32"),
        291705: Field("ValidStartTime", "int64"),
        229558: Field("ValidEndTime", "int64"),
        258440: Field("NeedPvePrivilege", "int32"),
        243906: Field("PvePrivilegeExtra", "int32"),
    },
    "pbFirstChargeAward": {
        256220: Field("LoginSeq", "int32"),
        269784: Field("DropID", "int32"),
    },
    "pbMysteryShopDiscount": {
        244760: Field("ActivityID", "int32"),
        277708: Field("AreaID", "int32"),
        233113: Field("Discount", "int32"),
    },
    "pbReportText": {
        224410: Field("ReportCategory", "int32"),
        262118: Field("ReportReason", "int32"),
    },
    "pbMatchTrainer": {
        217303: Field("MapId", "int32"),
        282995: Field("DifficultyLevel", "int32"),
        294469: Field("NeedCnt", "int32"),
    },
    "pbMvp_Conf": {
        244570: Field("GameType", "int32"),
        284285: Field("ParamVec", "int32", repeated=True),
        299811: Field("ParamType", "int32"),
    },
    "pbSystemMenuList": {
        239936: Field("MenuItem", "int32"),
    },
    "pbLobbyBg": {
        212370: Field("TransmuteId", "int32"),
        250142: Field("LobbyBgID", "int32"),
        266240: Field("OnTime", "int64"),
        280039: Field("IsDefault", "int32"),
        282277: Field("JumpID", "int32"),
        297667: Field("TransmuteNum", "int32"),
    },
    "pbNewbieChallengeTask": {
        211327: Field("TaskDay", "int32"),
        257394: Field("DayTaskID", "int32"),
        259562: Field("SubTaskID", "int32", repeated=True),
        269998: Field("SchemeId", "int32"),
    },
    "pbNewbieGift": {
        256687: Field("SDropID", "int32"),
        269998: Field("SchemeId", "int32"),
        289481: Field("GiftID", "int32"),
        296267: Field("ShowType", "int32"),
    },
    "pbStarRoadSite": {
        225519: Field("SiteType", "int32"),
        240550: Field("NeedScore", "int32"),
        255685: Field("SlotNum", "int32"),
        260146: Field("SiteID", "int32"),
        266240: Field("OnTime", "int64"),
        298876: Field("PokemonIDList", "int32", repeated=True),
    },
    "pbStarRoadPokemon": {
        232556: Field("PokemonId", "int32"),
        236844: Field("PokemonCoupons", "int32"),
    },
    "pbAchievement": {
        223098: Field("Score", "int32"),
        240759: Field("AchievementTaskID", "int32"),
        245693: Field("AchievementType", "int32"),
        270106: Field("AchievementID", "int32"),
    },
    "pbBattlePassSeason": {
        205541: Field("ElitePassRealPrice", "int32"),
        206330: Field("ElitePassDropID", "int32"),
        220905: Field("FullLevelTreasureBox", "int32"),
        223064: Field("SeasonID", "int32"),
        223390: Field("SeasonStartTime", "int64"),
        236957: Field("PrimaryPackLevel", "int32"),
        237363: Field("FullLevelTreasureBoxUpper", "int32"),
        248624: Field("PrimaryPackPrice", "int32"),
        252197: Field("ElitePassLevelUp", "int32"),
        253707: Field("ElitePassCoinType", "int32"),
        255263: Field("SeasonEndTime", "int64"),
        258169: Field("HighPackLevel", "int32"),
        259704: Field("FullLevelTreasureBoxEXP", "int32"),
        282987: Field("CommonPassCoinType", "int32"),
        297191: Field("CommonPassRealPrice", "int32"),
        297622: Field("HighPackPrice", "int32"),
    },
    "pbBattlePassInfo": {
        202953: Field("OutSideItemId", "int32", repeated=True),
        223064: Field("SeasonID", "int32"),
        237151: Field("ExtraPassDropID", "int32"),
        241787: Field("HighPassDropID", "int32"),
        244596: Field("DiscountPrice", "int32", repeated=True),
    },
    "pbBattlePass_SeasonTask": {
        211384: Field("WeekIndex", "int32"),
        215292: Field("WelfareTaskRefreshNum", "int32"),
        223064: Field("SeasonID", "int32"),
        233840: Field("WelfareTaskID", "int32", repeated=True),
        252175: Field("WeekMaxExp", "int32"),
        289808: Field("TaskID", "int32", repeated=True),
    },
    "pbBattlePass_TimeLimitTask": {
        223064: Field("SeasonID", "int32"),
        250088: Field("TimeLimitTaskID", "int32"),
        258156: Field("BeginTime", "int64"),
        266127: Field("ID", "int32"),
        294988: Field("EndTime", "int64"),
    },
    "pbReward_Pool": {
        236391: Field("StartTime", "int64"),
        258880: Field("EndTime", "int64"),
        259142: Field("LotteryPoolId", "int32"),
    },
    "pbCanopy_Base": {
        266240: Field("OnTime", "int64"),
        283821: Field("CanopyID", "int32"),
    },
    "pbCardFace": {
        232556: Field("PokemonID", "int32"),
        247630: Field("CardFaceID", "int32"),
        266240: Field("OnTime", "int64"),
    },
    "pbPersonStyleHeadFrame": {
        221526: Field("HeadIconID", "int32"),
        266240: Field("OnTime", "int64"),
        282160: Field("IsOpen", "int32"),
        295986: Field("Transmuteid", "int32"),
        297667: Field("TransmuteNum", "int32"),
    },
    "pbPersonalityLabel": {
        266127: Field("ID", "int32"),
        277336: Field("Category", "int32"),
    },
    "pbHeldItemSystemConfig": {
        245971: Field("LevelStages", "int32", repeated=True),
        266095: Field("Id", "int32"),
        278460: Field("MaxLevel", "int32"),
    },
    "pbBadgeUnlock": {
        207649: Field("CookiesBuy", "int32"),
        207889: Field("PokeCoinsBuy", "int32"),
        224071: Field("IsCookiesBuy", "int32"),
        228681: Field("IsPokeCoinsBuy", "int32"),
        239227: Field("IsCouponsBuy", "int32"),
        255043: Field("CouponsBuy", "int32"),
        291249: Field("BadgeSchemeID", "int32"),
    },
    "pbNationGroup": {
        234861: Field("CountryCode", "int32", repeated=True),
        276539: Field("NationGroupID", "int32"),
    },
    "pbSubScriptionPeriod": {
        266127: Field("ID", "int32"),
        288591: Field("StartTime", "int64"),
        294988: Field("EndTime", "int64"),
    },
    "pbTowerDefenseBase": {
        225340: Field("PeriodID", "int32"),
        266186: Field("DisplayEndTime", "int64"),
        288591: Field("StartTime", "int64"),
        294988: Field("EndTime", "int64"),
    },
    "pbTrainerExpGift": {
        206673: Field("IsBigAward", "int32"),
        224136: Field("BigAwardParam", "int32"),
        229878: Field("AwardParam", "int32"),
        230419: Field("AwardType", "int32"),
        269998: Field("SchemeId", "int32"),
        270334: Field("Level", "int32"),
        288065: Field("TotalExp", "int32"),
        262487: Field("AwardItemParam", "int32", repeated=True),
    },
    "pbTrainerSeasonExpGift": {
        223064: Field("SeasonID", "int32"),
        288065: Field("TotalExp", "int32"),
    },
    "pbPasterSpray": {
        230559: Field("SprayType", "int32"),
        266240: Field("OnTime", "int64"),
        271147: Field("PasterSprayID", "int32"),
        288997: Field("SprayBuffID", "int32"),
        298384: Field("PokemonLimit", "int32"),
    },
    "pbIntimacySetting": {
        266127: Field("ID", "int32"),
        210795: Field("Value", "int32"),
    },
    "pbIntimacyRule": {
        266127: Field("ID", "int32"),
        210795: Field("Value", "string"),
    },
    "pbIntimacyLevel": {
        270334: Field("Level", "int32"),
        279019: Field("UpgradeTotalExp", "int32"),
    },
    "pbBadgeColor": {
        241164: Field("BadgeColorLv", "int32"),
        266754: Field("BadgeColor", "int32"),
        299143: Field("LvNeedCount", "int32"),
    },
    "pbDrop_Base": {
        224348: Field("DropSeq", "int32"),
        246521: Field("DropPackID", "int32"),
        269784: Field("DropID", "int32"),
    },
    "pbDrop_Pack": {
        203263: Field("ResID", "int32"),
        209248: Field("Weight", "int32"),
        211808: Field("MinDropCount", "int32"),
        232538: Field("MaxDropCount", "int32"),
        246521: Field("DropPackID", "int32"),
        290483: Field("DropPackSeq", "int32"),
    },
    "pbRankedMatchMapOpenTime": {
        217335: Field("MapID", "int32"),
        223064: Field("SeasonID", "int32"),
        223148: Field("Index", "int32"),
        265497: Field("CloseTime", "int64"),
        285755: Field("OpenTime", "int64"),
        287307: Field("TrainMapID", "int32"),
    },
    "pbScene_OpenTime": {
        223148: Field("Index", "int32"),
        262999: Field("SceneId", "int32"),
        250068: Field("OpenDate", "int64"),
        224768: Field("CloseDate", "int64"),
        294206: Field("DisplayDate", "int64"),
        265884: Field("HideDate", "int64"),
        285755: Field("OpenTime", "int64"),
        265497: Field("CloseTime", "int64"),
        260263: Field("OpenMode", "int32"),
        268252: Field("OpenModeParaA", "string"),
        268249: Field("OpenModeParaB", "int32"),
        268250: Field("OpenModeParaC", "int32"),
        212815: Field("IsShowDate", "int32"),
    },
    "pbRankMatchPointMedal": {
        223098: Field("Score", "int32"),
        232230: Field("MedalGroup", "int32"),
        254318: Field("MedalID", "int32"),
        266513: Field("MedalType", "int32"),
        280152: Field("Priority", "int32"),
    },
    "pbRankedMatchSeasonTaskAward": {
        230196: Field("HistoryDivisionCanAward", "int32"),
        256687: Field("SDropID", "int32"),
        262339: Field("IsRecvShow", "int32"),
        274622: Field("BigRankedDivision", "int32"),
        285632: Field("RankedDivision", "int32"),
        299833: Field("AwardGroupId", "int32"),
    },
    "pbNewPlayerDefaultRes": {
        218513: Field("NewplayerRewardId", "int32"),
        232620: Field("ResourceNum", "int32"),
        239380: Field("ResourceType", "int32"),
        282173: Field("ResourceId", "int32"),
    },
    "pbUniteChallenge": {
        225340: Field("PeriodID", "int32"),
        235280: Field("ShowMapId", "int32"),
        252587: Field("WinReward", "pbUniteChallengeWinReward", repeated=True),
        265497: Field("CloseTime", "int64"),
        266127: Field("ID", "int32"),
        285755: Field("OpenTime", "int64"),
    },
    "pbTowerDefenseShop": {
        203263: Field("ResID", "int32"),
        205709: Field("IsBuff", "int32"),
        213727: Field("BuyNumLimit", "int32"),
        223098: Field("Score", "int32"),
        225340: Field("PeriodID", "int32"),
        245712: Field("GameLevel", "int32"),
        251718: Field("GroupId", "int32"),
        266240: Field("OnTime", "int64"),
        270334: Field("Level", "int32"),
        286796: Field("OffTime", "int64"),
        297445: Field("ShopID", "int32"),
    },
    "pbTowerDefensePassAward": {
        225340: Field("PeriodID", "int32"),
        256687: Field("SDropID", "int32"),
        262999: Field("SceneId", "int32"),
    },
    "pbBossRushActivityBase": {
        225340: Field("PeriodID", "int32"),
        266186: Field("DisplayEndTime", "int64"),
        271458: Field("RewardID", "int32"),
        288591: Field("StartTime", "int64"),
        294988: Field("EndTime", "int64"),
    },
    "pbBossRushGuildActivityScoreAward": {
        205430: Field("GearID", "int32"),
        223098: Field("Score", "int32"),
        256687: Field("SDropID", "int32"),
        271458: Field("RewardID", "int32"),
    },
    "pbMegaEvolutionOther": {
        243219: Field("OtherType", "int32"),
        244760: Field("ActivityID", "int32"),
        270501: Field("OtherVal", "int32"),
    },
    "pbMegaEvolutionReward": {
        202350: Field("TargetComplete", "int32"),
        221161: Field("AwardDropID", "int32"),
        244760: Field("ActivityID", "int32"),
        288679: Field("StateIndex", "int32"),
        299688: Field("AwardKey", "int32"),
    },
    "pbMegaEvolutionTaskPool": {
        223148: Field("Index", "int32"),
        236922: Field("CompleteCoupons", "int32"),
        244760: Field("ActivityID", "int32"),
        263409: Field("TaskType", "int32"),
        289808: Field("TaskID", "int32"),
    },
    "pbEeveeChallenge": {
        205577: Field("EeveeID", "int32"),
        244760: Field("ActivityID", "int32"),
        258104: Field("TaskIDList", "int32", repeated=True),
    },
    "pbBanPickRule": {
        200698: Field("BanMode", "int32"),
        248168: Field("Round", "int32", repeated=True),
        269833: Field("ExceptBanCD", "int32"),
        272931: Field("BanPickRuleId", "int32"),
        274750: Field("ViewVoteBanNum", "int32"),
        287974: Field("VoteCampBanNum", "int32"),
    },
    "pbFriendlyRoomRule": {
        203336: Field("RelationClose", "int32", repeated=True),
        242484: Field("RelationOpen", "int32", repeated=True),
        266095: Field("Id", "int32"),
    },
    "pbRoundRule": {
        220405: Field("BanOrPick", "int32"),
        237529: Field("RoundRuleId", "int32"),
        239263: Field("TimeOut", "int32"),
        247017: Field("CampTwoPos", "int32", repeated=True),
        251891: Field("CampOnePos", "int32", repeated=True),
    },
    "pbTenGames": {
        263802: Field("GamesId", "int32"),
        294206: Field("DisplayDate", "int64"),
        265884: Field("HideDate", "int64"),
        250068: Field("OpenDate", "int64"),
        224768: Field("CloseDate", "int64"),
        283815: Field("OpenGameTime", "int64", repeated=True),
        227203: Field("CloseGameTime", "int64", repeated=True),
        275391: Field("SettleEndTime", "int64"),
        264707: Field("JoinReward", "int32"),
        225220: Field("CanApplyMember", "int32", repeated=True),
        203478: Field("MapList", "int32", repeated=True),
    },
    "pbTenGamesRankReward": {
        263802: Field("GamesId", "int32"),
        205430: Field("GearID", "int32"),
        218630: Field("StartRank", "int32"),
        296573: Field("EndRank", "int32"),
        248095: Field("RewardDropID", "int32"),
        273074: Field("IsParticipationAward", "int32"),
    },
    "pbTenGamesBPActivityOption": {
        244760: Field("ActivityID", "int32"),
        236745: Field("SenierCertCoupons", "int32"),
    },
    "pbTenGamesBPActivityReward": {
        244760: Field("ActivityID", "int32"),
        277077: Field("Seq", "int32"),
        213775: Field("WinCnt", "int32"),
        216746: Field("FreeDropID", "int32"),
        249166: Field("SenierDropID", "int32"),
    },
    "pbPredictionTime": {
        299365: Field("PredictionStatge", "int32"),
        288591: Field("StartTime", "int64"),
        294988: Field("EndTime", "int64"),
    },
}

SERVER_FIELD_SUBSETS = {
    "pbActivity_Base": {
        229558: Field("ValidEndTime", "int64"),
        230959: Field("TaskGroupIDList", "int32", repeated=True),
        231174: Field("NewbieShownTime", "int32"),
        239066: Field("NationGroupList", "int32", repeated=True),
        244760: Field("ActivityID", "int32"),
        256887: Field("ActivityType", "int32"),
        291705: Field("ValidStartTime", "int64"),
        291726: Field("ActivityParam", "int32"),
    },
    "pbTask_Base": {
        227389: Field("ResetType", "int32"),
        251686: Field("GroupID", "int32"),
        256687: Field("SDropID", "int32"),
        266136: Field("FinishCondition", "pbFinishCondition", repeated=True),
        289808: Field("TaskID", "int32"),
    },
    "pbBadge_Base": {
        208485: Field("BadgeGroupId", "int32"),
        212370: Field("TransmuteId", "int32"),
        217461: Field("UpgradeCost", "pbUpgradeCost", repeated=True),
        247056: Field("BadgeQuality", "int32"),
        261074: Field("BadgeId", "int32"),
        282986: Field("BadgePictureId", "int32"),
        291479: Field("BadgeColors", "int32", repeated=True),
        297667: Field("TransmuteNum", "int32"),
    },
    "pbPokemon_Base": {
        229827: Field("OccupationType", "int32"),
        232524: Field("PokemonId", "int32"),
        238850: Field("BranchRecommend", "int32", repeated=True),
        240544: Field("HideInBox", "int32"),
        241002: Field("MainUnitId", "int32"),
        201545: Field("OriginalForm", "int32"),
        211739: Field("IsMultiform", "int32"),
        250599: Field("RecommendEquips", "int32", repeated=True),
    },
    "pbUnit_Base": {
        259263: Field("UnitId", "int32"),
        269633: Field("HeroMode", "int32", repeated=True),
        271778: Field("MonsterClassType", "int32"),
    },
}

NESTED_MESSAGE_FIELDS = {
    "pbUniteChallengeWinReward": {
        269752: Field("DropId", "int32"),
    },
    "pbFinishCondition": {
        266127: Field("ID", "int32"),
        270539: Field("ParamD", "int32", repeated=True),
        270541: Field("ParamB", "int32", repeated=True),
        270544: Field("ParamA", "int32", repeated=True),
        288334: Field("TargetProcess", "int32"),
    },
    "pbUpgradeCost": {
        249005: Field("Probability", "int32"),
        252306: Field("Gold", "int32"),
        267875: Field("BadgeNum", "int32"),
    },
    "pbEliteAward": {
        260768: Field("Num", "int32"),
        266127: Field("ID", "int32"),
    },
    "pbBPWeekTask": {
        289808: Field("TaskID", "int32"),
    },
    "pbWelfareTask": {
        289808: Field("TaskID", "int32"),
    },
    "pbFromRes": {
        260768: Field("Num", "int32"),
        266127: Field("ID", "int32"),
    },
    "pbToDst": {
        203263: Field("ResID", "int32"),
        293282: Field("ResNum", "int32"),
    },
}

SHOP_SERVER_FIELDS = (
    "ShopID",
    "ShopType",
    "ShopResID",
    "ABTestID",
    "OnTime",
    "OffTime",
    "IsPreView",
    "PreViewStartTime",
    "PreViewEndTime",
    "Level",
    "BuyLimitType",
    "BuyLimitNum",
    "CanBulkBuy",
    "IsCookiesBuy",
    "CookiesBuy",
    "DiscountCookiesBuy",
    "IsPokeCoinsBuy",
    "PokeCoinsBuy",
    "DiscountPokeCoinsBuy",
    "IsCouponsBuy",
    "CouponsBuy",
    "DiscountCouponsBuy",
    "IsCandysBuy",
    "IsFramentBuy",
    "FramentBuy",
    "DiscountFramentBuy",
    "IsCommonBuy",
    "IsBuyMixPay",
    "DiscountStartTime",
    "DiscountEndTime",
    "SubShopItemIDs",
)

OUT_SIDE_ITEM_SERVER_FIELDS = (
    "OutSideItemId",
    "OutSideItemType",
    "ItemParam",
    "OutSideItemCategory",
    "OutSideItemTypeParam",
    "OutSideItemStackSize",
    "IsCanSell",
    "IsCanBulkSell",
    "BulkSellLimit",
    "SellCurrency",
    "SellPrice",
    "IsCanLevelUp",
    "Transmuteid",
    "TransmuteNum",
    "ExpireTransId",
    "ExpireTransNum",
)

HELD_ITEM_LEVEL_UP_DESIGN_SERVER_FIELDS = ("Id", "GroupId", "Level", "LevelUpCost", "LevelUpCostID")

SYSTEM_UNLOCK_SERVER_FIELDS = ("ID", "SchemeId", "TrainerExp", "TrainerLevel")


NEWBIE_GUIDE_STEPS_SERVER_FIELDS = ("NewbieEnum", "StepIndex", "GuideType", "GuideParamInt")

PVE_CHALLENGE_STAGE_ROUTE_SERVER_FIELDS = (
    "StageID",
    "NodeID",
    "DifficultyLevel",
    "SceneId",
    "BattleId",
    "IsUnLock",
    "IsNormalType",
    "NextUnLock",
    "NextUnLockHard",
    "EnemyldList",
    "TeamldList",
    "TeamPickMode",
    "OneStarDrops",
    "TwoStarDrops",
    "ThreeStarDrops",
    "CompleteDrops",
    "RefreshDrops",
    "TaskID",
)

CONTESTANT_CARD_DECORATION_SERVER_FIELDS = ("DecorationID", "DecorationType", "IsDefault", "OnTime", "Transmuteid", "TransmuteNum")

HEAD_ICON_SERVER_FIELDS = ("HeadIconID", "IsOpen", "OnTime", "Transmuteid", "TransmuteNum")

BATTLE_SIGNAL_COMMUNICATE_SERVER_FIELDS = ("Id", "SignalType", "PhraseLocation")

NATION_SELECT_INFORMATION_SERVER_FIELDS = ("NationID", "ArpRegionID")

POKEMON_AVATAR_BASE_SERVER_FIELDS = (
    "PokemonAvatarId",
    "PokemonId",
    "OnTime",
    "Quality",
    "DefaultExpandEffectPackIDList",
    "Transmuteid",
    "TransmuteNum",
)

POKEMON_EQUIPMENT_SERVER_FIELDS = ("EquipmentId", "Category")

SKILL_EFFECT_EXPAND_PACK_BASE_SERVER_FIELDS = (
    "SkillEffectsExpandPackID",
    "SkillEffectsExpandPackType",
    "PokemonAvatarId",
    "OnTime",
    "Transmuteid",
    "TransmuteNum",
)

SCENE_MAP_SERVER_FIELDS = (
    "SceneId",
    "MapMode",
    "MapSubMode",
    "ShowInList",
    "CampNum",
    "CampPlayerNum",
    "CampMatchPlayerNum",
    "GameType",
    "GameLevel",
    "SortPriority",
    "PickTimeOut",
    "PickReminderSec",
    "MapBaseId",
    "HangupSchemeID",
    "OpenHeroMode",
    "SurrenderStartTime",
    "SurrenderCDTime",
    "SurrenderInvalidTime",
    "SurrenderTimesLimit",
    "Timeout",
    "CampOneBanPickRuleID",
    "CampTwoBanPickRuleID",
    "PickHeroMode",
    "PickAdjustSec",
    "SubModeFriendRule",
    "ChallengeTrainerMode",
)

RANKED_DIVISION_SERVER_FIELDS = (
    "Index",
    "RankedDivision",
    "BigRankedDivision",
    "UpgradeWinPoint",
    "TicketConsume",
    "DivisionFallProtect",
    "MasterPointFloor",
    "MasterPointCeil",
    "MedalPoint",
)

MAP_BASE_SERVER_FIELDS = ("MapBaseId", "MatchTime")

POKEMON_RELEASE_SERVER_FIELDS = ("PokemonId", "OnTime", "Order")

TABLES = {
    "databin_TenGames": Table("pbTenGames", LUA_ONLY_FIELDS["pbTenGames"], key_field="GamesId"),
    "databin_TenGamesRankReward": Table("pbTenGamesRankReward", LUA_ONLY_FIELDS["pbTenGamesRankReward"]),
    "databin_TenGamesBPActivityOption": Table(
        "pbTenGamesBPActivityOption", LUA_ONLY_FIELDS["pbTenGamesBPActivityOption"], key_field="ActivityID"
    ),
    "databin_TenGamesBPActivityReward": Table(
        "pbTenGamesBPActivityReward", LUA_ONLY_FIELDS["pbTenGamesBPActivityReward"]
    ),
    "databin_PredictionTime": Table(
        "pbPredictionTime", LUA_ONLY_FIELDS["pbPredictionTime"], key_field="PredictionStatge"
    ),
    "databin_GuildLabel": Table("pbGuildLabel", LUA_ONLY_FIELDS["pbGuildLabel"], key_field="LableID"),
    "databin_ReportText": Table("pbReportText", LUA_ONLY_FIELDS["pbReportText"]),
    "databin_PvePrivilegeActivity": Table(
        "pbPvePrivilegeActivity", LUA_ONLY_FIELDS["pbPvePrivilegeActivity"], key_field="ActivityID"
    ),
    "databin_PveChallengeShop": Table("pbPveChallengeShop", LUA_ONLY_FIELDS["pbPveChallengeShop"]),
    "databin_PveBuffLevelUpDesign": Table("pbPveBuffLevelUpDesign", LUA_ONLY_FIELDS["pbPveBuffLevelUpDesign"], key_field="Id"),
    "databin_ActivityExchange": Table(
        "pbActivityExchange", LUA_ONLY_FIELDS["pbActivityExchange"], key_field="ExchangeID"
    ),
    "databin_FirstChargeAward": Table("pbFirstChargeAward", LUA_ONLY_FIELDS["pbFirstChargeAward"], key_field="LoginSeq"),
    "databin_MysteryShopDiscount": Table("pbMysteryShopDiscount", LUA_ONLY_FIELDS["pbMysteryShopDiscount"]),
    "databin_GuildActivity_Base": Table(
        "pbGuildActivity_Base", LUA_ONLY_FIELDS["pbGuildActivity_Base"], key_field="PeriodID"
    ),
    "databin_GuildActivity_RewardPool": Table(
        "pbGuildActivity_RewardPool", LUA_ONLY_FIELDS["pbGuildActivity_RewardPool"], key_field="PoolID"
    ),
    "databin_GuildActivity_ScoreGear": Table(
        "pbGuildActivity_ScoreGear", LUA_ONLY_FIELDS["pbGuildActivity_ScoreGear"], key_field="GearID"
    ),
    "databin_GuildActivity_ScoreReward": Table(
        "pbGuildActivity_ScoreReward", LUA_ONLY_FIELDS["pbGuildActivity_ScoreReward"]
    ),
    "databin_GuildActivity_Task": Table(
        "pbGuildActivity_Task", LUA_ONLY_FIELDS["pbGuildActivity_Task"], key_field="RewardID"
    ),
    "databin_ActivityTask": Table("pbTask_Base", SERVER_FIELD_SUBSETS["pbTask_Base"], key_field="TaskID"),
    "databin_Activity_Base": Table(
        "pbActivity_Base", SERVER_FIELD_SUBSETS["pbActivity_Base"], key_field="ActivityID"
    ),
    "databin_AvatarItem": Table("pbAvatarItem", key_field="AvatarItemId"),
    "databin_AvatarSuit": Table("pbAvatarSuit", key_field="AvatarSuitId"),
    "databin_CreateRoleShowAvatar": Table("pbCreateRoleShowAvatar"),
    "databin_DefaultAvatarItem": Table("pbDefaultAvatarItem"),
    "databin_Drop_Static": Table("pbDrop_Static"),
    "databin_ResIDTypeMatch_Base": Table("pbResIDTypeMatch_Base"),
    "databin_Drop_Base": Table("pbDrop_Base", LUA_ONLY_FIELDS["pbDrop_Base"]),
    "databin_Drop_Pack": Table("pbDrop_Pack", LUA_ONLY_FIELDS["pbDrop_Pack"]),
    "databin_LobbyBg": Table("pbLobbyBg", LUA_ONLY_FIELDS["pbLobbyBg"]),
    "databin_Newbie_RewardHero": Table("pbNewbie_RewardHero"),
    "databin_OutSideItem_Base": Table(
        "pbOutSideItem_Base", key_field="OutSideItemId", kept_fields=OUT_SIDE_ITEM_SERVER_FIELDS
    ),
    "databin_RankedMatchSeason": Table("pbRankedMatchSeason", key_field="SeasonId"),
    "databin_RankedDivision": Table("pbRankedDivision", key_field="Index", kept_fields=RANKED_DIVISION_SERVER_FIELDS),
    "databin_RankedMatchMapOpenTime": Table(
        "pbRankedMatchMapOpenTime", LUA_ONLY_FIELDS["pbRankedMatchMapOpenTime"], key_field="Index"
    ),
    "databin_RankMatchPointMedal": Table(
        "pbRankMatchPointMedal", LUA_ONLY_FIELDS["pbRankMatchPointMedal"], key_field="MedalID"
    ),
    "databin_RankedMatchSeasonAward": Table("pbRankedMatchSeasonAward"),
    "databin_RankedMatchSeasonTaskAward": Table(
        "pbRankedMatchSeasonTaskAward", LUA_ONLY_FIELDS["pbRankedMatchSeasonTaskAward"]
    ),
    "databin_NewPlayerDefaultRes": Table(
        "pbNewPlayerDefaultRes", LUA_ONLY_FIELDS["pbNewPlayerDefaultRes"], key_field="NewplayerRewardId"
    ),
    "databin_EeveeChallenge": Table("pbEeveeChallenge", LUA_ONLY_FIELDS["pbEeveeChallenge"]),
    "databin_UniteChallenge": Table("pbUniteChallenge", LUA_ONLY_FIELDS["pbUniteChallenge"], key_field="ID"),
    "databin_TowerDefenseShop": Table("pbTowerDefenseShop", LUA_ONLY_FIELDS["pbTowerDefenseShop"]),
    "databin_TowerDefenseBuffLevelUpDesign": Table("pbTowerDefenseBuffLevelUpDesign", key_field="Id"),
    "databin_TowerDefensePassAward": Table("pbTowerDefensePassAward", LUA_ONLY_FIELDS["pbTowerDefensePassAward"]),
    "databin_BossRushActivityBase": Table(
        "pbBossRushActivityBase", LUA_ONLY_FIELDS["pbBossRushActivityBase"], key_field="PeriodID"
    ),
    "databin_BossRushGuildActivityScoreAward": Table(
        "pbBossRushGuildActivityScoreAward", LUA_ONLY_FIELDS["pbBossRushGuildActivityScoreAward"]
    ),
    "databin_MegaEvolutionOther": Table("pbMegaEvolutionOther", LUA_ONLY_FIELDS["pbMegaEvolutionOther"]),
    "databin_MegaEvolutionReward": Table("pbMegaEvolutionReward", LUA_ONLY_FIELDS["pbMegaEvolutionReward"]),
    "databin_MegaEvolutionTaskPool": Table(
        "pbMegaEvolutionTaskPool", LUA_ONLY_FIELDS["pbMegaEvolutionTaskPool"], key_field="Index"
    ),
    "databin_BanPickRule": Table("pbBanPickRule", LUA_ONLY_FIELDS["pbBanPickRule"], key_field="BanPickRuleId"),
    "databin_RoundRule": Table("pbRoundRule", LUA_ONLY_FIELDS["pbRoundRule"], key_field="RoundRuleId"),
    "databin_FriendlyRoomRule": Table("pbFriendlyRoomRule", LUA_ONLY_FIELDS["pbFriendlyRoomRule"], key_field="Id"),
    "databin_RoleImage": Table("pbRoleImage"),
    "databin_Scene_OpenTime": Table("pbScene_OpenTime", LUA_ONLY_FIELDS["pbScene_OpenTime"]),
    "databin_Scene_Map": Table("pbScene_Map", key_field="SceneId", kept_fields=SCENE_MAP_SERVER_FIELDS),
    "databin_MapBase": Table("pbMapBase", key_field="MapBaseId", kept_fields=MAP_BASE_SERVER_FIELDS),
    "databin_Mvp_Conf": Table("pbMvp_Conf", LUA_ONLY_FIELDS["pbMvp_Conf"]),
    "databin_MatchTrainer": Table("pbMatchTrainer", LUA_ONLY_FIELDS["pbMatchTrainer"]),
    "databin_TrainerLevel": Table("pbTrainerLevel", key_field="Level"),
    "databin_Shop.group.1": Table("pbShop", key_field="ShopID", kept_fields=SHOP_SERVER_FIELDS),
    "databin_StarRoadSite": Table("pbStarRoadSite", LUA_ONLY_FIELDS["pbStarRoadSite"]),
    "databin_StarRoadPokemon": Table("pbStarRoadPokemon", LUA_ONLY_FIELDS["pbStarRoadPokemon"]),
    "databin_NewbieGift": Table("pbNewbieGift", LUA_ONLY_FIELDS["pbNewbieGift"]),
    "databin_NewbieChallengeTask": Table("pbNewbieChallengeTask", LUA_ONLY_FIELDS["pbNewbieChallengeTask"]),
    "databin_OridinaryTask": Table("pbTask_Base", SERVER_FIELD_SUBSETS["pbTask_Base"], key_field="TaskID"),
    "databin_Achievement": Table("pbAchievement", LUA_ONLY_FIELDS["pbAchievement"], key_field="AchievementID"),
    "databin_BattlePassSeason": Table(
        "pbBattlePassSeason", LUA_ONLY_FIELDS["pbBattlePassSeason"], key_field="SeasonID"
    ),
    "databin_BattlePassInfo": Table("pbBattlePassInfo", LUA_ONLY_FIELDS["pbBattlePassInfo"], key_field="SeasonID"),
    "databin_BattlePassLevel": Table("pbBattlePassLevel"),
    "databin_BattlePassLevelReward.group.1": Table("pbBattlePassLevelReward"),
    "databin_BattlePass_SeasonTask": Table("pbBattlePass_SeasonTask", LUA_ONLY_FIELDS["pbBattlePass_SeasonTask"]),
    "databin_BattlePass_TimeLimitTask": Table(
        "pbBattlePass_TimeLimitTask", LUA_ONLY_FIELDS["pbBattlePass_TimeLimitTask"], key_field="ID"
    ),
    "databin_BattlePass_WeekIndex": Table("pbBattlePass_WeekIndex"),
    "databin_BattlePass_WeekTaskGroup": Table("pbBattlePass_WeekTaskGroup", key_field="WeekTaskGroupID"),
    "databin_BattlePassTaskBase": Table("pbTask_Base", SERVER_FIELD_SUBSETS["pbTask_Base"], key_field="TaskID"),
    "databin_Reward_Pool": Table("pbReward_Pool", LUA_ONLY_FIELDS["pbReward_Pool"], key_field="LotteryPoolId"),
    "databin_Canopy_Base": Table("pbCanopy_Base", LUA_ONLY_FIELDS["pbCanopy_Base"], key_field="CanopyID"),
    "databin_CardFace": Table("pbCardFace", LUA_ONLY_FIELDS["pbCardFace"], key_field="CardFaceID"),
    "databin_Contestant_Card_Decoration": Table(
        "pbContestant_Card_Decoration", key_field="DecorationID", kept_fields=CONTESTANT_CARD_DECORATION_SERVER_FIELDS
    ),
    "databin_HeadIcon_Base": Table("pbHeadIcon_Base", key_field="HeadIconID", kept_fields=HEAD_ICON_SERVER_FIELDS),
    "databin_HeadIcon": Table("pbHeadIcon", key_field="HeadIconID"),
    "databin_PersonStyleHeadFrame": Table(
        "pbPersonStyleHeadFrame", LUA_ONLY_FIELDS["pbPersonStyleHeadFrame"], key_field="HeadIconID"
    ),
    "databin_PersonalityLabel": Table("pbPersonalityLabel", LUA_ONLY_FIELDS["pbPersonalityLabel"], key_field="ID"),
    "databin_BattleSignalCommunicate": Table(
        "pbBattleSignalCommunicate", key_field="Id", kept_fields=BATTLE_SIGNAL_COMMUNICATE_SERVER_FIELDS
    ),
    "databin_SystemMenuList": Table("pbSystemMenuList", LUA_ONLY_FIELDS["pbSystemMenuList"], key_field="MenuItem"),
    "databin_NationSelectInformation": Table(
        "pbNationSelectInformation", key_field="NationID", kept_fields=NATION_SELECT_INFORMATION_SERVER_FIELDS
    ),
    "databin_HeldItemLevelUpDesign": Table(
        "pbHeldItemLevelUpDesign", key_field="Id", kept_fields=HELD_ITEM_LEVEL_UP_DESIGN_SERVER_FIELDS
    ),
    "databin_HeldItemSystemConfig": Table(
        "pbHeldItemSystemConfig", LUA_ONLY_FIELDS["pbHeldItemSystemConfig"], key_field="Id"
    ),
    "databin_SystemUnlock": Table("pbSystemUnlock", kept_fields=SYSTEM_UNLOCK_SERVER_FIELDS),
    "databin_HangupScheme": Table("pbHangupScheme"),
    "databin_Pokemon_Base": Table("pbPokemon_Base", SERVER_FIELD_SUBSETS["pbPokemon_Base"], key_field="PokemonId"),
    "databin_Pokemon_Release": Table(
        "pbPokemon_Release", key_field="PokemonId", kept_fields=POKEMON_RELEASE_SERVER_FIELDS
    ),
    "databin_Badge_Base": Table("pbBadge_Base", SERVER_FIELD_SUBSETS["pbBadge_Base"], key_field="BadgeId"),
    "databin_BadgeUnlock": Table("pbBadgeUnlock", LUA_ONLY_FIELDS["pbBadgeUnlock"], key_field="BadgeSchemeID"),
    "databin_IntimacySetting": Table("pbIntimacySetting", LUA_ONLY_FIELDS["pbIntimacySetting"], key_field="ID"),
    "databin_IntimacyRule": Table("pbIntimacyRule", LUA_ONLY_FIELDS["pbIntimacyRule"], key_field="ID"),
    "databin_IntimacyLevel": Table("pbIntimacyLevel", LUA_ONLY_FIELDS["pbIntimacyLevel"], key_field="Level"),
    "databin_BadgeColor": Table("pbBadgeColor", LUA_ONLY_FIELDS["pbBadgeColor"]),
    "databin_NationGroup": Table("pbNationGroup", LUA_ONLY_FIELDS["pbNationGroup"], key_field="NationGroupID"),
    "databin_SubScriptionPeriod": Table("pbSubScriptionPeriod", LUA_ONLY_FIELDS["pbSubScriptionPeriod"], key_field="ID"),
    "databin_TowerDefenseBase": Table(
        "pbTowerDefenseBase", LUA_ONLY_FIELDS["pbTowerDefenseBase"], key_field="PeriodID"
    ),
    "databin_TrainerExpGift": Table("pbTrainerExpGift", LUA_ONLY_FIELDS["pbTrainerExpGift"]),
    "databin_TrainerSeasonExpGift": Table("pbTrainerSeasonExpGift", LUA_ONLY_FIELDS["pbTrainerSeasonExpGift"]),
    "databin_PasterSpray": Table("pbPasterSpray", LUA_ONLY_FIELDS["pbPasterSpray"], key_field="PasterSprayID"),
    "databin_NewbieGuideOrder": Table("pbNewbieGuideOrder", key_field="ID"),
    "databin_NewbieGuideSteps": Table("pbNewbieGuideSteps", kept_fields=NEWBIE_GUIDE_STEPS_SERVER_FIELDS),
    "databin_PveChallengeStageRoute": Table(
        "pbPveChallengeStageRoute", kept_fields=PVE_CHALLENGE_STAGE_ROUTE_SERVER_FIELDS
    ),
    "databin_AiInfoConfig": Table("pbAiInfoConfig", key_field="ID"),
    "databin_TrainerPokemon": Table("pbTrainerPokemon", key_field="PokemonUnitId"),
    "databin_Pokemon_Avatar_Base": Table(
        "pbPokemon_Avatar_Base", key_field="PokemonAvatarId", kept_fields=POKEMON_AVATAR_BASE_SERVER_FIELDS
    ),
    "databin_Pokemon_Equipment": Table(
        "pbPokemon_Equipment", key_field="EquipmentId", kept_fields=POKEMON_EQUIPMENT_SERVER_FIELDS
    ),
    "databin_SkillEffectExpandPack_Base": Table(
        "pbSkillEffectExpandPack_Base",
        key_field="SkillEffectsExpandPackID",
        kept_fields=SKILL_EFFECT_EXPAND_PACK_BASE_SERVER_FIELDS,
    ),
    "databin_Pokemon_Hero": Table("pbUnit_Base", SERVER_FIELD_SUBSETS["pbUnit_Base"], key_field="UnitId"),
    "databin_Monster_Base_Monster": Table("pbUnit_Base", SERVER_FIELD_SUBSETS["pbUnit_Base"], key_field="UnitId"),
    "databin_Battle_Global_Base": Table("pbGlobal_Base", key_field="GlobalId"),
    "databin_Outside_Global_Base": Table("pbGlobal_Base", key_field="GlobalId"),
}

TABLE_MAGIC = b"pbc"
SUPPORTED_TABLE_VERSIONS = (1, 2)
INDEXED_TABLE_VERSION = 2
TABLE_HEADER = struct.Struct("<3sBII")
ROW_INDEX_ENTRY = struct.Struct("<QI")
UINT32 = struct.Struct("<I")
SECONDARY_INDEX_VALUE = struct.Struct("<iI")
ROW_KEY = struct.Struct("<Q")

WIRE_VARINT = 0
WIRE_FIXED64 = 1
WIRE_LENGTH_DELIMITED = 2
WIRE_FIXED32 = 5
MAX_VARINT_SHIFT = 63

VARINT_TYPES = {"int32", "int64", "uint32", "uint64", "bool"}
FIXED_WIDTH_TYPES = {
    "fixed64": struct.Struct("<Q"),
    "sfixed64": struct.Struct("<q"),
    "double": struct.Struct("<d"),
    "fixed32": struct.Struct("<I"),
    "sfixed32": struct.Struct("<i"),
    "float": struct.Struct("<f"),
}
WIRE_TYPE_OF_FIXED_WIDTH = {struct.calcsize("<Q"): WIRE_FIXED64, struct.calcsize("<I"): WIRE_FIXED32}
SIGNED_VARINT_TYPES = {"int32", "int64"}
JSON_STRING_TYPES = {"int64", "uint64", "fixed64", "sfixed64"}
UINT64_RANGE = 1 << 64
INT32_RANGE = range(-(1 << 31), 1 << 31)

PROTO_MESSAGE = re.compile(r"^message (\w+) \{\n(.*?)^\}", re.MULTILINE | re.DOTALL)
PROTO_ENUM = re.compile(r"^enum (\w+) \{", re.MULTILINE)
PROTO_FIELD = re.compile(r"^\s*(repeated\s+)?([\w.]+)\s+(\w+)\s*=\s*(\d+);", re.MULTILINE)


class ConversionError(Exception):
    pass


class ByteReader:
    def __init__(self, data, offset=0, end=None):
        self.data = data
        self.offset = offset
        self.end = len(data) if end is None else end

    @property
    def at_end(self):
        return self.offset >= self.end

    def unpack(self, layout, what):
        if self.offset + layout.size > self.end:
            raise ConversionError(f"{what} runs past the end")
        values = layout.unpack_from(self.data, self.offset)
        self.offset += layout.size
        return values

    def take(self, length, what):
        if self.offset + length > self.end:
            raise ConversionError(f"{what} runs past the end")
        chunk = self.data[self.offset : self.offset + length]
        self.offset += length
        return chunk

    def varint(self):
        result = shift = 0
        while True:
            if self.at_end or shift > MAX_VARINT_SHIFT:
                raise ConversionError("truncated varint")
            byte = self.data[self.offset]
            self.offset += 1
            result |= (byte & 0x7F) << shift
            shift += 7
            if byte < 0x80:
                return result


def read_proto_messages(proto_path):
    text = proto_path.read_text(encoding="utf-8")
    enum_names = set(PROTO_ENUM.findall(text))
    return {
        message_name: {
            int(number): Field(name, "int32" if field_type in enum_names else field_type, bool(repeated))
            for repeated, field_type, name, number in PROTO_FIELD.findall(body)
        }
        for message_name, body in PROTO_MESSAGE.findall(text)
    }


def read_row_index(reader):
    magic, version, _, row_count = reader.unpack(TABLE_HEADER, "table header")
    if magic != TABLE_MAGIC:
        raise ConversionError("missing pbc table header")
    if version not in SUPPORTED_TABLE_VERSIONS:
        raise ConversionError(f"table version {version} is not supported")
    row_index = [reader.unpack(ROW_INDEX_ENTRY, "row index") for _ in range(row_count)]
    if len({key for key, _ in row_index}) != row_count:
        raise ConversionError("row index repeats a key")
    return version, row_index


def skip_secondary_indexes(reader, row_keys):
    (index_count,) = reader.unpack(UINT32, "secondary index count")
    for _ in range(index_count):
        (name_length,) = reader.unpack(UINT32, "secondary index name length")
        reader.take(name_length, "secondary index name")
        (value_count,) = reader.unpack(UINT32, "secondary index value count")
        indexed_key_count = 0
        for _ in range(value_count):
            _, key_count = reader.unpack(SECONDARY_INDEX_VALUE, "secondary index value")
            for _ in range(key_count):
                (key,) = reader.unpack(ROW_KEY, "secondary index key")
                if key not in row_keys:
                    raise ConversionError(f"secondary index names key {key}, which has no row")
            indexed_key_count += key_count
        if indexed_key_count != len(row_keys):
            raise ConversionError(
                f"secondary index covers {indexed_key_count} keys, not the table's {len(row_keys)} rows"
            )


def read_rows(table_bytes):
    reader = ByteReader(table_bytes)
    version, row_index = read_row_index(reader)
    if version == INDEXED_TABLE_VERSION:
        skip_secondary_indexes(reader, {key for key, _ in row_index})
    rows_start = reader.offset
    rows_length = len(table_bytes) - rows_start
    row_starts = [start for _, start in row_index]
    row_ends = row_starts[1:] + [rows_length]
    for (key, start), end in zip(row_index, row_ends):
        if not 0 <= start <= end <= rows_length:
            raise ConversionError("row offsets are out of order or out of range")
        yield key, table_bytes[rows_start + start : rows_start + end]


def decode_varint(raw_value, field):
    if field.type == "bool":
        return raw_value != 0
    value = raw_value
    if field.type in SIGNED_VARINT_TYPES and value >= UINT64_RANGE >> 1:
        value -= UINT64_RANGE
    if field.type == "int32" and value not in INT32_RANGE:
        raise ConversionError(f"value {value} does not fit int32")
    return value


def decode_packed(payload, field):
    if field.type in VARINT_TYPES:
        reader = ByteReader(payload)
        values = []
        while not reader.at_end:
            values.append(decode_varint(reader.varint(), field))
        return values
    layout = FIXED_WIDTH_TYPES[field.type]
    if len(payload) % layout.size:
        raise ConversionError(f"{field.name} packed payload is truncated")
    return [value for (value,) in layout.iter_unpack(payload)]


def decode_length_delimited(payload, field):
    if field.type in NESTED_MESSAGE_FIELDS:
        return [without_trailing_zeros(convert_row(payload, NESTED_MESSAGE_FIELDS[field.type], drops_unnamed_fields=True))]
    if field.type == "string":
        try:
            return [payload.decode("utf-8")]
        except UnicodeDecodeError as error:
            raise ConversionError(f"{field.name} is not valid UTF-8") from error
    if field.type == "bytes":
        return [base64.b64encode(payload).decode("ascii")]
    if field.repeated and (field.type in VARINT_TYPES or field.type in FIXED_WIDTH_TYPES):
        return decode_packed(payload, field)
    raise ConversionError(f"{field.name} has type {field.type}, which this converter cannot write")


def without_trailing_zeros(row):
    sparse = {}
    for name, value in row.items():
        if isinstance(value, list):
            while value and value[-1] == 0:
                value = value[:-1]
            if not value:
                continue
        sparse[name] = value
    return sparse


def decode_field_values(reader, wire_type, field):
    if wire_type == WIRE_VARINT and field.type in VARINT_TYPES:
        return [decode_varint(reader.varint(), field)]
    layout = FIXED_WIDTH_TYPES.get(field.type)
    if layout is not None and wire_type == WIRE_TYPE_OF_FIXED_WIDTH[layout.size]:
        return list(reader.unpack(layout, field.name))
    if wire_type == WIRE_LENGTH_DELIMITED:
        return decode_length_delimited(reader.take(reader.varint(), field.name), field)
    raise ConversionError(f"{field.name} has wire type {wire_type}, which does not match its type {field.type}")


def skip_field_value(reader, wire_type):
    if wire_type == WIRE_VARINT:
        reader.varint()
    elif wire_type == WIRE_FIXED64:
        reader.offset += 8
    elif wire_type == WIRE_FIXED32:
        reader.offset += 4
    elif wire_type == WIRE_LENGTH_DELIMITED:
        reader.take(reader.varint(), "skipped field")
    else:
        raise ConversionError(f"unsupported wire type {wire_type}")


def to_json_value(value, field):
    return str(value) if field.type in JSON_STRING_TYPES else value


def convert_row(row_bytes, fields, drops_unnamed_fields):
    reader = ByteReader(row_bytes)
    values = {}
    while not reader.at_end:
        tag = reader.varint()
        number, wire_type = tag >> 3, tag & 7
        field = fields.get(number)
        if field is None:
            if not drops_unnamed_fields:
                raise ConversionError(f"field {number} is not in the message schema")
            skip_field_value(reader, wire_type)
            continue
        decoded = decode_field_values(reader, wire_type, field)
        if field.repeated:
            values.setdefault(number, []).extend(decoded)
        elif number in values:
            raise ConversionError(f"{field.name} appears twice but is not repeated")
        else:
            values[number] = decoded[0]
    if reader.offset != len(row_bytes):
        raise ConversionError("row ends inside a field")
    return {fields[number].name: to_json_values(value, fields[number]) for number, value in sorted(values.items())}


def to_json_values(value, field):
    if field.repeated:
        return [to_json_value(item, field) for item in value]
    return to_json_value(value, field)


def fields_of(table, proto_messages):
    if table.named_fields is not None:
        return table.named_fields
    if table.message in proto_messages:
        return proto_messages[table.message]
    raise ConversionError(f"{table.message} is not in the proto file")


def check_kept_fields(table, fields):
    field_names = {field.name for field in fields.values()}
    missing = [name for name in table.kept_fields or () if name not in field_names]
    if missing:
        raise ConversionError(f"kept fields are not in {table.message}: {', '.join(missing)}")


def check_row_key(table, row, index_key):
    if table.key_field is not None and str(row.get(table.key_field, 0)) != str(index_key):
        raise ConversionError(f"row with index key {index_key} has {table.key_field} {row.get(table.key_field)}")


def keep_server_fields(table, row):
    if table.kept_fields is None:
        return row
    return {name: value for name, value in row.items() if name in table.kept_fields}


def convert_table(databin_name, table, input_folder, proto_messages):
    fields = fields_of(table, proto_messages)
    table_path = input_folder / f"{databin_name}.bin"
    if not table_path.is_file():
        raise ConversionError(f"{table_path} does not exist; decode {databin_name} first")
    check_kept_fields(table, fields)
    rows = []
    for index_key, row_bytes in read_rows(table_path.read_bytes()):
        row = convert_row(row_bytes, fields, table.drops_unnamed_fields)
        check_row_key(table, row, index_key)
        rows.append(keep_server_fields(table, row))
    if not rows:
        raise ConversionError("table has no rows")
    return rows


def write_rows(output_path, rows):
    with output_path.open("w", encoding="utf-8", newline="\n") as output:
        json.dump(rows, output, ensure_ascii=False, indent=2)
        output.write("\n")


def databins_of(message):
    return [name for name, table in TABLES.items() if table.message == message]


def parse_arguments():
    parser = argparse.ArgumentParser(description="Convert decoded databin tables into the server's game data files.")
    parser.add_argument("names", nargs="*", help="Databin names to convert; all known tables when omitted")
    parser.add_argument("--input", type=Path, required=True, help="Folder holding decoded databin_X.bin tables")
    parser.add_argument("--proto", type=Path, default=DEFAULT_PROTO)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    args.names = args.names or sorted(TABLES)
    unknown = [name for name in args.names if name not in TABLES]
    if unknown:
        parser.error(f"Unknown databin tables: {', '.join(unknown)}; add them to TABLES first")
    missing_parts = sorted(
        {part for name in args.names for part in databins_of(TABLES[name].message) if part not in args.names}
    )
    if missing_parts:
        parser.error(f"Convert {', '.join(missing_parts)} too; they write the same game data file")
    if not args.input.is_dir():
        parser.error(f"Input folder does not exist: {args.input}")
    if not args.proto.is_file():
        parser.error(f"Proto file does not exist: {args.proto}; run tools/generate_protos.py first")
    return args


def main():
    args = parse_arguments()
    proto_messages = read_proto_messages(args.proto)
    args.output.mkdir(parents=True, exist_ok=True)
    failures = 0
    for message in dict.fromkeys(TABLES[name].message for name in args.names):
        rows = []
        try:
            for name in databins_of(message):
                rows.extend(convert_table(name, TABLES[name], args.input, proto_messages))
        except (ConversionError, struct.error) as error:
            print(f"{name}: {error}", file=sys.stderr)
            failures += 1
            continue
        output_path = args.output / f"{message}.json"
        write_rows(output_path, rows)
        print(f"{', '.join(databins_of(message))}: {len(rows)} rows written to {output_path}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
