using System.Net;

namespace SpiritUNITE.Network;

public sealed class TcpServerOptions
{
    public const string SectionName = "TcpServer";

    public string BindAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 10001;
    public int MaxConnections { get; init; } = 100;
    public int IdleTimeoutSeconds { get; init; } = 60;
    public int RegistrationIdleTimeoutSeconds { get; init; } = 900;

    public bool IsValid() =>
        IPAddress.TryParse(BindAddress, out _)
        && Port is >= IPEndPoint.MinPort + 1 and <= IPEndPoint.MaxPort
        && MaxConnections > 0
        && IdleTimeoutSeconds > 0
        && RegistrationIdleTimeoutSeconds >= IdleTimeoutSeconds;
}
