using Microsoft.Extensions.Logging;
using SpiritUNITE.Battle;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Online;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Battle;

public sealed class HeroSelectionHandlers(DeskManager desks, LobbyRoles lobbyRoles, ILogger<HeroSelectionHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChooseHeroReq)]
    public async Task<GamePacket[]> OnChooseHeroReq(GamePacket request)
    {
        var heroId = pbCSChooseHeroReq.Parser.ParseFrom(request.PkgData).HeroId;
        var outcome = desks.ChooseHero(request.Uid, heroId);
        logger.LogInformation("Uid {Uid} chooses hero {HeroId}: {Result}", request.Uid, heroId, outcome.Result);
        return outcome.Succeeded
            ? await DeliverAsync(request, outcome)
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChooseHeroRsp, new pbSCChooseHeroRsp { ResultInfo = Result(outcome) })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidConfirmHeroReq)]
    public async Task<GamePacket[]> OnConfirmHeroReq(GamePacket request)
    {
        var heroId = pbCSConfirmHeroReq.Parser.ParseFrom(request.PkgData).HeroId;
        var outcome = desks.ConfirmHero(request.Uid, heroId);
        logger.LogInformation("Uid {Uid} confirms hero {HeroId}: {Result}", request.Uid, heroId, outcome.Result);
        return outcome.Succeeded
            ? await DeliverAsync(request, outcome)
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidConfirmHeroRsp, new pbSCConfirmHeroRsp { ResultInfo = Result(outcome) })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidLockHeroReq)]
    public async Task<GamePacket[]> OnLockHeroReq(GamePacket request)
    {
        var heroId = pbCSLockHeroReq.Parser.ParseFrom(request.PkgData).HeroId;
        var outcome = desks.LockHero(request.Uid, heroId);
        logger.LogInformation("Uid {Uid} locks hero {HeroId}: {Result}", request.Uid, heroId, outcome.Result);
        return outcome.Succeeded
            ? await DeliverAsync(request, outcome)
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidLockHeroRsp, new pbSCLockHeroRsp { ResultInfo = Result(outcome) })];
    }

    private async Task<GamePacket[]> DeliverAsync(GamePacket request, RoomOutcome outcome)
    {
        foreach (var group in outcome.Pushes.Where(push => push.Uid != request.Uid).GroupBy(push => push.Uid))
        {
            var packets = group.Select(push => GamePacket.Create(push.Message.MsgID, push.Message.Message)).ToArray();
            if (!await lobbyRoles.PushAsync(group.Key, packets))
            {
                logger.LogInformation("Pick notices for uid {Uid} were not delivered: the role is offline", group.Key);
            }
        }

        return
        [
            .. outcome.Pushes
                .Where(push => push.Uid == request.Uid)
                .Select(push => request.Reply(push.Message.MsgID, push.Message.Message)),
        ];
    }

    private static pbResultInfo Result(RoomOutcome outcome) => new() { ResultId = (int)outcome.Result };
}
