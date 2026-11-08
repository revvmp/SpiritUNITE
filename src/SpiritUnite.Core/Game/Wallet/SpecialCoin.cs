using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Wallet;

public static class SpecialCoin
{
    public static pbCOMDT_SPECIAL_COIN_DETAIL BuildSpecialCoinDetail(int specialCoinID, long specialCoinNum) => new()
    {
        SpecialCoinID = specialCoinID,
        SpecialCoinNum = RoleWallet.ToClientCoinVal(specialCoinNum),
        CoinReceiveInfo = new pbCOMDT_SPECIAL_COIN_RECEIVE_DETAIL(),
    };

    public static pbSCSpecialCoinChangeNtf BuildSpecialCoinChangeNtf(int specialCoinID, long beforeCoinNum, long specialCoinNum) => new()
    {
        BeforeCoinNum = RoleWallet.ToClientCoinVal(beforeCoinNum),
        CoinInfo = BuildSpecialCoinDetail(specialCoinID, specialCoinNum),
    };
}
