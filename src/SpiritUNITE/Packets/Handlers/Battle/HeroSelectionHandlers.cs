using Microsoft.Extensions.Logging;
using SpiritUNITE.Battle;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Battle;

public sealed class HeroSelectionHandlers(
    DeskManager desks,
    PokemonManager pokemon,
    RoleInfoStore roles,
    ILogger<HeroSelectionHandlers> logger)
{
    private const int FromPokemonBox = 1;
    private const int HeroPokemonIdBase = (int)PB_MACRO_HERO_POKEMON_ID_BASE.PbHeroPokemonIdBase;

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

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChgSkillRecommendTypeReq)]
    public async Task<GamePacket[]> OnChgSkillRecommendTypeReq(GamePacket request)
    {
        var change = pbCSChgSkillRecommendTypeReq.Parser.ParseFrom(request.PkgData);
        var outcome = change.IsFromPokemonBox == FromPokemonBox
            ? new RoomOutcome()
            : Enum.IsDefined((PB_ENUM_ENM_SKILL_RECOMMEND_TYPE)change.SkillRecommendType)
                ? desks.ChangeSkillRecommend(
                    request.Uid,
                    change.HeroId,
                    new pbCOMDT_SKILLRECOMMENDINFO { SkillRecommendType = change.SkillRecommendType },
                    PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroChgRecommend)
                : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrSkillRecommendTypeInvalid);
        var result = outcome.Succeeded
            ? (await pokemon.SaveSkillRecommendTypeAsync(roles.Require(request.Uid), PokemonIdOf(change.HeroId), change.SkillRecommendType)).Result
            : outcome.Result;
        logger.LogInformation(
            "Uid {Uid} sets the skill recommendation of hero {HeroId} to {SkillRecommendType} (from Pokemon box {IsFromPokemonBox}): {Result}",
            request.Uid,
            change.HeroId,
            change.SkillRecommendType,
            change.IsFromPokemonBox,
            result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChgSkillRecommendTypeRsp, new pbSCChgSkillRecommendTypeRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            SkillRecommendType = change.SkillRecommendType,
            IsFromPokemonBox = change.IsFromPokemonBox,
        });
        return outcome.Succeeded ? [.. await DeliverAsync(request, outcome), response] : [response];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChgUserDefineSkillRecommendReq)]
    public async Task<GamePacket[]> OnChgUserDefineSkillRecommendReq(GamePacket request)
    {
        var change = pbCSChgUserDefineSkillRecommendReq.Parser.ParseFrom(request.PkgData);
        var outcome = !PokemonManager.IsValidSkillSlotRecommend(change.SkillSlotRecommend)
            ? RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrSkillRecommendTypeInvalid)
            : change.IsFromPokemonBox == FromPokemonBox
                ? new RoomOutcome()
                : desks.ChangeSkillRecommend(
                    request.Uid,
                    change.HeroId,
                    new pbCOMDT_SKILLRECOMMENDINFO
                    {
                        SkillRecommendType = (int)PB_ENUM_ENM_SKILL_RECOMMEND_TYPE.PbEnmSkillRecommendTypeUserdefine,
                        SkillSlotRecommend = { change.SkillSlotRecommend },
                    },
                    PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroUserdefineSkill);
        var result = outcome.Succeeded
            ? (await pokemon.SaveUserDefineSkillRecommendAsync(roles.Require(request.Uid), PokemonIdOf(change.HeroId), change.SkillSlotRecommend)).Result
            : outcome.Result;
        logger.LogInformation(
            "Uid {Uid} sets the user-defined moves of hero {HeroId} to {SkillSlotRecommend} (from Pokemon box {IsFromPokemonBox}): {Result}",
            request.Uid,
            change.HeroId,
            string.Join(',', change.SkillSlotRecommend),
            change.IsFromPokemonBox,
            result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChgUserDefineSkillRecommendRsp, new pbSCChgUserDefineSkillRecommendRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            SkillSlotRecommend = { change.SkillSlotRecommend },
            IsFromPokemonBox = change.IsFromPokemonBox,
        });
        return outcome.Succeeded ? [.. await DeliverAsync(request, outcome), response] : [response];
    }

    private static int PokemonIdOf(int heroId) => heroId % HeroPokemonIdBase;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRandomChooseHeroReq)]
    public async Task<GamePacket[]> OnRandomChooseHeroReq(GamePacket request)
    {
        var outcome = await desks.RandomChooseHeroAsync(request.Uid);
        logger.LogInformation("Uid {Uid} rerolls the random hero: {Result}", request.Uid, outcome.Result);
        return outcome.Succeeded
            ? await DeliverAsync(request, outcome)
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRandomChooseHeroRsp, new pbSCRandomChooseHeroRsp { ResultInfo = Result(outcome) })];
    }

    private async Task<GamePacket[]> DeliverAsync(GamePacket request, RoomOutcome outcome) =>
        [.. (await desks.DeliverAsync(request.Uid, outcome)).Select(message => request.Reply(message.MsgID, message.Message))];

    private static pbResultInfo Result(RoomOutcome outcome) => new() { ResultId = (int)outcome.Result };
}
