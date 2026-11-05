using System.Collections.Frozen;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Wallet;

public sealed class RoleWallet(IReadOnlyList<KeyValuePair<int, long>> balancesInGrantOrder)
{
    private readonly FrozenDictionary<int, long> balances = balancesInGrantOrder.ToFrozenDictionary();

    public long GetCoinVal(int coinID) => balances.GetValueOrDefault(coinID);

    public pbCOMDT_COIN_INFO BuildCoinInfo() => new()
    {
        CookiesVal = ToClientCoinVal(GetCoinVal(CoinID.Cookies)),
        PokeCoinVal = ToClientCoinVal(GetCoinVal(CoinID.PokeCoins)),
        CommonCoinInfo =
        {
            balances
                .Where(balance => !CoinID.WalletCoins.Contains(balance.Key) && !CoinID.IsSpecialCoin(balance.Key))
                .OrderBy(balance => balance.Key)
                .Select(balance => new pbCOMDT_COMMON_COIN_INFO
                {
                    CommonCoinResId = balance.Key,
                    CommonCoinVal = ToClientCoinVal(balance.Value),
                }),
        },
    };

    public pbCOMDT_SPECIAL_COIN_INFO BuildSpecialCoinInfo() => new()
    {
        CoinInfo =
        {
            balancesInGrantOrder
                .Where(balance => CoinID.IsSpecialCoin(balance.Key) && balance.Value > 0)
                .Select(balance => SpecialCoin.BuildSpecialCoinDetail(balance.Key, balance.Value)),
        },
    };

    public pbSCAcntCouponsRsp BuildAcntCouponsRsp() => new() { Coupons = GetCoinVal(CoinID.Coupons) };

    public int AcntRealCoupous => ToClientCoinVal(GetCoinVal(CoinID.Coupons));

    internal static int ToClientCoinVal(long coinVal) => (int)Math.Clamp(coinVal, 0, int.MaxValue);
}
