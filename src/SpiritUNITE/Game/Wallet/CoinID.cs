using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Wallet;

public static class CoinID
{
    public const int Cookies = 1000000001;
    public const int PokeCoins = 1000000002;
    public const int Coupons = 1000000003;
    public const int UNIVERSAL_SHARDS_TOKEN_ID = 1000000500;

    public static IReadOnlyList<int> WalletCoins { get; } = [Cookies, PokeCoins, Coupons];

    private static readonly int[] AttributeResIDsTrackedOutsideWallet =
    [
        (int)PB_ENUM_ENM_RESOURCES_ID.PbEnmResourcesIdAcntExp,
        (int)PB_ENUM_ENM_RESOURCES_ID.PbEnmResourcesIdBpExp,
    ];

    private static pbResIDTypeMatch_Base[]? resIDTypeMatch;

    public static void UseResIDTypeMatch(IEnumerable<pbResIDTypeMatch_Base> rows) => resIDTypeMatch = [.. rows];

    public static PB_ENUM_RES_RESOURCES_TYPE GetResIDType(int resID)
    {
        var rows = resIDTypeMatch ?? throw new InvalidOperationException("CoinID.UseResIDTypeMatch was not called at startup");
        return rows.FirstOrDefault(row => resID >= row.ResIDMin && resID <= row.ResIDMax) is { } row
            ? (PB_ENUM_RES_RESOURCES_TYPE)row.ResType
            : PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesMin;
    }

    public static bool IsSpecialCoin(int coinID) =>
        GetResIDType(coinID) == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr
        && !AttributeResIDsTrackedOutsideWallet.Contains(coinID);

    public static bool IsGrantableCoin(int coinID) => WalletCoins.Contains(coinID) || IsSpecialCoin(coinID);

    public static PB_ENUM_RES_RESOURCES_TYPE GetCoinResourceType(int coinID) => IsSpecialCoin(coinID)
        ? PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr
        : PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin;

    public static int? GetCoinID(PB_ENUM_RES_COIN_TYPE coinType) => coinType switch
    {
        PB_ENUM_RES_COIN_TYPE.PbResCoinTypeCookies => Cookies,
        PB_ENUM_RES_COIN_TYPE.PbResCoinTypePokecoins => PokeCoins,
        PB_ENUM_RES_COIN_TYPE.PbResCoinTypeCoupons => Coupons,
        _ => null,
    };
}
