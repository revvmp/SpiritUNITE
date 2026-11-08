using SpiritUnite.Databin;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.PveChallenge;

public sealed record PveChallengeNode(int StageID, int NodeID, int DiffLevel, int StarNum, bool IsUnLock, bool IsComplete)
{
    public pbCOMDT_PVE_CHALLENGE_NODE_INFO ToNodeInfo() => new()
    {
        NodeID = NodeID,
        StarNum = StarNum,
        IsUnLock = IsUnLock ? 1 : 0,
        IsComplete = IsComplete ? 1 : 0,
        StageID = StageID,
        DiffLevel = DiffLevel,
    };
}

public sealed class PveChallengeProgress
{
    public const int NormalDifficultyLevel = (int)PB_ENUM_RES_PVESTAGE_DIFFICULTY_TYPE.PbResVestageDifficultyTypeNormal;
    public const int HardDifficultyLevel = (int)PB_ENUM_RES_PVESTAGE_DIFFICULTY_TYPE.PbResVestageDifficultyTypeHard;

    private const int TutorialBattleGuideType = (int)PB_ENUM_RES_NEWBIE_GUIDE_TYPE.PbResNewbieGuideTypeBattleTutorial;
    private const int NoBattleId = 0;
    private const int NoNode = 0;
    private const int UnlockedByTable = 1;
    private const int NormalType = 1;
    private const int TutorialBattleStarNum = 3;
    private const int NotCompletedStarNum = 0;

    private readonly GameData gameData;
    private readonly IReadOnlyDictionary<int, int> newbieByBattleId;
    private readonly ILookup<int, pbPveChallengeStageRoute> routesByNewbie;

    public PveChallengeProgress(GameData gameData)
    {
        this.gameData = gameData;
        newbieByBattleId = gameData.NewbieGuideSteps.Rows
            .Where(step => step.GuideType == TutorialBattleGuideType && step.GuideParamInt.Count > 0)
            .GroupBy(step => step.GuideParamInt[0])
            .ToDictionary(group => group.Key, group => group.First().NewbieEnum);
        routesByNewbie = gameData.PveChallengeStageRoute.Rows
            .Where(route => route.BattleId != NoBattleId && newbieByBattleId.ContainsKey(route.BattleId))
            .ToLookup(route => newbieByBattleId[route.BattleId]);
    }

    public pbPveChallengeStageRoute? FindRoute(int stageID, int nodeID, int difficultyLevel) =>
        gameData.PveChallengeStageRoute.Find(pbPveChallengeStageRoute.Key(stageID, nodeID, difficultyLevel))
            is { } route && route.StageID == stageID && route.NodeID == nodeID && route.DifficultyLevel == difficultyLevel
            ? route
            : null;

    public IReadOnlyList<PveChallengeNode> Load(LMNRoleInfo role)
    {
        var completed = gameData.PveChallengeStageRoute.Rows.Where(route => GetStarNum(role, route) > NotCompletedStarNum).ToArray();
        var unlocked = gameData.PveChallengeStageRoute.Rows
            .Where(IsUnlockedFromStart)
            .Concat(completed.SelectMany(NextUnLockRoutes))
            .ToHashSet();
        unlocked.UnionWith(completed);
        return
        [
            .. unlocked
                .OrderBy(route => route.StageID)
                .ThenBy(route => route.DifficultyLevel)
                .ThenBy(route => route.NodeID)
                .Select(route => ToNode(route, GetStarNum(role, route))),
        ];
    }

    public int CountOpenNormalNodes(LMNRoleInfo role) =>
        Load(role).Count(node => node.DiffLevel == NormalDifficultyLevel && !node.IsComplete);

    public int GetStarNum(LMNRoleInfo role, pbPveChallengeStageRoute route)
    {
        var storedStarNum = role.GetPveChallengeStarNum(route.StageID, route.NodeID, route.DifficultyLevel);
        return storedStarNum > NotCompletedStarNum || !IsCompletedByNewbie(role, route) ? storedStarNum : TutorialBattleStarNum;
    }

