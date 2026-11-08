using Microsoft.Extensions.Logging;
using SpiritUnite.Battle;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Profile;

public sealed class CanopyHandlers(RoleInfoStore roles, ProfileManager profiles, RelayManager relay, ILogger<CanopyHandlers> logger)
{
    private const int CanopyUpdateType = 2;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetCanopyListReq)]
    public async Task<GamePacket[]> OnGetCanopyListReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetCanopyListReq.Parser.ParseFrom(request.PkgData);
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var response = new pbSCGetCanopyListRsp
        {
            ResultInfo = new pbResultInfo(),
            CanopyList = profiles.BuildCanopyList(role),
            CurCanopyID = profiles.GetCurCanopyID(role, style),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetCanopyListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearCanopyNewTagReq)]
    public async Task<GamePacket[]> OnClearCanopyNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var canopyId = pbCSClearCanopyNewTagReq.Parser.ParseFrom(request.PkgData).CanopyId;
        if (canopyId != ProfileStyle.NoCanopyID && !role.OwnsItem(RoleItemType.Canopy, canopyId))
        {
            logger.LogInformation("Backdrop new tag clear refused for uid {Uid}: {CanopyId} not owned", role.GameUlongUID, canopyId);
            return [BuildClearCanopyNewTagRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCanopyNotExists, canopyId)];
        }

        int[] canopyIds = canopyId == ProfileStyle.NoCanopyID
            ? [.. role.GetOwnedItems(RoleItemType.Canopy).Select(item => item.ItemId)]
            : [canopyId];
        var cleared = role.ClearNewTags(RoleItemType.Canopy, canopyIds);
        if (cleared.Length == 0)
        {
            return [BuildClearCanopyNewTagRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, canopyId)];
        }

        await roles.SaveAsync(role);
        var update = new pbSCUpdateCanopyNtf
        {
            CanopyUpdateList =
            {
                cleared.Select(item => new pbCOMDT_CANOPY_UPDATE_NODE
                {
                    UpdateType = CanopyUpdateType,
                    Canopy = ProfileManager.ToCanopyInfo(item),
                }),
            },
        };
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateCanopyNtf, update),
            BuildClearCanopyNewTagRsp(request, PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, canopyId),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetCurPersonalCanopyReq)]
    public async Task<GamePacket[]> OnSetCurPersonalCanopyReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var canopyId = pbCSSetCurPersonalCanopyReq.Parser.ParseFrom(request.PkgData).CanopyId;
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = profiles.SetCurPersonalCanopy(role, style, canopyId);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await profiles.SaveAsync(role.GameUlongUID, style);
        }

        logger.LogInformation("Backdrop {CanopyId} set for uid {Uid}: {Result}", canopyId, role.GameUlongUID, result);
        var curCanopyID = profiles.GetCurCanopyID(role, style);
        var response = new pbSCSetCurPersonalCanopyRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            CanopyId = curCanopyID,
            CanopyInfo = BuildCanopyShowInfo(role, curCanopyID),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetCurPersonalCanopyRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCancelCurCanopyReq)]
    public async Task<GamePacket[]> OnCancelCurCanopyReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var canopyId = pbCSCancelCurCanopyReq.Parser.ParseFrom(request.PkgData).CanopyId;
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = ProfileManager.CancelCurCanopy(style, canopyId);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await profiles.SaveAsync(role.GameUlongUID, style);
        }

        logger.LogInformation("Backdrop {CanopyId} cancelled for uid {Uid}: {Result}", canopyId, role.GameUlongUID, result);
        var response = new pbSCCancelCurCanopyRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            CanopyId = canopyId,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCancelCurCanopyRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidShowMyCanopyReq)]
    public async Task<GamePacket[]> OnShowMyCanopyReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var canopyId = pbCSShowMyCanopyReq.Parser.ParseFrom(request.PkgData).CanopyId;
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = profiles.CheckCanopy(role, canopyId);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc && canopyId != profiles.GetCurCanopyID(role, style))
        {
            result = PB_ENUM_RES_ERROR_CODE.PbErrCanopyNotExists;
        }

        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            result = await relay.ShowCanopyAsync(role.GameUlongUID, canopyId);
        }

        logger.LogInformation("Backdrop {CanopyId} shown in battle loading by uid {Uid}: {Result}", canopyId, role.GameUlongUID, result);
        var response = new pbSCShowMyCanopyRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            CanopyId = canopyId,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidShowMyCanopyRsp, response)];
    }

    private static pbCOMDT_CANOPY_SHOW_INFO BuildCanopyShowInfo(LMNRoleInfo role, int canopyID)
    {
        var owned = role.GetOwnedItems(RoleItemType.Canopy).FirstOrDefault(item => item.ItemId == canopyID);
        return owned is null
            ? new pbCOMDT_CANOPY_SHOW_INFO()
            : new pbCOMDT_CANOPY_SHOW_INFO { CanopyID = owned.ItemId, NewTag = owned.NewTag, TimeLimited = owned.TimeLimited };
    }

    private static GamePacket BuildClearCanopyNewTagRsp(GamePacket request, PB_ENUM_RES_ERROR_CODE result, int canopyId) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearCanopyNewTagRsp, new pbSCClearCanopyNewTagRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            CanopyId = canopyId,
        });
}
