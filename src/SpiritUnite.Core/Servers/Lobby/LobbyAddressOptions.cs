using System.Net;

namespace SpiritUnite.Servers.Lobby;

public sealed class LobbyAddressOptions
{
    public const string SectionName = "LobbyServer";

    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 10002;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Host)
        && Port is > IPEndPoint.MinPort and <= IPEndPoint.MaxPort;
}
