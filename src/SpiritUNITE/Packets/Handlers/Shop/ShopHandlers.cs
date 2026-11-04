using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Shop;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Shop;

public sealed class ShopHandlers(
    RoleInfoStore roles,
    ShopManager shop,
    GameData gameData,
    ILogger<ShopHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidShopBuyReq)]
    public async Task<GamePacket[]> OnShopBuyReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var shopBuyInfo = pbCSShopBuyReq.Parser.ParseFrom(request.PkgData).ShopBuyInfo ?? new pbCOMDT_SHOP_BUY_INFO();
        var outcome = await shop.BuyAsync(role, shopBuyInfo);
        var shopIDs = string.Join(',', shopBuyInfo.BuyShopList.Select(shopInfo => shopInfo.ShopID));
        var response = new pbSCShopBuyRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            ShopBuyInfo = shopBuyInfo,
        };
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidShopBuyRsp, response);

        if (outcome is not { Purchase: { } purchase, Wallet: { } wallet, BuyRecords: { } buyRecords })
        {
            logger.LogInformation(
                "Shop buy refused for uid {Uid}: {Result} for shop ids {ShopIDs}",
                role.GameUlongUID,
                outcome.Result,
                shopIDs);
            return [reply];
        }

        logger.LogInformation(
            "Shop buy for uid {Uid}: shop ids {ShopIDs}, cost {Costs}, {ItemCount} items, Pokemon {PokemonIds}",
            role.GameUlongUID,
            shopIDs,
            string.Join(',', purchase.CoinCosts.Select(cost => $"{cost.Key}x{cost.Value}")),
            purchase.Items.Count,
            string.Join(',', purchase.PokemonIds));
        return [.. BuildPurchaseUpdates(request, role, purchase, wallet, buyRecords), reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidShopGetPriceReq)]
    public GamePacket[] OnShopGetPriceReq(GamePacket request)
    {
        var shopList = pbCSShopGetPriceReq.Parser.ParseFrom(request.PkgData).ShopList;
        var response = new pbSCShopGetPriceRsp
        {
            ResultInfo = new pbResultInfo(),
            ShopPriceInfo = shop.GetShopPrice(shopList),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidShopGetPriceRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidShopGetNewSaleInfoReq)]
    public GamePacket[] OnShopGetNewSaleInfoReq(GamePacket request)
    {
        pbCSShopGetNewSaleInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCShopGetNewSaleInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            ShopNewSaleInfo = new pbCOMDT_SHOP_NEW_SALE_INFO_LIST(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidShopGetNewSaleInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidShopNewSaleClickReq)]
    public GamePacket[] OnShopNewSaleClickReq(GamePacket request)
    {
        pbCSShopNewSaleClickReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCShopNewSaleClickRsp
        {
            ResultInfo = new pbResultInfo(),
            ShopNewSaleInfo = new pbCOMDT_SHOP_NEW_SALE_INFO_LIST(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidShopNewSaleClickRsp, response)];
    }

    private IEnumerable<GamePacket> BuildPurchaseUpdates(
        GamePacket request,
        LMNRoleInfo role,
        ShopPurchase purchase,
        RoleWallet wallet,
        IReadOnlyList<ShopBuyRecord> buyRecords)
    {
        foreach (var item in purchase.Items.Where(item => item.ItemType == RoleItemType.AvatarItem))
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateAvatarNtf, new pbSCUpdateAvatarNtf
            {
                UpdateType = (int)PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeGain,
                AvatarInfo = item.ToAvatarInfo(),
            });
        }

        if (purchase.Items.Any(item => item.ItemType == RoleItemType.LobbyBg))
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetLobbyBackgroundInfoRsp, new pbSCGetLobbyBackgroundInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                LobbyBackgroundInfo = role.BuildLobbyBackgroundInfo(gameData),
            });
        }

        foreach (var pokemonId in purchase.PokemonIds)
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = LMNRoleInfo.BuildPokemonInfo(pokemonId),
            });
        }

        yield return request.Reply(
            PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf,
            new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() });

        if (purchase.CoinCosts.ContainsKey(CoinID.Coupons))
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp());
        }

        if (purchase.BuyRecords.Count > 0)
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSpecialSaleInfoNtf, new pbSCSpecialSaleInfoNtf
            {
                SpecialSaleInfo = ShopBuyRecord.BuildSpecialSaleInfo(buyRecords),
            });
        }

        if (purchase.Items.Count > 0 || purchase.PokemonIds.Count > 0)
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, purchase.BuildResourcesAwardNotify());
        }
    }
}
