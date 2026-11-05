using System.Net;
using SpiritUNITE.Servers.Lobby;

namespace SpiritUNITE.Servers.Relay;

public sealed class RelayAddressOptions
{
    public const string SectionName = "RelayServer";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 10003;

    public bool IsValid() =>
        Host is not null
        && Host == Host.Trim()
        && Port is > IPEndPoint.MinPort and <= IPEndPoint.MaxPort;

    public string AdvertisedHost(LobbyAddressOptions lobbyAddress) => Host.Length == 0 ? lobbyAddress.Host : Host;
}
