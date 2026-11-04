using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Wallet;

public sealed class CoinHandlers(RoleInfoStore roles, WalletManager wallets, LobbyNotices notices)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetCoinValReq)]
    public async Task<GamePacket[]> OnGetCoinValReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetCoinValReq.Parser.ParseFrom(request.PkgData);
        var wallet = await wallets.LoadAsync(role.GameUlongUID);
        var response = new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcntCouponsReq)]
    public async Task<GamePacket[]> OnAcntCouponsReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSAcntCouponsReq.Parser.ParseFrom(request.PkgData);
        var wallet = await wallets.LoadAsync(role.GameUlongUID);
        return [.. notices.BuildCouponsRefreshPackets(wallet.BuildAcntCouponsRsp(), grantSubscriptionOpen: false).Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq })];
    }
}
