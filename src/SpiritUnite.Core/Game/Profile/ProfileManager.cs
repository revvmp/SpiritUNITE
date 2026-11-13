using System.Collections.Concurrent;
using System.Globalization;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Tools;
using SpiritUnite.Game.Credit;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Profile;

public sealed class ProfileManager(
    ProfileRepository repository,
    ProfileStatisticRepository statistics,
    FabulousRepository fabulous,
    GameData gameData,
    PokemonManager pokemon,
    RankedManager ranked,
    CreditManager credit,
    TimeProvider clock)
{
    private const int FlagOff = 0;
    private const int FlagOn = 1;
    private const int HeadIconOpenFlag = 1;
    private const int NoHeadIconID = 0;
    private const int DefaultTrainerImageLensDistance = 2;
    private const int DefaultTrainerImagePosX = 1;
    private const int DefaultTrainerImagePosY = 2;
    private const int DefaultTrainerImageRotationAngle = 1;
    private const int MaxIndividualLabelNum = (int)PB_MACRO_MAX_INDIVIDUAL_LABEL_NUM.PbMaxIndividualLabelNum;
    private const int PkmavatarHeadType = (int)PB_ENUM_ENUM_SOCIAL_HEAD_TYPE.PbEnumSocialHeadTypePkmavatar;

    private readonly ConcurrentDictionary<ulong, ProfileStyle> styles = new();

    public async Task<ProfileStyle> LoadAsync(ulong gameUlongUID)
    {
        if (styles.TryGetValue(gameUlongUID, out var cached))
        {
            return cached;
        }

        return styles.GetOrAdd(gameUlongUID, await repository.LoadAsync(gameUlongUID));
    }

    public void Forget(ulong gameUlongUID) => styles.TryRemove(gameUlongUID, out _);

    public Task SaveAsync(ulong gameUlongUID, ProfileStyle style) => repository.SaveAsync(gameUlongUID, style);

    public Task SaveWearCardFaceAsync(ulong gameUlongUID, ProfileStyle style, int pokemonID) =>
        repository.SaveWearCardFaceAsync(gameUlongUID, style, pokemonID);

    public long CurrentSvrCfgTime => UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());

    public long CurrentServerTime => clock.GetUtcNow().ToUnixTimeSeconds();

    public static bool IsFlag(int value) => value is FlagOff or FlagOn;

    public PB_ENUM_RES_ERROR_CODE EditContestantCard(LMNRoleInfo role, ProfileStyle style, pbCOMDT_PLAYER_CONTESTANT_INFO? info)
    {
        var result = ContestantCard.Validate(gameData, role, info, CurrentSvrCfgTime);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc && info is not null)
        {
            style.PlayerContestantInfo = ContestantCard.ToStoredInfo(info);
        }

        return result;
    }

    public pbSCProfileGainResNtf[] GrantRegistGive(LMNRoleInfo role)
    {
        var registGiveDecorationIDs = ContestantCard.FindRegistGiveDecorationIDs(gameData);
        if (registGiveDecorationIDs.Any(decorationID => role.OwnsItem(RoleItemType.ContestantCardDecoration, decorationID)))
        {
            return [];
        }

        var gainTime = CurrentServerTime;
        role.GrantItem(RoleItemType.LobbyBg, gameData.DefaultLobbyBgId, gainTime);
        var gains = new List<pbSCProfileGainResNtf>();
        GrantDecorations(role, ContestantCard.FindRegistDefaultPokemonEffectIDs(gameData, CurrentSvrCfgTime), gainTime, gains);
        foreach (var decorationID in registGiveDecorationIDs)
        {
            GrantDecorations(role, [decorationID], gainTime, gains);
        }

        return [.. gains];
    }

    private void GrantDecorations(LMNRoleInfo role, int[] decorationIDs, long gainTime, List<pbSCProfileGainResNtf> gains)
    {
        var granted = decorationIDs
            .Select(decorationID => role.GrantItem(RoleItemType.ContestantCardDecoration, decorationID, gainTime))
            .OfType<RoleItemInfo>()
            .Count();
        if (granted > 0)
        {
            gains.Add(new pbSCProfileGainResNtf { ContestantResList = ContestantCard.BuildContestantResList(gameData, role) });
        }
    }

    public int[] FindOwnedDecorationIDs(LMNRoleInfo role, int decorationType) =>
    [
        .. role.GetOwnedItems(RoleItemType.ContestantCardDecoration)
            .Where(item => gameData.ContestantCardDecoration.Find(item.ItemId)?.DecorationType == decorationType)
            .Select(item => item.ItemId),
    ];

    public PB_ENUM_RES_ERROR_CODE SetIndividualLabelInfo(ProfileStyle style, IReadOnlyCollection<int> labelList)
    {
        if (labelList.Count > MaxIndividualLabelNum
            || labelList.Distinct().Count() != labelList.Count
            || !labelList.All(label => gameData.PersonalityLabel.Contains(label)))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        style.LabelList = [.. labelList];
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public PB_ENUM_RES_ERROR_CODE ChangeCurHeadIcon(LMNRoleInfo role, ProfileStyle style, int headIconID)
    {
        var headIcon = gameData.HeadIconBase.Find(headIconID);
        if (headIcon is null || headIcon.IsOpen != HeadIconOpenFlag)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrHeadIconNotInCfg;
        }

        if (!OwnsHeadIcon(role, headIconID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrHeadIconNotExist;
        }

        style.CurHeadIconID = headIconID;
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public PB_ENUM_RES_ERROR_CODE ChangeCurPersonStyleHeadFrame(LMNRoleInfo role, ProfileStyle style, int headFrameID, int headType)
    {
        if (headType == ProfileStyle.DefaultHeadType && headFrameID == ProfileStyle.NoHeadResID)
        {
            style.CurPersonStyleHead = (headType, headFrameID);
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        }

        if (headType != PkmavatarHeadType)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        var headFrame = gameData.PersonStyleHeadFrame.Find(headFrameID);
        if (headFrame is null || headFrame.IsOpen != HeadIconOpenFlag)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrHeadIconNotInCfg;
        }

        if (!IsUsable(role, RoleItemType.PersonStyleHeadFrame, headFrameID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrHeadIconNotExist;
        }

        style.CurPersonStyleHead = (headType, headFrameID);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public PB_ENUM_RES_ERROR_CODE SetCurPersonalCanopy(LMNRoleInfo role, ProfileStyle style, int canopyID)
    {
        var result = CheckCanopy(role, canopyID);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            style.CurCanopyID = canopyID;
        }

        return result;
    }

    public PB_ENUM_RES_ERROR_CODE CheckCanopy(LMNRoleInfo role, int canopyID)
    {
        if (!gameData.CanopyBase.Contains(canopyID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCanopyIdinvalid;
        }

        return IsUsable(role, RoleItemType.Canopy, canopyID)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrCanopyNotExists;
    }

    public static PB_ENUM_RES_ERROR_CODE CancelCurCanopy(ProfileStyle style, int canopyID)
    {
        if (canopyID == ProfileStyle.NoCanopyID || style.CurCanopyID != canopyID)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCanopyNotExists;
        }

        style.CurCanopyID = ProfileStyle.NoCanopyID;
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public PB_ENUM_RES_ERROR_CODE WearCardFace(LMNRoleInfo role, ProfileStyle style, int pokemonID, int cardFaceID)
    {
        if (!role.OwnsPokemon(pokemonID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists;
        }

        if (cardFaceID != ProfileStyle.NoCardFaceID)
        {
            var cardFace = gameData.CardFace.Find(cardFaceID);
            if (cardFace is null || cardFace.PokemonID != pokemonID)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrCardFaceIdinvalid;
            }

            if (!IsUsable(role, RoleItemType.CardFace, cardFaceID))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrCardFaceNotExist;
            }
        }

        style.SetWearCardFaceID(pokemonID, cardFaceID);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<pbCSDT_PROFILE_DETAIL_NEW> BuildProfileDetailAsync(LMNRoleInfo role, ProfileStyle style)
    {
        var pokemonList = await pokemon.BuildGetPokemonListRspAsync(role);
        var usedPokemonList = pokemonList.UsedPokemonList.Select(EmptyChildren.Fill).ToArray();
        var seasonID = RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId;
        var profileStatistics = await statistics.LoadAsync(role.GameUlongUID, seasonID);
        await ranked.LoadAsync(role.GameUlongUID);
        var rankedDivision = ranked.BuildRankedDivision(role.GameUlongUID);
        var rankedHistory = await ranked.BuildRankedHistoryAsync(role.GameUlongUID);
        return new pbCSDT_PROFILE_DETAIL_NEW
        {
            OpenID = role.GameUlongUID.ToString(CultureInfo.InvariantCulture),
            Uid = role.GameUlongUID,
            RoleName = role.InGameUsrName,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            RoleCurExp = role.RoleTotalExp,
            RoleGender = role.Gender,
            RankedDivision = rankedDivision.RankedDivision,
            HighestRankedDivisionInHistory = rankedHistory.HighestRankedDivisionInHistory.RankedDivision,
            FollowInfo = await pokemon.BuildFollowInfoAsync(role),
            PokemonList = { pokemonList.PokemonList },
            UsedPokemonList = { usedPokemonList },
            HeadID = RoleImage.GetHeadID(gameData, role),
            CreditPoint = await credit.GetCreditPointAsync(role.GameUlongUID),
            PlayerContestantInfo = BuildPlayerContestantInfo(role, style),
            UserShortId = RoleProfile.BuildShortId(role),
            BeFabulousCnt = await fabulous.FindBeFabulousCntAsync(role.GameUlongUID),
            RankedMatchSeasonID = seasonID,
            StatisticInfo = profileStatistics.StatisticInfo,
            SeasonStatisticInfo = profileStatistics.SeasonStatisticInfo,
            HeadIconInfo = BuildHeadIconInfo(role, style),
            IsHideInfo = style.IsHideInfo,
            TotalMasterNum = rankedHistory.TotalMasterNum,
            MaxContinuedMasterNum = rankedHistory.MaxContinuedMasterNum,
            LableInfo = style.BuildLableInfo(),
            RoleTotalPoint = usedPokemonList.Sum(info => info.PokemonTotalPoint),
            UserPlantInfo = style.BuildUserPlantInfo(),
        };
    }

    public static pbSCProfileImageInfoRsp BuildProfileImageInfo(LMNRoleInfo role, ProfileStyle style) => new()
    {
        ResultInfo = new pbResultInfo(),
        RoleAvatar = role.RoleAvatarInfo.Clone(),
        TrainerImageInfo = BuildTrainerImageInfo(style),
        ShortId = RoleProfile.BuildShortId(role),
        QueryUid = role.GameUlongUID,
    };

    public pbCOMDT_PLAYER_CONTESTANT_INFO BuildLoginPlayerContestantInfo(LMNRoleInfo role, ProfileStyle style)
    {
        var info = style.PlayerContestantInfo;
        info.ContestantResList = ContestantCard.BuildContestantResList(gameData, role);
        return info;
    }

    public static pbCOMDT_TRAINER_IMAGE_INFO BuildTrainerImageInfo(ProfileStyle style) =>
        style.PlayerContestantInfo.TrainerImageInfo ?? BuildDefaultTrainerImageInfo();

    private static pbCOMDT_TRAINER_IMAGE_INFO BuildDefaultTrainerImageInfo() => new()
    {
        Effect = new pbCOMDT_CONTESTANT_EFFECT_LIST(),
        LensDistance = DefaultTrainerImageLensDistance,
        Position = new pbCOMDT_TRAINER_IMAGE_POSITION { PosX = DefaultTrainerImagePosX, PosY = DefaultTrainerImagePosY },
        PokemonEffect = new pbCOMDT_CONTESTANT_POKEMON_EFFECT_INFO { Position = new pbCOMDT_TRAINER_IMAGE_POSITION() },
        RotationAngle = DefaultTrainerImageRotationAngle,
    };

    public pbCOMDT_HEAD_ICON_INFO BuildHeadIconInfo(LMNRoleInfo role, ProfileStyle style)
    {
        var owned = role.GetOwnedItems(RoleItemType.HeadIcon).Select(item => item.ItemId);
        return new pbCOMDT_HEAD_ICON_INFO
        {
            CurHeadIconInfo = new pbCOMDT_HEAD_ICON_BASE_INFO
            {
                HeadIconID = role.HasRoleAppearance ? style.CurHeadIconID : NoHeadIconID,
            },
            HeadIconDetail =
            {
                (role.HasRoleAppearance ? owned.Prepend(ProfileStyle.DefaultHeadIconID) : owned)
                    .Distinct()
                    .Select(headIconID => new pbCOMDT_HEAD_ICON_BASE_INFO { HeadIconID = headIconID }),
            },
            SubscriptionHeadIconInfo = new pbCOMDT_HEAD_ICON_BASE_INFO(),
        };
    }

    public static pbCOMDT_PLAYER_PERSON_STYLE_HEAD_INFO BuildPersonStyleHeadInfo(LMNRoleInfo role, ProfileStyle style) => new()
    {
        CurIconInfo = new pbCOMDT_PLAYER_PERSON_STYLE_BASE_INFO { ResID = style.CurPersonStyleHead.CurHeadResID },
        IconDetail =
        {
            role.GetOwnedItems(RoleItemType.PersonStyleHeadFrame).Select(item => new pbCOMDT_PLAYER_PERSON_STYLE_BASE_INFO
            {
                ResID = item.ItemId,
                NewTag = item.NewTag,
                Timelimited = item.TimeLimited,
            }),
        },
    };

    public static pbCOMDT_PLAYER_PERSON_STYLE_RESOURCE_INFO BuildPersonStyleInfo(LMNRoleInfo role, ProfileStyle style) => new()
    {
        HeadFrameInfo = BuildPersonStyleHeadInfo(role, style),
    };

    public pbCOMDT_PLAYER_CONTESTANT_INFO BuildPlayerContestantInfo(LMNRoleInfo role, ProfileStyle style)
    {
        var info = style.PlayerContestantInfo;
        info.OnlinePeriod ??= new pbCOMDT_ONLINE_PERIOD();
        info.TrainerImageInfo ??= BuildDefaultTrainerImageInfo();
        info.ContestantResList = ContestantCard.BuildContestantResList(gameData, role);
        info.RoleAvatarInfo = role.RoleAvatarInfo.Clone();
        return info;
    }

    public pbCOMDT_CANOPY_LIST BuildCanopyList(LMNRoleInfo role) => new()
    {
        CanopyList =
        {
            role.GetOwnedItems(RoleItemType.Canopy)
                .Where(item => gameData.CanopyBase.Contains(item.ItemId) && !item.IsExpired(CurrentServerTime))
                .Select(ToCanopyInfo),
        },
    };

    public static pbCOMDT_CANOPY_INFO ToCanopyInfo(RoleItemInfo item) => new()
    {
        CanopyID = item.ItemId,
        NewTag = item.NewTag,
        TimeLimited = item.TimeLimited,
    };

    public int GetCurCanopyID(LMNRoleInfo role, ProfileStyle style)
    {
        var curCanopyID = style.CurCanopyID;
        return curCanopyID != ProfileStyle.NoCanopyID && CheckCanopy(role, curCanopyID) == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ? curCanopyID
            : ProfileStyle.NoCanopyID;
    }

    public pbCOMDT_CARD_FACE_LIST BuildCardFaceList(LMNRoleInfo role) => new()
    {
        CardFaceList =
        {
            role.GetOwnedItems(RoleItemType.CardFace)
                .Where(item => gameData.CardFace.Contains(item.ItemId) && !item.IsExpired(CurrentServerTime))
                .Select(item => new pbCOMDT_ONE_CARD_FACE { CardFaceID = item.ItemId, NewTag = item.NewTag }),
        },
    };

    private bool OwnsHeadIcon(LMNRoleInfo role, int headIconID) =>
        headIconID == ProfileStyle.DefaultHeadIconID || IsUsable(role, RoleItemType.HeadIcon, headIconID);

    private bool IsUsable(LMNRoleInfo role, RoleItemType itemType, int itemId) =>
        role.GetOwnedItems(itemType).Any(item => item.ItemId == itemId && !item.IsExpired(CurrentServerTime));
}
