using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Network;
using SpiritUnite.Network.Thrift;
using SpiritUnite.Network.Tuap;

namespace SpiritUnite.Servers.Update;

public sealed class UpdateServer(
    IOptions<UpdateServerOptions> updateServer,
    IOptions<TcpServerOptions> options,
    UpdateServiceReplies replies,
    TimeProvider time,
    ILogger<UpdateServer> updateLogger,
    ILogger<TuapServer> logger)
    : TuapServer(nameof(UpdateServer), updateServer.Value.Port, options, time, logger)
{
    protected override TuapService? FindService(string name) => UpdateServiceReplies.FindService(name);

    protected override Task<byte[]?> HandleRequestAsync(TuapService service, ReadOnlyMemory<byte> payload, EndPoint? remoteEndPoint)
    {
        ThriftMessage call;
        try
        {
            call = ThriftMessage.Read(payload.Span);
        }
        catch (ThriftProtocolException exception)
        {
            updateLogger.LogWarning("Update request on {Service} from {RemoteEndPoint} refused: {Reason}", service.Name, remoteEndPoint, exception.Message);
            return Task.FromResult<byte[]?>(null);
        }

        var result = replies.Answer(service, call, remoteEndPoint);
        return Task.FromResult(result is null ? null : call.ReplyWith(result).Write());
    }
}
