using SpiritUNITE.Databin;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Ranked;

public readonly record struct RankedPosition(int RankedDivision, int WinPoint, int MasterPoint);

public readonly record struct RankedMove(RankedPosition Position, bool DivisionFallProtect);

public sealed class RankedLadder
{
    private const int FallProtected = 1;
    private const int MasterPointDivisionWinPoint = 1;
    private const int MasterRankedDivision = (int)PB_ENUM_RES_RANK_DIVISION.PbResRankDivisionMaster;
    private const int LegendRankedDivision = (int)PB_ENUM_RES_RANK_DIVISION.PbResRankDivisionLegend1;

    private readonly pbRankedDivision[] ladder;
    private readonly Dictionary<int, pbRankedDivision> rowsByDivision;
    private readonly int masterIndex;
    private readonly int legendIndex;
    private readonly int masterPointInitValue;

    public RankedLadder(GameData gameData)
    {
        ladder = [.. gameData.RankedDivision.Rows.OrderBy(row => row.Index)];
        rowsByDivision = ladder.ToDictionary(row => row.RankedDivision);
        masterIndex = rowsByDivision[MasterRankedDivision].Index;
        legendIndex = rowsByDivision[LegendRankedDivision].Index;
        masterPointInitValue = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalRankedDivisionMasterPointInitValue);
    }

    public pbRankedDivision Origin => ladder[0];

    public const int UnplacedRankedDivision = 0;

    public static RankedPosition UnplacedPosition => new(UnplacedRankedDivision, 0, 0);

    public bool IsKnown(int rankedDivision) => rowsByDivision.ContainsKey(rankedDivision);

    public pbRankedDivision RowOf(int rankedDivision) => rowsByDivision.GetValueOrDefault(rankedDivision) ?? Origin;

    public int BigOf(int rankedDivision) => RowOf(rankedDivision).BigRankedDivision;

    public int IndexOf(int rankedDivision) => RowOf(rankedDivision).Index;

    public bool IsMaster(int rankedDivision) => IndexOf(rankedDivision) >= masterIndex;

    public bool IsLegend(int rankedDivision) => IndexOf(rankedDivision) >= legendIndex;

    public int TicketConsumeOf(int rankedDivision) => RowOf(rankedDivision).TicketConsume;

    public bool IsAtLeast(int rankedDivision, int thresholdRankedDivision) =>
        IsKnown(thresholdRankedDivision) && IndexOf(rankedDivision) >= IndexOf(thresholdRankedDivision);

    public IEnumerable<int> DivisionsOfBig(int bigRankedDivision) =>
        ladder.Where(row => row.BigRankedDivision == bigRankedDivision).Select(row => row.RankedDivision);

    public int MaxBig => ladder[^1].BigRankedDivision;

    public int MasterBig => BigOf(MasterRankedDivision);

    public int MasterIndex => masterIndex;

    public int MasterPointClassWidth => rowsByDivision[MasterRankedDivision].MasterPointCeil - rowsByDivision[MasterRankedDivision].MasterPointFloor + 1;

    public RankedPosition ToClientPosition(RankedPosition position) => IsMaster(position.RankedDivision)
        ? position with { RankedDivision = FirstRowOfBig(BigOf(position.RankedDivision)).RankedDivision, WinPoint = MasterPointDivisionWinPoint }
        : position;

    public RankedPosition StartOfBig(int bigRankedDivision)
    {
        var row = FirstRowOfBig(bigRankedDivision);
        return IsMaster(row.RankedDivision)
            ? new RankedPosition(row.RankedDivision, 0, row.MasterPointFloor)
            : new RankedPosition(row.RankedDivision, 0, 0);
    }

    private pbRankedDivision FirstRowOfBig(int bigRankedDivision) =>
        ladder.FirstOrDefault(candidate => candidate.BigRankedDivision == bigRankedDivision) ?? Origin;

    public RankedMove Move(RankedPosition from, int delta)
    {
        var row = RowOf(from.RankedDivision);
        return IsMaster(row.RankedDivision)
            ? MoveMasterPoint(from.MasterPoint + delta, delta < 0)
            : MoveWinPoint(row, from.WinPoint + delta);
    }

    private RankedMove MoveWinPoint(pbRankedDivision row, int winPoint)
    {
        while (winPoint >= row.UpgradeWinPoint && row.Index < ladder.Length)
        {
            winPoint -= row.UpgradeWinPoint;
            row = ladder[row.Index];
            if (IsMaster(row.RankedDivision))
            {
                return MoveMasterPoint(masterPointInitValue + winPoint, falling: false);
            }
        }

        var fallProtect = false;
        while (winPoint < 0)
        {
            if (row.DivisionFallProtect == FallProtected || row.Index == Origin.Index)
            {
                winPoint = 0;
                fallProtect = row.DivisionFallProtect == FallProtected;
                break;
            }

            row = ladder[row.Index - 2];
            winPoint += row.UpgradeWinPoint;
        }

        return new RankedMove(new RankedPosition(row.RankedDivision, winPoint, 0), fallProtect);
    }

    private RankedMove MoveMasterPoint(int masterPoint, bool falling)
    {
        var masterRows = ladder.Where(row => row.Index >= masterIndex).ToArray();
        var floor = masterRows[0].MasterPointFloor;
        var ceil = masterRows[^1].MasterPointCeil;
        var fallProtect = falling && masterPoint < floor;
        var clamped = Math.Clamp(masterPoint, floor, ceil);
        var row = masterRows.LastOrDefault(candidate => candidate.MasterPointFloor <= clamped) ?? masterRows[0];
        return new RankedMove(new RankedPosition(row.RankedDivision, 0, clamped), fallProtect);
    }
}
