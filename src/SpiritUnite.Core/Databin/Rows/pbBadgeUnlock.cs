namespace SpiritUnite.Databin;

public sealed record pbBadgeUnlock(
    int BadgeSchemeID,
    int IsCookiesBuy = 0,
    int CookiesBuy = 0,
    int IsPokeCoinsBuy = 0,
    int PokeCoinsBuy = 0,
    int IsCouponsBuy = 0,
    int CouponsBuy = 0);
