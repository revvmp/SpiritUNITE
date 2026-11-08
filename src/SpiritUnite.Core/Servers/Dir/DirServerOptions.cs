using System.Net;

namespace SpiritUnite.Servers.Dir;

public sealed class DirServerOptions
{
    public const string SectionName = "DirServer";

    public int Port { get; init; } = 10001;

    public bool IsValid() => Port is > IPEndPoint.MinPort and <= IPEndPoint.MaxPort;
}
