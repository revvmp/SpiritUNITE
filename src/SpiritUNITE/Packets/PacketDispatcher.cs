using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets;

public enum eNetEventServerType
{
    LobbyServer = 1,
    RelayServer = 2,
    DirServer = 3,
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PacketHandlerAttribute(eNetEventServerType serverType, PB_ENUM_CS_MSGID_DEF msgID) : Attribute
{
    public eNetEventServerType ServerType { get; } = serverType;
    public int MsgID { get; } = (int)msgID;
}

public sealed class PacketDispatcher
{
    private readonly Dictionary<(eNetEventServerType, int), Func<GamePacket, GamePacket[]>> handlers;

    private PacketDispatcher(Dictionary<(eNetEventServerType, int), Func<GamePacket, GamePacket[]>> handlers)
    {
        this.handlers = handlers;
    }

    public static PacketDispatcher Discover(Assembly assembly, IServiceProvider services)
    {
        var handlers = new Dictionary<(eNetEventServerType, int), Func<GamePacket, GamePacket[]>>();

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

    public bool TryDispatch(eNetEventServerType serverType, GamePacket request, out GamePacket[] replies)
    {
        replies = [];
        if (!handlers.TryGetValue((serverType, request.MsgID), out var handler))
        {
            return false;
        }

        replies = handler(request);
        return true;
    }

    private static void Register(
        Dictionary<(eNetEventServerType, int), Func<GamePacket, GamePacket[]>> handlers,
        MethodInfo method,
        object instance)
    {
        if (method.ReturnType != typeof(GamePacket[])
            || method.GetParameters() is not [{ ParameterType: var parameterType }]
            || parameterType != typeof(GamePacket))
        {
            throw new InvalidOperationException(
                $"Handler {method.DeclaringType?.Name}.{method.Name} must take a GamePacket and return a GamePacket array");
        }

        var handler = method.CreateDelegate<Func<GamePacket, GamePacket[]>>(instance);
        foreach (var attribute in method.GetCustomAttributes<PacketHandlerAttribute>())
        {
            if (!handlers.TryAdd((attribute.ServerType, attribute.MsgID), handler))
            {
                throw new InvalidOperationException(
                    $"Message {attribute.MsgID} on {attribute.ServerType} already has a handler");
            }
        }
    }
}
