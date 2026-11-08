using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets;

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
    private readonly Dictionary<(eNetEventServerType, int), Func<GamePacket, Task<GamePacket[]>>> handlers;

    private PacketDispatcher(Dictionary<(eNetEventServerType, int), Func<GamePacket, Task<GamePacket[]>>> handlers)
    {
        this.handlers = handlers;
    }

    public static PacketDispatcher Discover(Assembly assembly, IServiceProvider services)
    {
        var handlers = new Dictionary<(eNetEventServerType, int), Func<GamePacket, Task<GamePacket[]>>>();

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

    public async Task<GamePacket[]?> DispatchAsync(eNetEventServerType serverType, GamePacket request) =>
        handlers.TryGetValue((serverType, request.MsgID), out var handler) ? await handler(request) : null;

    private static void Register(
        Dictionary<(eNetEventServerType, int), Func<GamePacket, Task<GamePacket[]>>> handlers,
        MethodInfo method,
        object instance)
    {
        if (method.GetParameters() is not [{ ParameterType: var parameterType }] || parameterType != typeof(GamePacket))
        {
            throw new InvalidOperationException(
                $"Handler {method.DeclaringType?.Name}.{method.Name} must take a GamePacket");
        }

        var handler = BuildHandler(method, instance);
        foreach (var attribute in method.GetCustomAttributes<PacketHandlerAttribute>())
        {
            if (!handlers.TryAdd((attribute.ServerType, attribute.MsgID), handler))
            {
                throw new InvalidOperationException(
                    $"Message {attribute.MsgID} on {attribute.ServerType} already has a handler");
            }
        }
    }

    private static Func<GamePacket, Task<GamePacket[]>> BuildHandler(MethodInfo method, object instance)
    {
        if (method.ReturnType == typeof(Task<GamePacket[]>))
        {
            return method.CreateDelegate<Func<GamePacket, Task<GamePacket[]>>>(instance);
        }

        if (method.ReturnType == typeof(GamePacket[]))
        {
            var handler = method.CreateDelegate<Func<GamePacket, GamePacket[]>>(instance);
            return request => Task.FromResult(handler(request));
        }

        throw new InvalidOperationException(
            $"Handler {method.DeclaringType?.Name}.{method.Name} must return GamePacket[] or Task<GamePacket[]>");
    }
}
