using System.Net;
using SpiritUnite.Servers.Lobby;

namespace SpiritUnite.Servers.Voice;

public sealed class VoiceServerOptions
{
    public const string SectionName = "VoiceServer";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 8700;

    public bool IsValid() =>
        Host is not null
        && Host == Host.Trim()
        && !Host.Contains(' ')
        && Port is > IPEndPoint.MinPort and <= IPEndPoint.MaxPort;

    public string AdvertisedHost(LobbyAddressOptions lobbyAddress) => Host.Length == 0 ? lobbyAddress.Host : Host;
}
