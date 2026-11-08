using System.Net;

namespace SpiritUnite.Network;

public sealed class TcpServerOptions
{
    public const string SectionName = "TcpServer";

    public string BindAddress { get; init; } = "127.0.0.1";
    public int MaxConnections { get; init; } = 100;
    public int IdleTimeoutSeconds { get; init; } = 120;
    public int RegistrationIdleTimeoutSeconds { get; init; } = 900;

    public bool IsValid() =>
        IPAddress.TryParse(BindAddress, out _)
        && MaxConnections > 0
        && IdleTimeoutSeconds > 0
        && RegistrationIdleTimeoutSeconds >= IdleTimeoutSeconds;
}
