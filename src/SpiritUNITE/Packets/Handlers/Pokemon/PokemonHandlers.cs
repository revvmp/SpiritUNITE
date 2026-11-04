using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Newbie;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Pokemon;

public sealed class PokemonHandlers(RoleInfoStore roles, PokemonManager pokemon, ILogger<PokemonHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetPokemonListReq)]
    public async Task<GamePacket[]> OnGetPokemonListReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetPokemonListReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetPokemonListRsp, await pokemon.BuildGetPokemonListRspAsync(role))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetPokemonFollowReq)]
    public async Task<GamePacket[]> OnSetPokemonFollowReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var pokemonId = pbCSSetPokemonFollowReq.Parser.ParseFrom(request.PkgData).PokemonId;
        var result = role.SetPokemonFollow(pokemonId);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation(
                "Follow Pokemon {PokemonId} refused for uid {Uid}: {Result}",
                pokemonId,
                role.GameUlongUID,
                result);
        }
        else
        {
            await roles.SaveAsync(role);
            logger.LogInformation("Follow Pokemon set to {PokemonId} for uid {Uid}", pokemonId, role.GameUlongUID);
        }

        var response = new pbSCSetPokemonFollowRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            PokemonId = role.FollowPokemonId,
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetPokemonFollowRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearPokemonNewTagReq)]
    public async Task<GamePacket[]> OnClearPokemonNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var pokemonId = pbCSClearPokemonNewTagReq.Parser.ParseFrom(request.PkgData).PokemonId;
        if (role.ClearPokemonNewTag(pokemonId) is not { } cleared)
        {
            return
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearPokemonNewTagRsp, new pbSCClearPokemonNewTagRsp
                {
                    Result = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists },
                }),
            ];
        }

        await roles.SaveAsync(role);
        return
        [
            NewbieManager.BuildAddPokemonRedDot(role) with { Uid = request.Uid, ClientSeq = request.ClientSeq },
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeInfoUpdate,
                PokemonInfo = cleared.ToPokemonInfo(),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearPokemonLevelUpNewTagReq)]
    public GamePacket[] OnClearPokemonLevelUpNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var pokemonId = pbCSClearPokemonLevelUpNewTagReq.Parser.ParseFrom(request.PkgData).PokemonId;
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearPokemonLevelUpNewTagRsp, new pbSCClearPokemonLevelUpNewTagRsp
            {
                Result = new pbResultInfo { ResultId = (int)OwnedPokemonResult(role, pokemonId) },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearSkillWayUnlockNewTagReq)]
    public GamePacket[] OnClearSkillWayUnlockNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var pokemonId = pbCSClearSkillWayUnlockNewTagReq.Parser.ParseFrom(request.PkgData).PokemonID;
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearSkillWayUnlockNewTagRsp, new pbSCClearSkillWayUnlockNewTagRsp
            {
                Result = new pbResultInfo { ResultId = (int)OwnedPokemonResult(role, pokemonId) },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearPokemonPasterNewTagReq)]
    public GamePacket[] OnClearPokemonPasterNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var clear = pbCSClearPokemonPasterNewTagReq.Parser.ParseFrom(request.PkgData);
        var result = !role.OwnsPokemon(clear.PokemonId)
            ? PB_ENUM_RES_ERROR_CODE.PbErrPasterWearPokemonIdinvalid
            : clear.PasterID == PokemonManager.NoPaster
                ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
                : PB_ENUM_RES_ERROR_CODE.PbErrPasterNotExists;
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearPokemonPasterNewTagRsp, new pbSCClearPokemonPasterNewTagRsp
            {
                Result = new pbResultInfo { ResultId = (int)result },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidWearPokemonPasterReq)]
    public async Task<GamePacket[]> OnWearPokemonPasterReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var wear = pbCSWearPokemonPasterReq.Parser.ParseFrom(request.PkgData);
        var (result, used) = await pokemon.WearPokemonPasterAsync(role, wear.PokemonID, wear.PasterID);
        logger.LogInformation(
            "Holowear {PasterID} on Pokemon {PokemonID} for uid {Uid}: {Result}",
            wear.PasterID,
            wear.PokemonID,
            role.GameUlongUID,
            result);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidWearPokemonPasterRsp, new pbSCWearPokemonPasterRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                PokemonID = wear.PokemonID,
                PasterID = used.WearPasterID,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetSkillEffectsExpandPackListReq)]
    public GamePacket[] OnGetSkillEffectsExpandPackListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGetSkillEffectsExpandPackListReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetSkillEffectsExpandPackListRsp, new pbSCGetSkillEffectsExpandPackListRsp
            {
                ResultInfo = new pbResultInfo(),
                SkillEffectsExpandPackList = new pbCOMDT_SKILL_EFFECTS_EXPAND_PACK_LIST(),
            }),
        ];
    }

    private static PB_ENUM_RES_ERROR_CODE OwnedPokemonResult(LMNRoleInfo role, int pokemonId) =>
        role.OwnsPokemon(pokemonId) ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists;
}
