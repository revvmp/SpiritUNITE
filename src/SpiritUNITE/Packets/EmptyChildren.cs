using Google.Protobuf;
using Google.Protobuf.Reflection;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets;

public static class EmptyChildren
{
    private static readonly HashSet<MessageDescriptor> TypesSentWithoutChildren = [pbResultInfo.Descriptor];

    public static T Fill<T>(T message)
        where T : IMessage
    {
        Fill(message, []);
        return message;
    }

    private static void Fill(IMessage message, HashSet<MessageDescriptor> enclosingTypes)
    {
        var descriptor = message.Descriptor;
        if (TypesSentWithoutChildren.Contains(descriptor) || !enclosingTypes.Add(descriptor))
        {
            return;
        }

        foreach (var field in descriptor.Fields.InDeclarationOrder())
        {
            if (field.FieldType != FieldType.Message || field.IsRepeated || field.IsMap || field.RealContainingOneof is not null)
            {
                continue;
            }

            if (field.Accessor.GetValue(message) is not IMessage child)
            {
                if (enclosingTypes.Contains(field.MessageType))
                {
                    continue;
                }

                child = field.MessageType.Parser.ParseFrom(ByteString.Empty);
                field.Accessor.SetValue(message, child);
            }

            Fill(child, enclosingTypes);
        }

        enclosingTypes.Remove(descriptor);
    }
}
