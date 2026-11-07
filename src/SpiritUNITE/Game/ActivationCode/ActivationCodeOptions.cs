using System.Globalization;

namespace SpiritUNITE.Game.ActivationCode;

public sealed class ActivationCodeOptions
{
    public const string SectionName = "ActivationCode";

    public HostActivationCode[] Codes { get; init; } = [];

    public bool IsValid() =>
        Codes.All(code => code.IsValid())
        && Codes.Select(code => code.ActivationCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() == Codes.Length;
}

public sealed class HostActivationCode
{
    public const long NoValidEndTime = 0;
    public const int UnlimitedUseCnt = 0;

    private const string CfgTimeFormat = "yyyyMMddHHmmss";

    public string ActivationCode { get; init; } = string.Empty;
    public long ValidEndTime { get; init; } = NoValidEndTime;
    public int MaxUseCnt { get; init; } = UnlimitedUseCnt;

    public bool IsValid() =>
        ActivationCodeManager.IsWellFormed(ActivationCode)
        && MaxUseCnt >= UnlimitedUseCnt
        && (ValidEndTime == NoValidEndTime || DateTime.TryParseExact(
            ValidEndTime.ToString(CultureInfo.InvariantCulture),
            CfgTimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out _));

    public bool IsExpired(long svrConfTime) => ValidEndTime != NoValidEndTime && svrConfTime > ValidEndTime;
}
