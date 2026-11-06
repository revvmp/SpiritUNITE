using Microsoft.Extensions.Logging;
using SpiritUNITE.Battle;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Battle;

public sealed class HeroSelectionHandlers(DeskManager desks, ILogger<HeroSelectionHandlers> logger)
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

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUnLockHeroReq)]
    public async Task<GamePacket[]> OnUnLockHeroReq(GamePacket request)
    {
        var heroId = pbCSUnLockHeroReq.Parser.ParseFrom(request.PkgData).HeroId;
        var outcome = desks.UnlockHero(request.Uid, heroId);
        logger.LogInformation("Uid {Uid} unlocks hero {HeroId}: {Result}", request.Uid, heroId, outcome.Result);
        return outcome.Succeeded
            ? await DeliverAsync(request, outcome)
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUnLockHeroRsp, new pbSCUnLockHeroRsp { ResultInfo = Result(outcome) })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCancelChooseHeroReq)]
    public async Task<GamePacket[]> OnCancelChooseHeroReq(GamePacket request)
    {
        var outcome = desks.CancelChooseHero(request.Uid);
        logger.LogInformation("Uid {Uid} cancels the hero choice: {Result}", request.Uid, outcome.Result);
        return outcome.Succeeded
            ? await DeliverAsync(request, outcome)
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCancelChooseHeroRsp, new pbSCCancelChooseHeroRsp { ResultInfo = Result(outcome) })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChgWayTipReq)]
    public async Task<GamePacket[]> OnChgWayTipReq(GamePacket request)
    {
        var wayTipType = pbCSChgWayTipReq.Parser.ParseFrom(request.PkgData).WayTipType;
        var outcome = desks.ChangeWayTip(request.Uid, wayTipType);
        logger.LogInformation("Uid {Uid} changes lane to {WayTipType}: {Result}", request.Uid, wayTipType, outcome.Result);
        return outcome.Succeeded
            ? await DeliverAsync(request, outcome)
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChgWayTipRsp, new pbSCChgWayTipRsp { ResultInfo = Result(outcome), WayTipType = wayTipType })];
    }

    private async Task<GamePacket[]> DeliverAsync(GamePacket request, RoomOutcome outcome) =>
        [.. (await desks.DeliverAsync(request.Uid, outcome)).Select(message => request.Reply(message.MsgID, message.Message))];

    private static pbResultInfo Result(RoomOutcome outcome) => new() { ResultId = (int)outcome.Result };
}