    public pbSCPveChallengeStageInfoNtf BuildCompletionNtf(LMNRoleInfo role, pbPveChallengeStageRoute route) => new()
    {
        PveChallengeInfo = BuildInfo(
        [
            .. NextUnLockRoutes(route).Select(next => ToNode(next, GetStarNum(role, next))),
            ToNode(route, GetStarNum(role, route)),
        ]),
    };

    public bool IsUnlocked(LMNRoleInfo role, pbPveChallengeStageRoute route) =>
        Load(role).Any(node => node.StageID == route.StageID
            && node.NodeID == route.NodeID
            && node.DiffLevel == route.DifficultyLevel
            && node.IsUnLock);

    public pbSCPveChallengeStageInfoNtf[] CompleteByNewbie(int newbie) =>
    [
        .. routesByNewbie[newbie].Select(route => new pbSCPveChallengeStageInfoNtf
        {
            PveChallengeInfo = BuildInfo([.. NextUnLockRoutes(route).Select(next => ToNode(next, NotCompletedStarNum)), ToNode(route, TutorialBattleStarNum)]),
        }),
    ];

    public static pbSCPveChallengeStageInfoNtf[] BuildNewbieCompletedNodeNtfs(LMNRoleInfo role, IEnumerable<PveChallengeNode> nodes) =>
    [
        .. nodes
            .Where(node => node.IsComplete
                && role.GetPveChallengeStarNum(node.StageID, node.NodeID, node.DiffLevel) == NotCompletedStarNum)
            .Select(node => new pbSCPveChallengeStageInfoNtf
            {
                PveChallengeInfo = BuildInfo([node]),
            }),
    ];

    public static pbCOMDT_PVE_CHALLENGE_INFO BuildInfo(IReadOnlyList<PveChallengeNode> nodes)
    {
        var info = new pbCOMDT_PVE_CHALLENGE_INFO();
        foreach (var stage in nodes.GroupBy(node => node.StageID))
        {
            var stageInfo = new pbCOMDT_PVE_CHALLENGE_STAGE_INFO
            {
                StageID = stage.Key,
                NormalChallengeList = new pbCOMDT_PVE_CHALLENGE_NODE_LIST(),
                HardChallengeList = new pbCOMDT_PVE_CHALLENGE_NODE_LIST(),
            };
            foreach (var node in stage)
            {
                var list = node.DiffLevel == NormalDifficultyLevel ? stageInfo.NormalChallengeList : stageInfo.HardChallengeList;
                list.NodeList.Add(node.ToNodeInfo());
            }

            info.StageList.Add(stageInfo);
        }

        return info;
    }

    private bool IsCompletedByNewbie(LMNRoleInfo role, pbPveChallengeStageRoute route) =>
        route.BattleId != NoBattleId
        && newbieByBattleId.TryGetValue(route.BattleId, out var newbie)
        && role.IsNewbieStateSet(newbie);

    private static bool IsUnlockedFromStart(pbPveChallengeStageRoute route) =>
        route.IsUnLock == UnlockedByTable
        && route.IsNormalType == NormalType
        && route.DifficultyLevel == NormalDifficultyLevel;

    private IEnumerable<pbPveChallengeStageRoute> NextUnLockRoutes(pbPveChallengeStageRoute route) =>
        FindRoutes(route.StageID, route.NextUnLock, route.DifficultyLevel)
            .Concat(FindRoutes(route.StageID, route.NextUnLockHard, HardDifficultyLevel));

    private IEnumerable<pbPveChallengeStageRoute> FindRoutes(int stageID, IEnumerable<int> nodeIDs, int difficultyLevel) =>
        nodeIDs
            .Where(nodeID => nodeID != NoNode)
            .Select(nodeID => FindRoute(stageID, nodeID, difficultyLevel))
            .OfType<pbPveChallengeStageRoute>();

    private static PveChallengeNode ToNode(pbPveChallengeStageRoute route, int starNum) => new(
        route.StageID,
        route.NodeID,
        route.DifficultyLevel,
        starNum,
        IsUnLock: true,
        starNum > NotCompletedStarNum);
}
