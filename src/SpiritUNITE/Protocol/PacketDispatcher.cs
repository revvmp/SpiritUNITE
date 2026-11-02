using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace SpiritUNITE.Protocol;

public enum Connector
{
    Dir,
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PacketHandlerAttribute(Connector connector, int messageId) : Attribute
{
    public Connector Connector { get; } = connector;
    public int MessageId { get; } = messageId;
}

public sealed class PacketDispatcher
{
    private readonly Dictionary<(Connector, int), Func<GamePacket, GamePacket?>> handlers;

    private PacketDispatcher(Dictionary<(Connector, int), Func<GamePacket, GamePacket?>> handlers)
    {
        this.handlers = handlers;
    }

    public static PacketDispatcher Discover(Assembly assembly, IServiceProvider services)
    {
        var handlers = new Dictionary<(Connector, int), Func<GamePacket, GamePacket?>>();

        foreach (var type in assembly.GetTypes())
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.IsDefined(typeof(PacketHandlerAttribute)))
                .ToList();
            if (methods.Count == 0)
            {
                continue;
            }

            var instance = ActivatorUtilities.CreateInstance(services, type);
            foreach (var method in methods)
            {
                Register(handlers, method, instance);
            }
        }

        return new PacketDispatcher(handlers);
    }

    public bool TryDispatch(Connector connector, GamePacket request, out GamePacket? reply)
    {
        reply = null;
        if (!handlers.TryGetValue((connector, request.MessageId), out var handler))
        {
            return false;
        }

        reply = handler(request);
        return true;
    }

    private static void Register(
        Dictionary<(Connector, int), Func<GamePacket, GamePacket?>> handlers,
        MethodInfo method,
        object instance)
    {
        if (method.ReturnType != typeof(GamePacket)
            || method.GetParameters() is not [{ ParameterType: var parameterType }]
            || parameterType != typeof(GamePacket))
        {
            throw new InvalidOperationException(
                $"Handler {method.DeclaringType?.Name}.{method.Name} must take and return a GamePacket");
        }

        var handler = method.CreateDelegate<Func<GamePacket, GamePacket?>>(instance);
        foreach (var attribute in method.GetCustomAttributes<PacketHandlerAttribute>())
        {
            if (!handlers.TryAdd((attribute.Connector, attribute.MessageId), handler))
            {
                throw new InvalidOperationException(
                    $"Message {attribute.MessageId} on {attribute.Connector} already has a handler");
            }
        }
    }
}
