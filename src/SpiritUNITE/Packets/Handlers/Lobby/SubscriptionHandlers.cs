using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Lobby;

public sealed class SubscriptionHandlers(RoleInfoStore roles, WalletManager wallets, LobbyNotices notices)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSubscrEndTimeReq)]
    public GamePacket[] OnSubscrEndTimeReq(GamePacket request)
    {
        pbCSSubscrEndTimeReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCSubscrEndTimeRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSubscrEndTimeRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSubscriptionInterfaceReq)]
    public async Task<GamePacket[]> OnSubscriptionInterfaceReq(GamePacket request)
    {
        pbCSSubscriptionInterfaceReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var wallet = await wallets.LoadAsync(role.GameUlongUID);
        return
        [
            .. notices.BuildSubscriptionInterfaceReplies(wallet.BuildAcntCouponsRsp())
                .Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
        ];
    }
}
