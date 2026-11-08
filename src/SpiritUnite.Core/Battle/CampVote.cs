using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

public enum CampVoteKind
{
    Surrender,
    HangUp,
}

public enum CampVoteOutcome
{
    Open,
    Passed,
    Failed,
}

public sealed record SurrenderRules(int StartSeconds, int CDSeconds, int InvalidSeconds, int TimesLimit)
{
    public static readonly SurrenderRules Disabled = new(int.MaxValue, 0, 0, 0);
}

public sealed class CampVote(CampVoteKind kind, int campClient, IReadOnlyList<DeskPlayer> voters, long startedAt, long surrenderTime)
{
    public const int SurrenderOpen = 0;
    public const int SurrenderFailed = 2;
    public const int SurrenderPassed = 3;

    private const int DissentersAllowed = 1;
    private const int FirstPlayerCamp = (int)PB_ENUM_COM_CAMP_CLIENT.PbCampClientPlayer1;
    private const int MinAgreesNeeded = 1;

    private readonly List<(DeskPlayer Voter, bool Agrees)> ballots = [];
    private readonly Dictionary<DeskPlayer, TimeSpan> scheduledNpcVotes = [];
    private int camp1Score;
    private int camp2Score;

    public CampVoteKind Kind { get; } = kind;
    public int CampClient { get; } = campClient;
    public IReadOnlyList<DeskPlayer> Voters { get; } = voters;
    public long StartedAt { get; } = startedAt;
    public long SurrenderTime { get; } = surrenderTime;
    public DeskPlayer? Initiator => ballots.Count > 0 ? ballots[0].Voter : null;

    public int SurrenderValidCnt => Voters.Count;

    public int ScoreBehind => CampClient == FirstPlayerCamp ? camp2Score - camp1Score : camp1Score - camp2Score;

    public int AgreesNeeded => Math.Max(MinAgreesNeeded, SurrenderValidCnt - DissentersAllowed);

    public int Agrees => ballots.Count(ballot => ballot.Agrees);

    public int Rejects => ballots.Count - Agrees;

    public CampVoteOutcome Outcome =>
        Agrees >= AgreesNeeded ? CampVoteOutcome.Passed
        : Rejects > SurrenderValidCnt - AgreesNeeded || ballots.Count == SurrenderValidCnt ? CampVoteOutcome.Failed
        : CampVoteOutcome.Open;

    public int SurrenderData
    {
        get
        {
            var data = 0;
            for (var index = 0; index < ballots.Count; index++)
            {
                if (ballots[index].Agrees)
                {
                    data |= 1 << index;
                }
            }

            return data;
        }
    }

    public void RecordScores(int camp1Score, int camp2Score)
    {
        this.camp1Score = Math.Max(0, camp1Score);
        this.camp2Score = Math.Max(0, camp2Score);
    }

    public void ScheduleNpcVote(DeskPlayer voter, TimeSpan after)
    {
        if (CanVote(voter))
        {
            scheduledNpcVotes.TryAdd(voter, after);
        }
    }

    public IReadOnlyList<DeskPlayer> TakeDueNpcVotes(TimeSpan elapsed)
    {
        DeskPlayer[] due = [.. scheduledNpcVotes.Where(scheduled => scheduled.Value <= elapsed).OrderBy(scheduled => scheduled.Value).Select(scheduled => scheduled.Key)];
        foreach (var voter in due)
        {
            scheduledNpcVotes.Remove(voter);
        }

        return due;
    }

    public bool CanVote(DeskPlayer voter) => Voters.Contains(voter) && !ballots.Any(ballot => ballot.Voter == voter);

    public bool TryCast(DeskPlayer voter, bool agrees)
    {
        if (!CanVote(voter) || Outcome != CampVoteOutcome.Open)
        {
            return false;
        }

        ballots.Add((voter, agrees));
        return true;
    }

    public pbCOMDT_CAMP_SURRENDER_INFO BuildCampSurrenderInfo() => new()
    {
        SurrenderValidCnt = SurrenderValidCnt,
        SurrenderCnt = ballots.Count,
        SurrenderData = SurrenderData,
        SurrenderTime = SurrenderTime,
        SurrenderResult = Outcome switch
        {
            CampVoteOutcome.Passed => SurrenderPassed,
            CampVoteOutcome.Failed => SurrenderFailed,
            _ => SurrenderOpen,
        },
    };

    public pbCOMDT_CAMP_SURRENDER_INFO BuildExpiredCampSurrenderInfo()
    {
        var info = BuildCampSurrenderInfo();
        info.SurrenderResult = SurrenderFailed;
        return info;
    }
}
