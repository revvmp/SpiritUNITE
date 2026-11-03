using System.Net;
using System.Net.Sockets;

namespace SpiritUNITE.Network;

public static class ClientAddress
{
    private const string UnknownClientAddress = "unknown";
    private const int Ipv6SubnetPrefixBytes = 8;

    public static string Partition(IPAddress? address)
    {
        if (address is null)
        {
            return UnknownClientAddress;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6
            ? Convert.ToHexString(address.GetAddressBytes(), 0, Ipv6SubnetPrefixBytes)
            : address.ToString();
    }
}
