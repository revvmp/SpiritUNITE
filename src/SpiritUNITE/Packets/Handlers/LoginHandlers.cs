using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class LoginHandlers(RoleInfoStore roles, TimeProvider clock, ILogger<LoginHandlers> logger)
{
    private const int StartingLevel = 1;
    private const int LoginSucceeded = 1;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcntLoginReq)]
    public async Task<GamePacket[]> OnAcntLoginReq(GamePacket request)
    {
        var login = pbCSAcntLoginReq.Parser.ParseFrom(request.PkgData);
        var uid = ResolveUid(request.Uid, login.PlayerName);
        var role = await roles.GetOrCreateAsync(uid);
        logger.LogInformation("Login from uid {Uid} as {Name}", uid, role.InGameUsrName);

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var response = new pbSCAcntLoginRsp
        {
            ResultInfo = new pbResultInfo(),
            IsSucc = LoginSucceeded,
            GameAcntUid = uid,
            Level = StartingLevel,
            Name = role.InGameUsrName,
            Gender = role.Gender,
            RoleAvatarInfo = role.RoleAvatarInfo,
            NewbieState = role.BuildNewbieState(),
            ServerGameTimeStamp = now,
            ServerRealTimeStamp = now,
        };

        var session = request with { Uid = uid };
        return
        [
            session.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntLoginRsp, response),
            session.Reply(PB_ENUM_CS_MSGID_DEF.PbScidLoginFinishNtf, new pbSCLoginFinishNtf()),
        ];
    }

    private static ulong ResolveUid(ulong headUid, string platformOpenId)
    {
        if (headUid != 0)
        {
            return headUid;
        }

        if (ulong.TryParse(platformOpenId, out var gameAcntUid) && gameAcntUid != 0)
        {
            return gameAcntUid;
        }

        throw new InvalidDataException("Login has no usable account id");
    }
}
