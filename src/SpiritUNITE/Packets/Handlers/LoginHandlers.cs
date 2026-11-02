using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Accounts;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class LoginHandlers(AccountStore accounts, TimeProvider clock, ILogger<LoginHandlers> logger)
{
    private const int StartingLevel = 1;
    private const int LoginSucceeded = 1;

    [PacketHandler(Connector.Lobby, PB_ENUM_CS_MSGID_DEF.PbCsidAcntLoginReq)]
    public GamePacket[] Login(GamePacket request)
    {
        var login = pbCSAcntLoginReq.Parser.ParseFrom(request.Payload);
        var uid = ResolveUid(request.Uid, login.PlayerName);
        var account = accounts.GetOrCreate(uid);
        logger.LogInformation("Login from uid {Uid} as {Name}", uid, account.Name);

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var response = new pbSCAcntLoginRsp
        {
            ResultInfo = new pbResultInfo(),
            IsSucc = LoginSucceeded,
            GameAcntUid = uid,
            Level = StartingLevel,
            Name = account.Name,
            Gender = account.Gender,
            RoleAvatarInfo = account.Avatar,
            NewbieState = account.BuildNewbieState(),
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

    private static ulong ResolveUid(ulong headUid, string platformAccount)
    {
        if (headUid != 0)
        {
            return headUid;
        }

        if (ulong.TryParse(platformAccount, out var accountUid) && accountUid != 0)
        {
            return accountUid;
        }

        throw new InvalidDataException("Login has no usable account id");
    }
}
