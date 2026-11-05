using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.FightHistory;

public sealed class FightHistoryRecords(RoleInfoStore roles)
{
    public async Task<pbSCFightHistoryListRsp> BuildListAsync(pbCSFightHistoryListReq query)
    {
        var response = new pbSCFightHistoryListRsp
        {
            ResultInfo = new pbResultInfo(),
            QueryUid = query.QueryUid,
            MapSubMode = query.MapSubMode,
        };
        if (await roles.FindAsync(query.QueryUid) is null)
        {
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrAcntFightHistoryListNotExist;
            return response;
        }

        response.FightHistoryList = new pbCOMDT_FIGHT_HISTORY_LIST();
        return response;
    }

    public static pbSCFightHistoryDetailRsp BuildMissingDetail(pbCSFightHistoryDetailReq query) => new()
    {
        ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrAcntFightHistoryListNotExist },
        FightRecordIndex = query.FightRecordIndex,
    };
}
