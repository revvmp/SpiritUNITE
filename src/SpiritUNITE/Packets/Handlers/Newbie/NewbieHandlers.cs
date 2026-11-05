using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Newbie;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Newbie;

public sealed class NewbieHandlers(
    RoleInfoStore roles,
    NewbieManager newbies,
    NewbieChallengeManager newbieChallenges,
    ILogger<NewbieHandlers> logger)
{
    private const int NoPokemonId = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetNewbieStateInfoReq)]
    public async Task<GamePacket[]> OnSetNewbieStateInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var newbie = pbCSSetNewbieStateInfoReq.Parser.ParseFrom(request.PkgData).Newbie;
        if (!LMNRoleInfo.IsNewbieInRange(newbie))
        {
            throw new InvalidDataException($"Newbie {newbie} is out of range");
        }

        var grants = await newbies.ArchiveAsync(role, newbie);
        await roles.SaveAsync(role);
        return
        [
            .. Replies(request, grants),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, NewbieManager.BuildNewbieStateInfoNtf(role)),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSkipNewbieStepReq)]
    public async Task<GamePacket[]> OnSkipNewbieStepReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var skipID = pbCSSkipNewbieStepReq.Parser.ParseFrom(request.PkgData).SkipID;
        if (skipID != NewbieManager.TutorialSkipID)
        {
            logger.LogInformation("Skip tutorial refused for uid {Uid}: unknown SkipID {SkipID}", role.GameUlongUID, skipID);
            return [BuildSkipNewbieStepRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid)];
        }

        var grants = await newbies.SkipTutorialAsync(role);
        await roles.SaveAsync(role);
        logger.LogInformation("Uid {Uid} skipped the tutorial with {GrantCount} grant messages", role.GameUlongUID, grants.Length);
        return
        [
            .. Replies(request, grants),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, NewbieManager.BuildNewbieStateInfoNtf(role)),
            BuildSkipNewbieStepRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidStartSingleGameReq)]
    public GamePacket[] OnStartSingleGameReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSStartSingleGameReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation("Uid {Uid} starts a local tutorial battle", role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidStartSingleGameRsp, new pbSCStartSingleGameRsp { ResultInfo = new pbResultInfo() })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetTutorialRewardReq)]
    public async Task<GamePacket[]> OnGetTutorialRewardReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var pokemonId = pbCSGetTutorialRewardReq.Parser.ParseFrom(request.PkgData).PokemonId;
        var (result, grants) = await newbies.GetTutorialRewardAsync(role, pokemonId);
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
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNewbieStateInfoNtf, NewbieManager.BuildNewbieStateInfoNtf(role)),
            .. Replies(request, grants),
            BuildGetTutorialRewardRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, pokemonId),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetNewbieChallengeReq)]
    public async Task<GamePacket[]> OnGetNewbieChallengeReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetNewbieChallengeReq.Parser.ParseFrom(request.PkgData);
        var (result, unlockInfo) = await newbieChallenges.GetNewbieChallengeAsync(role);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Newbie challenge info refused for uid {Uid}: {Result}", role.GameUlongUID, result);
        }

        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetNewbieChallengeRsp, new pbSCGetNewbieChallengeRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                UnlockInfo = unlockInfo,
            }),
        ];
    }

    private static IEnumerable<GamePacket> Replies(GamePacket request, IEnumerable<GamePacket> packets) =>
        packets.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq });

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
}
