using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Room;

public sealed class FriendlyRoomRules
{
    public const int MaxFriendlyRoomRuleNum = (int)PB_MACRO_COM_MAX_FRIENDLY_ROOM_RULE_NUM.PbComMaxFriendlyRoomRuleNum;

    private const int NoRule = (int)PB_ENUM_RES_MATCHMAPSUBMODEFRIENDLYRULE.PbMatchMapSubModeFriendlyRuleInvalid;

    public static readonly FriendlyRoomRules None = new([]);

    private FriendlyRoomRules(IReadOnlyList<int> ruleList)
    {
        RuleList = ruleList;
    }

    public IReadOnlyList<int> RuleList { get; }

    public bool IsFullLevel => Has(PB_ENUM_RES_MATCHMAPSUBMODEFRIENDLYRULE.PbMatchMapSubModeFriendlyRuleHolditemLevel30);

    public bool IsFullHeldItem => Has(PB_ENUM_RES_MATCHMAPSUBMODEFRIENDLYRULE.PbMatchMapSubModeFriendlyRuleHolditemAllopen);

    public bool IsAllOpenMode => Has(PB_ENUM_RES_MATCHMAPSUBMODEFRIENDLYRULE.PbMatchMapSubModeFriendlyRulePokemonAllopen);

    public bool IsAllTrainerItem => Has(PB_ENUM_RES_MATCHMAPSUBMODEFRIENDLYRULE.PbMatchMapSubModeFriendlyRuleTraineritemAllopen);

    public bool IsBanpickCampChg => Has(PB_ENUM_RES_MATCHMAPSUBMODEFRIENDLYRULE.PbMatchMapSubModeFriendlyRuleBanpickCampChg);

    public bool IsGlobalBp => Has(PB_ENUM_RES_MATCHMAPSUBMODEFRIENDLYRULE.PbMatchMapSubModeFriendlyRuleGlobalBp);

    public bool KeepsHeldItemsTemporary => IsFullHeldItem || IsAllOpenMode;

    public static IReadOnlyList<int> OfferedBy(pbScene_Map scene) => [.. scene.SubModeFriendRule.Where(rule => rule != NoRule).Distinct()];

    public static FriendlyRoomRules? Link(GameData gameData, pbScene_Map scene, IEnumerable<int> requested)
    {
        var offered = OfferedBy(scene).ToHashSet();
        int[] asked = [.. requested.Where(rule => rule != NoRule)];
        if (asked.Length > MaxFriendlyRoomRuleNum || asked.Any(rule => !offered.Contains(rule)))
        {
            return null;
        }

        var linked = asked.ToHashSet();
        var opening = new Queue<int>(linked);
        while (opening.TryDequeue(out var rule))
        {
            foreach (var related in RelationOf(gameData, rule, open: true).Where(offered.Contains))
            {
                if (linked.Add(related))
                {
                    opening.Enqueue(related);
                }
            }
        }

        foreach (var rule in offered.Where(rule => !linked.Contains(rule)).ToArray())
        {
            linked.ExceptWith(RelationOf(gameData, rule, open: false));
        }

        return new FriendlyRoomRules([.. linked.Order()]);
    }

    public FriendlyRoomRules OfferedOn(pbScene_Map scene)
    {
        var offered = OfferedBy(scene).ToHashSet();
        return new FriendlyRoomRules([.. RuleList.Where(offered.Contains)]);
    }

    public pbCOMDT_FRIENDLY_ROOM_RULE_INFO Build() => new() { RuleList = { RuleList } };

    private bool Has(PB_ENUM_RES_MATCHMAPSUBMODEFRIENDLYRULE rule) => RuleList.Contains((int)rule);

    private static IEnumerable<int> RelationOf(GameData gameData, int rule, bool open) =>
        gameData.FriendlyRoomRule.Find(rule) is { } row
            ? (open ? row.RelationOpen : row.RelationClose).Where(related => related != NoRule)
            : [];
}
