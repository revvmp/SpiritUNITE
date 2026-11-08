namespace SpiritUnite.Databin;

public sealed record pbBanPickRule(
    int BanPickRuleId,
    IReadOnlyList<int> Round,
    int BanMode = 0,
    int ExceptBanCD = 0,
    int VoteCampBanNum = 0,
    int ViewVoteBanNum = 0);
