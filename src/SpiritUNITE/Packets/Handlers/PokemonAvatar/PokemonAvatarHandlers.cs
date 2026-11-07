using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.PokemonAvatar;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.PokemonAvatar;

public sealed class PokemonAvatarHandlers(
    RoleInfoStore roles,
    PokemonAvatarManager pokemonAvatars,
    ILogger<PokemonAvatarHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUseSkillEffectsExpandPackReq)]
    public async Task<GamePacket[]> OnUseSkillEffectsExpandPackReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var packID = pbCSUseSkillEffectsExpandPackReq.Parser.ParseFrom(request.PkgData).SkillEffectsExpandPackID;
        var use = await pokemonAvatars.UseSkillEffectsExpandPackAsync(role, packID);
        logger.LogInformation("Skill effect pack {PackID} use by uid {Uid}: {Result}", packID, role.GameUlongUID, use.Result);
        return
        [
            .. use.UpdateNtf is { } ntf ? [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateSkillEffectsExpandPackNtf, ntf)] : Array.Empty<GamePacket>(),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseSkillEffectsExpandPackRsp, new pbSCUseSkillEffectsExpandPackRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)use.Result },
                NewSkillEffectsExpandPackID = use.NewSkillEffectsExpandPackID,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearSkillEffectsExpandPackNewTagReq)]
    public async Task<GamePacket[]> OnClearSkillEffectsExpandPackNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var packID = pbCSClearSkillEffectsExpandPackNewTagReq.Parser.ParseFrom(request.PkgData).SkillEffectsExpandPackID;
        var result = pokemonAvatars.ClearSkillEffectsExpandPackNewTag(role, packID, out var roleChanged);
        if (roleChanged)
        {
            await roles.SaveAsync(role);
        }

        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearSkillEffectsExpandPackNewTagRsp, new pbSCClearSkillEffectsExpandPackNewTagRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                SkillEffectsExpandPackID = packID,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetSpecialEffectInfoReq)]
    public GamePacket[] OnGetSpecialEffectInfoReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGetSpecialEffectInfoReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetSpecialEffectInfoRsp, new pbSCGetSpecialEffectInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                SpecialEffectInfo = PokemonAvatarManager.BuildSpecialEffectInfo(),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidTakeOnSpecialEffectReq)]
    public GamePacket[] OnTakeOnSpecialEffectReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var specialEffectList = pbCSTakeOnSpecialEffectReq.Parser.ParseFrom(request.PkgData).SpecialEffectList;
        var result = PokemonAvatarManager.ChangeSpecialEffects(role, specialEffectList, out var failList);
        logger.LogInformation("Special effect take on of {Count} for uid {Uid}: {Result}", specialEffectList.Count, role.GameUlongUID, result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidTakeOnSpecialEffecRsp, new pbSCTakeOnSpecialEffecRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                SpecialEffectFailList = { failList },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidTakeOffSpecialEffectReq)]
    public GamePacket[] OnTakeOffSpecialEffectReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var specialEffectList = pbCSTakeOffSpecialEffectReq.Parser.ParseFrom(request.PkgData).SpecialEffectList;
        var result = PokemonAvatarManager.ChangeSpecialEffects(role, specialEffectList, out var failList);
        logger.LogInformation("Special effect take off of {Count} for uid {Uid}: {Result}", specialEffectList.Count, role.GameUlongUID, result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidTakeOffSpecialEffecRsp, new pbSCTakeOffSpecialEffecRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                SpecialEffectFailList = { failList },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPasterUseSprayerInfoReq)]
    public async Task<GamePacket[]> OnPasterUseSprayerInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSPasterUseSprayerInfoReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPasterUseSprayerInfoRsp, new pbSCPasterUseSprayerInfoRsp
            {
                SprayerInfo = await pokemonAvatars.BuildPasterUseSprayerInfoAsync(role),
                ResultInfo = new pbResultInfo(),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUseSprayerToPasterReq)]
    public async Task<GamePacket[]> OnUseSprayerToPasterReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var use = pbCSUseSprayerToPasterReq.Parser.ParseFrom(request.PkgData);
        var sprayerItemList = use.SprayerItemList?.SprayerItemList ?? [];
        var spray = await pokemonAvatars.UseSprayerToPasterAsync(role, use.PasterID, use.PasterSprayID, sprayerItemList);
        logger.LogInformation(
            "Glitter spray {PasterSprayID} on holowear {PasterID} by uid {Uid}: {Result}",
            use.PasterSprayID,
            use.PasterID,
            role.GameUlongUID,
            spray.Result);
        return
        [
            .. spray.ChangedItems.Count > 0
                ? [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(spray.ChangedItems))]
                : Array.Empty<GamePacket>(),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUseSprayerToPasterRsp, new pbSCUseSprayerToPasterRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)spray.Result },
                PasterUseSprayerInfo = spray.PasterUseSprayerInfo,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePasterUseSprayerReq)]
    public async Task<GamePacket[]> OnBattlePasterUseSprayerReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var use = pbCSBattlePasterUseSprayerReq.Parser.ParseFrom(request.PkgData);
        var spray = await pokemonAvatars.BattlePasterUseSprayerAsync(role, use.PasterID, use.PasterSprayID);
        logger.LogInformation(
            "Glitter spray {PasterSprayID} equip on holowear {PasterID} by uid {Uid}: {Result}",
            use.PasterSprayID,
            use.PasterID,
            role.GameUlongUID,
            spray.Result);
        return
        [
            .. spray.UpdateNtf is { } ntf ? [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonAvatarNtf, ntf)] : Array.Empty<GamePacket>(),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePasterUseSprayerRsp, new pbSCBattlePasterUseSprayerRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)spray.Result },
                PasterUseSprayerInfo = spray.PasterUseSprayerInfo,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPasterSprayerClearExclusiveRedDotReq)]
    public GamePacket[] OnPasterSprayerClearExclusiveRedDotReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var pokemonID = pbCSPasterSprayerClearExclusiveRedDotReq.Parser.ParseFrom(request.PkgData).PokemonID;
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPasterSprayerClearExclusiveRedDotRsp, new pbSCPasterSprayerClearExclusiveRedDotRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PokemonAvatarManager.PasterSprayerClearExclusiveRedDot(role, pokemonID) },
            }),
        ];
    }
}
