namespace SpiritUNITE.Game.Wallet;

public sealed class WalletOptions
{
    public const string SectionName = "Wallet";

    public int StartingCookies { get; init; }
    public int StartingPokeCoins { get; init; }
    public int StartingCoupons { get; init; }

    public bool IsValid() =>
        StartingCookies >= 0
        && StartingPokeCoins >= 0
        && StartingCoupons >= 0;

    public IReadOnlyDictionary<int, long> StartingBalances() => new Dictionary<int, long>
    {
        [CoinID.Cookies] = StartingCookies,
        [CoinID.PokeCoins] = StartingPokeCoins,
        [CoinID.Coupons] = StartingCoupons,
    };
}
