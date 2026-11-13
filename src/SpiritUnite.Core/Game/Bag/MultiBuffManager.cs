using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Bag;

public sealed class MultiBuffManager(GameData gameData, BagManager bag, MultiBuffRepository repository, TimeProvider clock)
{
    private const int MultiBuffTypeParamIndex = 0;
    private const int MultipleValueParamIndex = 1;
    private const int MultiBuffSubTypeParamIndex = 2;
    private const int DurationParamIndex = 3;
    private const int SingleItem = 1;
    private const int NoMultiplier = 1;

    public bool IsMultiBuffCard(int resID) =>
        gameData.OutSideItemBase.Find(resID)?.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeMultibuff;

    public async Task<MultiBuffUse> UseMultiBuffCardAsync(ulong gameUlongUID, int resID, ulong gridUniqueID, int resCnt)
    {
        var (validation, config) = await bag.ValidateUseAsync(gameUlongUID, resID, gridUniqueID, resCnt);
        if (config is null)
        {
            return MultiBuffUse.Refused(validation);
        }

        if (resCnt < SingleItem)
        {
            return MultiBuffUse.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemInvalidNum);
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();

        var card = ReadCard(config);
        if (card.MultiBuffType != PB_ENUM_RES_MULTIBUFF_TYPE.PbResMultibuffTypeAcntexp
            && card.MultiBuffType != PB_ENUM_RES_MULTIBUFF_TYPE.PbResMultibuffTypeCoin)
        {
            return MultiBuffUse.Refused(PB_ENUM_RES_ERROR_CODE.PbErrMultiBuffOpTypeError);
        }

        if (card.MultiBuffType == PB_ENUM_RES_MULTIBUFF_TYPE.PbResMultibuffTypeCoin)
        {
            return MultiBuffUse.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemExpired);
        }

        if (card.MultiBuffSubType != PB_ENUM_RES_MULTIBUFF_SUBTYPE.PbResMultibuffSubtypeTime)
        {
            return MultiBuffUse.Refused(PB_ENUM_RES_ERROR_CODE.PbErrMultiBuffUnSupportMultiBuffSubType);
        }

        if (card.MultipleValue <= NoMultiplier || card.Days <= 0)
        {
            return MultiBuffUse.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemIdnotExistInCfg);
        }

        return await repository.UseCardAsync(gameUlongUID, new BagCost(RoleItemType.OutSideItem, resID, resCnt), card, now);
    }

    public async Task<pbCOMDT_MULTIBUFF_INFO> BuildMultiBuffInfoAsync(ulong gameUlongUID, long now) =>
        BuildMultiBuffInfo(await repository.LoadAsync(gameUlongUID), now);

    public static pbCOMDT_MULTIBUFF_INFO BuildMultiBuffInfo(IEnumerable<MultiBuff> buffs, long now) => new()
    {
        MultiBuffInfoDetail = { buffs.Where(buff => buff.IsActiveAt(now)).Select(buff => buff.ToOneMultiBuffInfo()) },
    };

    public async Task<MultiBuff?> FindActiveAsync(ulong gameUlongUID, PB_ENUM_RES_MULTIBUFF_TYPE multiBuffType, long now) =>
        (await repository.LoadAsync(gameUlongUID)).FirstOrDefault(buff => buff.MultiBuffType == multiBuffType && buff.IsActiveAt(now));

    private static MultiBuffCard ReadCard(pbOutSideItem_Base config) => new(
        config.OutSideItemId,
        (PB_ENUM_RES_MULTIBUFF_TYPE)config.ItemParam.ElementAtOrDefault(MultiBuffTypeParamIndex),
        config.ItemParam.ElementAtOrDefault(MultipleValueParamIndex),
        (PB_ENUM_RES_MULTIBUFF_SUBTYPE)config.ItemParam.ElementAtOrDefault(MultiBuffSubTypeParamIndex),
        config.ItemParam.ElementAtOrDefault(DurationParamIndex));
}
