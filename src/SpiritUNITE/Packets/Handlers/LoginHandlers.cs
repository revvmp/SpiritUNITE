using Microsoft.Extensions.Logging;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class LoginHandlers(TimeProvider clock, ILogger<LoginHandlers> logger)
{
    private const int MaxPlayerNameLength = 32;
    private const int StartingLevel = 1;
    private const int LoginSucceeded = 1;

    [PacketHandler(Connector.Lobby, PB_ENUM_CS_MSGID_DEF.PbCsidAcntLoginReq)]
    public GamePacket[] Login(GamePacket request)
    {
        var login = pbCSAcntLoginReq.Parser.ParseFrom(request.Payload);
        var name = SanitizedName(login.PlayerName, request.Uid);
        logger.LogInformation("Login from uid {Uid} as {Name}, client version {Version}",
            request.Uid, name, login.CltAppVersion);

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var response = new pbSCAcntLoginRsp
        {
            ResultInfo = new pbResultInfo(),
            IsSucc = LoginSucceeded,
            GameAcntUid = request.Uid,
            Level = StartingLevel,
            Name = name,
            NewbieState = new pbCOMDT_NEWBIE_STATE_INFO(),
            ServerGameTimeStamp = now,
            ServerRealTimeStamp = now,
        };

        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntLoginRsp, response),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidLoginFinishNtf, new pbSCLoginFinishNtf()),
        ];
    }

    private static string SanitizedName(string requestedName, ulong uid)
    {
        var trimmed = requestedName.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxPlayerNameLength || trimmed.Any(char.IsControl))
        {
            return $"Player{uid}";
        }

        return trimmed;
    }
}
