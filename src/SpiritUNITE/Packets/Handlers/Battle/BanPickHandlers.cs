using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Battle;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Battle;

public sealed class BanPickHandlers(DeskManager desks, ILogger<BanPickHandlers> logger)
{
    private const int SwapConfirmed = 1;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidVoteBanHeroReq)]
    public Task<GamePacket[]> OnVoteBanHeroReq(GamePacket request)
    {
        var heroId = pbCSVoteBanHeroReq.Parser.ParseFrom(request.PkgData).HeroId;
        return HandleAsync(request, $"votes to ban hero {heroId}", turn => turn.Draft.VoteBan(turn.Desk, turn.Player, heroId, lockBan: false),
            result => (PB_ENUM_CS_MSGID_DEF.PbScidVoteBanHeroRsp, new pbSCVoteBanHeroRsp { ResultInfo = result, HeroId = heroId }));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidLockBanHeroReq)]
    public Task<GamePacket[]> OnLockBanHeroReq(GamePacket request)
    {
        var heroId = pbCSLockBanHeroReq.Parser.ParseFrom(request.PkgData).HeroId;
        return HandleAsync(request, $"locks the ban of hero {heroId}", turn => turn.Draft.VoteBan(turn.Desk, turn.Player, heroId, lockBan: true),
            result => (PB_ENUM_CS_MSGID_DEF.PbScidLockBanHeroRsp, new pbSCLockBanHeroRsp { ResultInfo = result, HeroId = heroId }));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidExpectBanHeroReq)]
    public Task<GamePacket[]> OnExpectBanHeroReq(GamePacket request)
    {
        var heroId = pbCSExpectBanHeroReq.Parser.ParseFrom(request.PkgData).HeroId;
        return HandleAsync(request, $"requests a ban of hero {heroId}", turn => turn.Draft.ExpectBan(turn.Desk, turn.Player, heroId, turn.NowMs),
            result => (PB_ENUM_CS_MSGID_DEF.PbScidExpectBanHeroRsp, new pbSCExpectBanHeroRsp { ResultInfo = result, HeroId = heroId }));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPreChooseHeroReq)]
    public Task<GamePacket[]> OnPreChooseHeroReq(GamePacket request)
    {
        var heroId = pbCSPreChooseHeroReq.Parser.ParseFrom(request.PkgData).HeroId;
        return HandleAsync(request, $"pre-selects hero {heroId}", turn => turn.Draft.PreChoose(turn.Desk, turn.Player, heroId),
            result => (PB_ENUM_CS_MSGID_DEF.PbScidPreChooseHeroRsp, new pbSCPreChooseHeroRsp { ResultInfo = result, HeroId = heroId }));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidHelpChooseHeroReq)]
    public Task<GamePacket[]> OnHelpChooseHeroReq(GamePacket request)
    {
        var heroId = pbCSHelpChooseHeroReq.Parser.ParseFrom(request.PkgData).HeroId;
        return HandleAsync(request, $"asks a teammate to pick hero {heroId}", turn => turn.Draft.HelpChoose(turn.Desk, turn.Player, heroId),
            result => (PB_ENUM_CS_MSGID_DEF.PbScidHelpChooseHeroRsp, new pbSCHelpChooseHeroRsp { ResultInfo = result, HeroId = heroId }));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidConfirmHelpChooseHeroReq)]
    public Task<GamePacket[]> OnConfirmHelpChooseHeroReq(GamePacket request)
    {
        var needHelpUid = pbCSConfirmHelpChooseHeroReq.Parser.ParseFrom(request.PkgData).NeedHelpUid;
        return HandleAsync(request, $"swaps picking positions with uid {needHelpUid}", turn => turn.Draft.ConfirmHelpChoose(turn.Desk, turn.Player, needHelpUid, turn.Actions),
            result => (PB_ENUM_CS_MSGID_DEF.PbScidConfirmHelpChooseHeroRsp, new pbSCConfirmHelpChooseHeroRsp { ResultInfo = result }));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBanPickSwapPokemonReq)]
    public Task<GamePacket[]> OnBanPickSwapPokemonReq(GamePacket request)
    {
        var dstUid = pbCSBanPickSwapPokemonReq.Parser.ParseFrom(request.PkgData).DstUid;
        return HandleAsync(request, $"asks uid {dstUid} to swap Pokemon", turn => turn.Draft.RequestSwap(turn.Desk, turn.Player, dstUid, turn.NowMs),
            result => (PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonRsp, new pbSCBanPickSwapPokemonRsp { ResultInfo = result }));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBanPickSwapPokemonConfirmReq)]
    public Task<GamePacket[]> OnBanPickSwapPokemonConfirmReq(GamePacket request)
    {
        var isConfirm = pbCSBanPickSwapPokemonConfirmReq.Parser.ParseFrom(request.PkgData).IsConfirm == SwapConfirmed;
        return HandleAsync(request, $"answers a Pokemon swap ({(isConfirm ? "accept" : "decline")})", turn => turn.Draft.ConfirmSwap(turn.Desk, turn.Player, isConfirm),
            result => (PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonConfirmRsp, new pbSCBanPickSwapPokemonConfirmRsp { ResultInfo = result, IsConfirm = isConfirm ? SwapConfirmed : 0 }));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBanPickSwapPokemonCancelReq)]
    public Task<GamePacket[]> OnBanPickSwapPokemonCancelReq(GamePacket request)
    {
        pbCSBanPickSwapPokemonCancelReq.Parser.ParseFrom(request.PkgData);
        return HandleAsync(request, "cancels a Pokemon swap", turn => turn.Draft.CancelSwap(turn.Player),
            result => (PB_ENUM_CS_MSGID_DEF.PbScidBanPickSwapPokemonCancelRsp, new pbSCBanPickSwapPokemonCancelRsp { ResultInfo = result }));
    }

    private async Task<GamePacket[]> HandleAsync(
        GamePacket request,
        string action,
        Func<BanPickTurn, RoomOutcome> act,
        Func<pbResultInfo, (PB_ENUM_CS_MSGID_DEF MsgID, IMessage Message)> refusal)
    {
        var outcome = desks.InDraft(request.Uid, act);
        logger.LogInformation("Uid {Uid} {Action} in the draft: {Result}", request.Uid, action, outcome.Result);
        if (!outcome.Succeeded)
        {
            var (msgID, message) = refusal(new pbResultInfo { ResultId = (int)outcome.Result });
            return [request.Reply(msgID, message)];
        }

        return [.. (await desks.DeliverAsync(request.Uid, outcome)).Select(message => request.Reply(message.MsgID, message.Message))];
    }
}
