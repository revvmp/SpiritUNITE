using System.Buffers.Binary;
using System.Net.Sockets;

namespace SpiritUNITE.Network;

public static class TcpRtt
{
    private const int WindowsSioTcpInfo = unchecked((int)0xD8000027);
    private const int WindowsTcpInfoVersion = 0;
    private const int WindowsTcpInfoV0Size = 88;
    private const int WindowsRttUsOffset = 20;

    private const int LinuxIpProtoTcp = 6;
    private const int LinuxTcpInfo = 11;
    private const int LinuxTcpInfoSize = 104;
    private const int LinuxRttUsOffset = 68;

    private const int MicrosecondsPerTick = 10;

    public static TimeSpan? Measure(Socket socket)
    {
        try
        {
            return OperatingSystem.IsWindows() ? MeasureWindows(socket)
                : OperatingSystem.IsLinux() ? MeasureLinux(socket)
                : null;
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static TimeSpan? MeasureWindows(Socket socket)
    {
        var version = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(version, WindowsTcpInfoVersion);
        var info = new byte[WindowsTcpInfoV0Size];
        var written = socket.IOControl(WindowsSioTcpInfo, version, info);
        return ReadRttUs(info.AsSpan(0, written), WindowsRttUsOffset);
    }

    private static TimeSpan? MeasureLinux(Socket socket)
    {
        var info = new byte[LinuxTcpInfoSize];
        var written = socket.GetRawSocketOption(LinuxIpProtoTcp, LinuxTcpInfo, info);
        return ReadRttUs(info.AsSpan(0, written), LinuxRttUsOffset);
    }

    private static TimeSpan? ReadRttUs(ReadOnlySpan<byte> info, int offset)
    {
        if (info.Length < offset + sizeof(uint))
        {
            return null;
        }

        var rttUs = BinaryPrimitives.ReadUInt32LittleEndian(info[offset..]);
        return rttUs == 0 ? null : TimeSpan.FromTicks((long)rttUs * MicrosecondsPerTick);
    }
}
