using SpiritUnite.Databin;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Profile;

public static class ContestantCard
{
    public const int EffectType = 1;
    public const int BackGroundType = 2;
    public const int FrameType = 3;
    public const int ExpressionType = 4;
    public const int ActionType = 5;
    public const int PokemonEffectType = 6;

    private const int NoResId = 0;
    private const int NoPokemonId = 0;
    private const int DefaultDecorationFlag = 1;
    private const int MaxBestLine = 3;
    private const int MaxOnlineOption = 3;
    private const int MaxEffectResNum =
        (int)PB_MACRO_COM_MAX_CONTESTANT_EFFECT_RES_NUM.PbComMaxContestantEffectResNum;

    public static int[] FindRegistDefaultPokemonEffectIDs(GameData gameData, long svrCfgTime)
    {
        var registGiveDecorationIDs = FindRegistGiveDecorationIDs(gameData);
        return
        [
            .. gameData.ContestantCardDecoration.Rows
                .Where(decoration => decoration.DecorationType == PokemonEffectType
                    && IsReleasedDefault(decoration, svrCfgTime)
                    && !registGiveDecorationIDs.Contains(decoration.DecorationID))
                .Select(decoration => decoration.DecorationID),
        ];
    }

    public static int[] FindRegistGiveDecorationIDs(GameData gameData) =>
        NewPlayerDefaultGrants.ResourceIdsOf(gameData, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesContestantCardRecoration);

    public static bool IsDecorationType(int decorationType) => decorationType is >= EffectType and <= PokemonEffectType;

    public static bool IsOwned(GameData gameData, LMNRoleInfo role, int decorationID, int decorationType, long svrCfgTime)
    {
        var decoration = gameData.ContestantCardDecoration.Find(decorationID);
        if (decoration is null || decoration.DecorationType != decorationType)
        {
            return false;
        }

        return IsReleasedDefault(decoration, svrCfgTime)
            || role.OwnsItem(RoleItemType.ContestantCardDecoration, decorationID);
    }

    private static bool IsReleasedDefault(pbContestant_Card_Decoration decoration, long svrCfgTime) =>
        decoration.IsDefault == DefaultDecorationFlag
        && decoration.OnTime != 0
        && svrCfgTime >= decoration.OnTime;

    public static pbCOMDT_PLAYER_CONTESTANT_RES_LIST BuildContestantResList(GameData gameData, LMNRoleInfo role)
    {
        var registGiveDecorationIDs = FindRegistGiveDecorationIDs(gameData);
        return BuildContestantResList(role, registGiveDecorationIDs);
    }

    private static pbCOMDT_PLAYER_CONTESTANT_RES_LIST BuildContestantResList(LMNRoleInfo role, int[] registGiveDecorationIDs) => new()
    {
        ResInfoList =
        {
            role.GetOwnedItems(RoleItemType.ContestantCardDecoration)
                .OrderBy(item => item.GainTime)
                .ThenBy(item => registGiveDecorationIDs.Contains(item.ItemId))
                .ThenBy(item => item.ItemId)
                .Select(item => new pbCOMDT_CONTESTANT_RES_INFO
            {
                ResID = item.ItemId,
                NewTag = item.NewTag,
            }),
        },
    };

    public static PB_ENUM_RES_ERROR_CODE Validate(
        GameData gameData,
        LMNRoleInfo role,
        pbCOMDT_PLAYER_CONTESTANT_INFO? info,
        long svrCfgTime)
    {
        if (info is null
            || info.BestLine1 is < 0 or > MaxBestLine
            || info.BestLine2 is < 0 or > MaxBestLine
            || info.OnlinePeriod?.OnlineDayType is < 0 or > MaxOnlineOption
            || info.OnlinePeriod?.OnlineTimeRange is < 0 or > MaxOnlineOption)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        var image = info.TrainerImageInfo ?? new pbCOMDT_TRAINER_IMAGE_INFO();
        var effectResult = ValidateEffects(gameData, role, image.Effect?.EffectInfoList ?? [], svrCfgTime);
        if (effectResult != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return effectResult;
        }

        if (image.PokemonId != NoPokemonId && !role.OwnsPokemon(image.PokemonId))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrContestantResPokemonNotExist;
        }

        (int DecorationID, int DecorationType, PB_ENUM_RES_ERROR_CODE Refusal)[] wornDecorations =
        [
            (image.BackGround, BackGroundType, PB_ENUM_RES_ERROR_CODE.PbErrContestantResBackgroundNotExist),
            (image.Frame, FrameType, PB_ENUM_RES_ERROR_CODE.PbErrContestantResFrameNotExist),
            (image.Expression, ExpressionType, PB_ENUM_RES_ERROR_CODE.PbErrContestantResExpressionNotExist),
            (image.Action, ActionType, PB_ENUM_RES_ERROR_CODE.PbErrContestantResActionNotExist),
            (image.PokemonEffect?.ResId ?? NoResId, PokemonEffectType, PB_ENUM_RES_ERROR_CODE.PbErrContestantResPokemonEffectNotExist),
        ];
        return wornDecorations
            .Where(worn => !IsWearable(gameData, role, worn.DecorationID, worn.DecorationType, svrCfgTime))
            .Select(worn => worn.Refusal)
            .DefaultIfEmpty(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            .First();
    }

    public static pbCOMDT_PLAYER_CONTESTANT_INFO ToStoredInfo(pbCOMDT_PLAYER_CONTESTANT_INFO info) => new()
    {
        BestLine1 = info.BestLine1,
        BestLine2 = info.BestLine2,
        OnlinePeriod = info.OnlinePeriod?.Clone() ?? new pbCOMDT_ONLINE_PERIOD(),
        TrainerImageInfo = info.TrainerImageInfo?.Clone() ?? new pbCOMDT_TRAINER_IMAGE_INFO(),
    };

    private static PB_ENUM_RES_ERROR_CODE ValidateEffects(
        GameData gameData,
        LMNRoleInfo role,
        IReadOnlyCollection<pbCOMDT_CONTESTANT_EFFECT_INFO> effects,
        long svrCfgTime)
    {
        if (effects.Count > MaxEffectResNum)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrContestantResReachLimitThisType;
        }

        if (effects.Select(effect => effect.ResId).Distinct().Count() != effects.Count)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrContestantResDuplicate;
        }

        return effects.All(effect => effect.ResId != NoResId
            && IsOwned(gameData, role, effect.ResId, EffectType, svrCfgTime))
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrContestantResEffectNotExist;
    }

    private static bool IsWearable(GameData gameData, LMNRoleInfo role, int decorationID, int decorationType, long svrCfgTime) =>
        decorationID == NoResId || IsOwned(gameData, role, decorationID, decorationType, svrCfgTime);
}
