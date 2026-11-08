using SpiritUNITE.Databin;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Shop;

public sealed record MysteryShopDiscountOutcome(PB_ENUM_RES_ERROR_CODE Result, int Discount);

public sealed class MysteryShopManager(GameData gameData, ActivitySchedule schedule, SettingsManager settings, TimeProvider clock)
{
    private const int NoDiscount = 0;

    public async Task<MysteryShopDiscountOutcome> GetDiscountAsync(ulong gameUlongUID)
    {
        if (schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeMysteryShop, clock.GetUtcNow()) is not { } open)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrMysteryShopActivityNotExit, NoDiscount);
        }

        var countryCode = (await settings.LoadAsync(gameUlongUID)).CountryCode;
        var areaID = gameData.NationSelectInformation.Find(countryCode)?.ArpRegionID ?? pbMysteryShopDiscount.NoArea;
        return gameData.MysteryShopDiscount.Find(pbMysteryShopDiscount.Key(areaID, open.Row.ActivityID)) is { } discount
            ? new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, discount.Discount)
            : new(PB_ENUM_RES_ERROR_CODE.PbErrMysteryShopDataAbnormal, NoDiscount);
    }
}
