using Google.Protobuf;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.FightHistory;

public sealed class FightHistoryManager(RoleInfoStore roles, FightHistoryRepository repository)
{
    public const int MaxRecordNumPerMapSubMode = (int)PB_MACRO_COMDT_FIGHT_HISTORY_RECORD_NUM.PbComdtFightHistoryRecordNum;

    private const int AllMapSubModes = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeInvalid;
    private const int FirstMapSubMode = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceNormal;
    private const int MapSubModeMax = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeMax;

    public async Task<pbSCFightHistoryListRsp[]> BuildListAsync(pbCSFightHistoryListReq query)
    {
        if (!IsListedMapSubMode(query.MapSubMode) && query.MapSubMode != AllMapSubModes)
        {
            return [BuildMissingList(query.QueryUid, query.MapSubMode)];
        }

        int[] mapSubModes = query.MapSubMode == AllMapSubModes
            ? [.. Enumerable.Range(FirstMapSubMode, MapSubModeMax - FirstMapSubMode)]
            : [query.MapSubMode];
        if (await roles.FindAsync(query.QueryUid) is null)
        {
            return [.. mapSubModes.Select(mapSubMode => BuildMissingList(query.QueryUid, mapSubMode))];
        }

        var records = (await repository.ListAsync(query.QueryUid))
            .GroupBy(stored => stored.MapSubMode)
            .SelectMany(subModeRecords => subModeRecords.Select((stored, fightRecordIndex) => new pbCOMDT_FIGHT_HISTORY_ONE_DATA
            {
                FightHistoryRecord = pbCOMDT_FIGHT_HISTORY_RECORD.Parser.ParseFrom(stored.FightHistoryRecord),
                FightHistoryIndex = fightRecordIndex,
            }))
            .Where(data => data.FightHistoryRecord.MapMode == query.MapMode)
            .ToLookup(data => data.FightHistoryRecord.MapSubMode);
        return [.. mapSubModes.Select(mapSubMode => new pbSCFightHistoryListRsp
        {
            ResultInfo = new pbResultInfo(),
            FightHistoryList = new pbCOMDT_FIGHT_HISTORY_LIST
            {
                FightHistoryRecord = { records[mapSubMode] },
            },
            QueryUid = query.QueryUid,
            MapSubMode = mapSubMode,
        })];
    }

    public async Task<pbSCFightHistoryDetailRsp> BuildDetailAsync(pbCSFightHistoryDetailReq query)
    {
        if (!IsListedMapSubMode(query.MapSubMode)
            || query.FightRecordIndex is < 0 or >= MaxRecordNumPerMapSubMode
            || await repository.FindBattleReviewInfoAsync(query.QueryUid, query.MapSubMode, query.FightRecordIndex) is not { } battleReviewInfo)
        {
            return new pbSCFightHistoryDetailRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrAcntFightHistoryListNotExist },
                FightRecordIndex = query.FightRecordIndex,
            };
        }

        return new pbSCFightHistoryDetailRsp
        {
            ResultInfo = new pbResultInfo(),
            FightRecordIndex = query.FightRecordIndex,
            FightHistoryRecordDetail = new pbCOMDT_FIGHT_HISTORY_RECORD_DETAIL
            {
                BattleReviewInfo = pbCOMDT_BATTLE_REVIEW_INFO.Parser.ParseFrom(battleReviewInfo),
            },
        };
    }

    public static RoleFightHistory CreateRecord(
        ulong gameUlongUID,
        pbCOMDT_FIGHT_HISTORY_RECORD fightHistoryRecord,
        pbCOMDT_BATTLE_REVIEW_INFO? battleReviewInfo,
        long recordTime) => new()
        {
            GameUlongUID = gameUlongUID,
            MapSubMode = fightHistoryRecord.MapSubMode,
            FightHistoryRecord = fightHistoryRecord.ToByteArray(),
            BattleReviewInfo = (battleReviewInfo ?? new pbCOMDT_BATTLE_REVIEW_INFO()).ToByteArray(),
            RecordTime = recordTime,
        };

    private static bool IsListedMapSubMode(int mapSubMode) => mapSubMode is >= FirstMapSubMode and < MapSubModeMax;

    private static pbSCFightHistoryListRsp BuildMissingList(ulong queryUid, int mapSubMode) => new()
    {
        ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrAcntFightHistoryListNotExist },
        QueryUid = queryUid,
        MapSubMode = mapSubMode,
    };
}
