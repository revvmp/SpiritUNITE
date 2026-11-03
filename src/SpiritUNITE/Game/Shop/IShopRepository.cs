using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Shop;

public interface IShopRepository
{
    Task<IReadOnlyList<ShopBuyRecord>> GetBuyRecordsAsync(ulong gameUlongUID);

    Task<(PB_ENUM_RES_ERROR_CODE Result, RoleWallet? Wallet)> CommitPurchaseAsync(ulong gameUlongUID, ShopPurchase purchase);
}
