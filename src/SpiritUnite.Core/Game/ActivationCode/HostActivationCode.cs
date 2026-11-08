namespace SpiritUnite.Game.ActivationCode;

public sealed record HostActivationCodeEntry(string ActivationCode, long ValidEndTime, int MaxUseCnt, int UseCnt);

public sealed class HostActivationCode
{
    public const long NoValidEndTime = 0;
    public const int UnlimitedUseCnt = 0;

    public string ActivationCode { get; init; } = string.Empty;
    public long ValidEndTime { get; init; } = NoValidEndTime;
    public int MaxUseCnt { get; init; } = UnlimitedUseCnt;

    public bool IsExpired(long svrConfTime) => ValidEndTime != NoValidEndTime && svrConfTime > ValidEndTime;
}
