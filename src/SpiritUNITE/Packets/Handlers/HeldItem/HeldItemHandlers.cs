using Microsoft.Extensions.Logging;
using SpiritUNITE.Battle;
using SpiritUNITE.Game.HeldItem;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Handlers.Bag;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.HeldItem;

public sealed class HeldItemHandlers(
    RoleInfoStore roles,
    HeldItemManager heldItems,
    PokemonManager pokemon,
    DeskManager desks,
    ILogger<HeldItemHandlers> logger)
{
    private const int UsedRecommend = 1;
    private const int Permanent = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChgHeldItemReq)]
    public async Task<GamePacket[]> OnChgHeldItemReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var chg = pbCSChgHeldItemReq.Parser.ParseFrom(request.PkgData);
        var heldItemIds = chg.HeldItemDesignInfo?.HeldItemID.ToArray() ?? [];
        var (result, used) = await heldItems.ChgHeldItemAsync(role, chg.PokemonId, heldItemIds);
        logger.LogInformation(
            "Held items of Pokemon {PokemonId} set to {HeldItemIds} for uid {Uid}: {Result}",
            chg.PokemonId,
            string.Join(',', heldItemIds),
            role.GameUlongUID,
            result);
        var pickNotices = result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ? await desks.DeliverAsync(request.Uid, desks.ChangeHeldItems(request.Uid, chg.PokemonId, used.HeldItemID))
            : [];
        return
        [
            .. pickNotices.Select(message => request.Reply(message.MsgID, message.Message)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChgHeldItemRsp, new pbSCChgHeldItemRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                PokemonId = chg.PokemonId,
                HeldItemDesignInfo = used.BuildHeldItemDesignInfo(),
                IsTemporary = Permanent,
                IsUsedRecommend = chg.IsUsedRecommend == UsedRecommend ? UsedRecommend : 0,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidReadHeldItemReq)]
    public async Task<GamePacket[]> OnReadHeldItemReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var read = pbCSReadHeldItemReq.Parser.ParseFrom(request.PkgData);
        var used = await pokemon.FindUsedPokemonAsync(role, read.PokemonId);
        var result = role.CanUsePokemon(read.PokemonId)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrPokemonUnexists;
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidReadHeldItemRsp, new pbSCReadHeldItemRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                PokemonId = read.PokemonId,
                HeldItemDesignInfo = used.BuildHeldItemDesignInfo(),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidLevelUpOutSideItemReq)]
    public async Task<GamePacket[]> OnLevelUpOutSideItemReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var resID = pbCSLevelUpOutSideItemReq.Parser.ParseFrom(request.PkgData).ResID;
        var outcome = await heldItems.LevelUpHeldItemAsync(role, resID);
        logger.LogInformation("Level up of held item {ResID} for uid {Uid}: {Result}", resID, role.GameUlongUID, outcome.Result);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidLevelUpOutSideItemRsp, new pbSCLevelUpOutSideItemRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            ResID = resID,
        });
        return outcome.Succeeded ? [.. BagHandlers.BuildBagUpdates(request, outcome), reply] : [reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearHeldItemSlotRedotReq)]
    public async Task<GamePacket[]> OnClearHeldItemSlotRedotReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var slotIndex = pbCSClearHeldItemSlotRedotReq.Parser.ParseFrom(request.PkgData).HeldItemSlotIndex;
        var result = await heldItems.ClearHeldItemSlotRedotAsync(role, slotIndex);
        var reply = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearHeldItemSlotRedotNtf, new pbSCClearHeldItemSlotRedotNtf
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            HeldItemSlotIndex = slotIndex,
        });
        return result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ?
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf
                {
                    RedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdUnlockHelditem,
                }),
                reply,
            ]
            : [reply];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCarryTrainerItemReq)]
    public async Task<GamePacket[]> OnCarryTrainerItemReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var carry = pbCSCarryTrainerItemReq.Parser.ParseFrom(request.PkgData);
        var (result, used) = await pokemon.CarryTrainerItemAsync(role, carry.PokemonID, carry.TrainerItemID);
        logger.LogInformation(
            "Battle item {TrainerItemID} carried by Pokemon {PokemonID} for uid {Uid}: {Result}",
            carry.TrainerItemID,
            carry.PokemonID,
            role.GameUlongUID,
            result);
        var pickNotices = result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ? await desks.DeliverAsync(request.Uid, desks.ChangeTrainerItem(request.Uid, carry.PokemonID, used.LastCarryTrainerItemID))
            : [];
        return
        [
            .. pickNotices.Select(message => request.Reply(message.MsgID, message.Message)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCarryTrainerItemRsp, new pbSCCarryTrainerItemRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
                PokemonID = carry.PokemonID,
                TrainerItemID = used.LastCarryTrainerItemID,
            }),
        ];
    }
}
