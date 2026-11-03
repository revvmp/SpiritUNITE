using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Wallet;

public static class CoinID
{
    public const int Cookies = 1000000001;
    public const int PokeCoins = 1000000002;
    public const int Coupons = 1000000003;

    public static IReadOnlyList<int> WalletCoins { get; } = [Cookies, PokeCoins, Coupons];

    public static int? GetCoinID(PB_ENUM_RES_COIN_TYPE coinType) => coinType switch
    {
        PB_ENUM_RES_COIN_TYPE.PbResCoinTypeCookies => Cookies,
        PB_ENUM_RES_COIN_TYPE.PbResCoinTypePokecoins => PokeCoins,
        PB_ENUM_RES_COIN_TYPE.PbResCoinTypeCoupons => Coupons,
        _ => null,
    };
}
