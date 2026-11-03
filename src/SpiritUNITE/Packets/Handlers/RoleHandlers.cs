using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class RoleHandlers(
    RoleInfoStore roles,
    GameData gameData,
    TimeProvider clock,
    ILogger<RoleHandlers> logger)
{
    private const int NoPokemonId = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetRoleCosmeticReq)]
    public async Task<GamePacket[]> OnSetRoleCosmeticReq(GamePacket request)
    {
        var role = RequireRole(request);
        var cosmetic = pbCSSetRoleCosmeticReq.Parser.ParseFrom(request.PkgData);
        var result = role.SetRoleCosmetic(
            gameData,
            cosmetic.Gender,
            cosmetic.RoleAvatarInfo ?? new pbCOMDT_ROLE_AVATAR_INFO(),
            clock.GetUtcNow().ToUnixTimeSeconds());
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Role cosmetic refused for uid {Uid}: {Result}", role.GameUlongUID, result);
        }
        else
        {
            await roles.SaveAsync(role);
        }

        var response = new pbSCSetRoleCosmeticRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            RoleAvatarInfo = role.RoleAvatarInfo,
            Gender = role.Gender,
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetRoleCosmeticRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetNewbieStateInfoReq)]
    public async Task<GamePacket[]> OnSetNewbieStateInfoReq(GamePacket request)
    {
        var role = RequireRole(request);
        var newbie = pbCSSetNewbieStateInfoReq.Parser.ParseFrom(request.PkgData).Newbie;
        if (!role.SetNewbieState(newbie))
        {
            throw new InvalidDataException($"Newbie {newbie} is out of range");
        }

        await roles.SaveAsync(role);
        var response = new pbSCNewbieStateInfoNtf { NewbieState = role.BuildNewbieState() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSkipNewbieStepReq)]
    public async Task<GamePacket[]> OnSkipNewbieStepReq(GamePacket request)
    {
        var role = RequireRole(request);
        var skipID = pbCSSkipNewbieStepReq.Parser.ParseFrom(request.PkgData).SkipID;
        if (!role.SkipNewbieStep(skipID))
        {
            return [BuildSkipNewbieStepRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid)];
        }

        await roles.SaveAsync(role);
        var state = new pbSCNewbieStateInfoNtf { NewbieState = role.BuildNewbieState() };
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, state),
            BuildSkipNewbieStepRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetTutorialRewardReq)]
    public async Task<GamePacket[]> OnGetTutorialRewardReq(GamePacket request)
    {
        var role = RequireRole(request);
        var pokemonId = pbCSGetTutorialRewardReq.Parser.ParseFrom(request.PkgData).PokemonId;
        var result = role.GetTutorialReward(gameData, pokemonId);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation(
                "Tutorial reward {PokemonId} refused for uid {Uid}: {Result}",
                pokemonId,
                role.GameUlongUID,
                result);
            return [BuildGetTutorialRewardRsp(request, result, NoPokemonId)];
        }

        await roles.SaveAsync(role);
        logger.LogInformation("Tutorial reward {PokemonId} granted to uid {Uid}", pokemonId, role.GameUlongUID);
        var gain = new pbSCUpdatePokemonNtf
        {
            UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
            PokemonInfo = LMNRoleInfo.BuildPokemonInfo(pokemonId),
        };
        var state = new pbSCNewbieStateInfoNtf { NewbieState = role.BuildNewbieState() };
        GamePacket[] followUpdate = role.FollowPokemonId == pokemonId ? [BuildSetPokemonFollowRsp(request, pokemonId)] : [];
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, gain),
            .. followUpdate,
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, state),
            BuildGetTutorialRewardRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, pokemonId),
        ];
    }

    private static GamePacket BuildSetPokemonFollowRsp(GamePacket request, int pokemonId) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetPokemonFollowRsp, new pbSCSetPokemonFollowRsp
        {
            ResultInfo = new pbResultInfo(),
            PokemonId = pokemonId,
        });

    private static GamePacket BuildGetTutorialRewardRsp(GamePacket request, PB_ENUM_RES_ERROR_CODE result, int pokemonId) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetTutorialRewardRsp, new pbSCGetTutorialRewardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            PokemonId = pokemonId,
        });

    private static GamePacket BuildSkipNewbieStepRsp(GamePacket request, PB_ENUM_RES_ERROR_CODE result) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSkipNewbieStepRsp, new pbSCSkipNewbieStepRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        });

    private LMNRoleInfo RequireRole(GamePacket request) =>
        roles.Find(request.Uid) ?? throw new InvalidDataException("Message requires a logged in role");
}
