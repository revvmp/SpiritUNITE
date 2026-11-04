using SpiritUNITE.Packets;

namespace SpiritUNITE.Network.Gcp;

public sealed class GcpSessionHooks
{
    public static readonly GcpSessionHooks None = new();

    public Func<ulong, GcpSession, Task> LoginCompleted { get; init; } = (_, _) => Task.CompletedTask;

    public Func<ulong, GcpSession, Task> SessionClosed { get; init; } = (_, _) => Task.CompletedTask;

    public Func<ulong, IReadOnlyList<GamePacket>, Task> RepliesSending { get; init; } = (_, _) => Task.CompletedTask;

    public Func<ulong, int?> FindNextSvrPkgSeq { get; init; } = _ => null;
}
