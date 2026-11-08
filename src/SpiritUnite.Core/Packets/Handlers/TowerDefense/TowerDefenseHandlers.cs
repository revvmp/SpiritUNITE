using SpiritUnite.Game.Role;
using SpiritUnite.Game.TowerDefense;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.TowerDefense;

public sealed class TowerDefenseHandlers(TowerDefenseManager towerDefense, RoleInfoStore roles)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryTowerDefenseReq)]
    public async Task<GamePacket[]> OnQueryTowerDefenseReq(GamePacket request)
    {
        pbCSQueryTowerDefenseReq.Parser.ParseFrom(request.PkgData);
        var response = await towerDefense.BuildQueryRspAsync(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQueryTowerDefenseRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidTowerDefenseShopBuyReq)]
    public async Task<GamePacket[]> OnTowerDefenseShopBuyReq(GamePacket request)
    {
        var buy = pbCSTowerDefenseShopBuyReq.Parser.ParseFrom(request.PkgData);
        var outcome = await towerDefense.BuyAsync(roles.Require(request.Uid), buy.ShopId, buy.ShopCnt);
        var response = new pbSCTowerDefenseShopBuyRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            ShopId = buy.ShopId,
        };
        return
        [
            .. outcome.BeforeRsp.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidTowerDefenseShopBuyRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidTowerDefenseBuffLevelUpReq)]
    public async Task<GamePacket[]> OnTowerDefenseBuffLevelUpReq(GamePacket request)
    {
        var buffID = pbCSTowerDefenseBuffLevelUpReq.Parser.ParseFrom(request.PkgData).BuffID;
        var result = await towerDefense.LevelUpBuffAsync(request.Uid, buffID);
        var response = new pbSCTowerDefenseBuffLevelUpRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            BuffID = buffID,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidTowerDefenseBuffLevelUpRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidTowerDefenseBuffClearNewTagReq)]
    public async Task<GamePacket[]> OnTowerDefenseBuffClearNewTagReq(GamePacket request)
    {
        pbCSTowerDefenseBuffClearNewTagReq.Parser.ParseFrom(request.PkgData);
        await towerDefense.ClearBuffNewTagAsync(request.Uid);
        var response = new pbSCTowerDefenseBuffClearNewTagRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidTowerDefenseBuffClearNewTagRsp, response)];
    }
}
