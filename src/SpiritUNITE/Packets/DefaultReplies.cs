using Google.Protobuf;
using Google.Protobuf.Reflection;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets;

public static class DefaultReplies
{
    private const string RequestPrefix = "PbCsid";
    private const string RequestSuffix = "Req";
    private const string ResponsePrefix = "PbScid";
    private const string ResponseSuffix = "Rsp";

    public static GamePacket? TryBuild(GamePacket request)
    {
        var requestName = ((PB_ENUM_CS_MSGID_DEF)request.MsgID).ToString();
        if (!requestName.StartsWith(RequestPrefix) || !requestName.EndsWith(RequestSuffix))
        {
            return null;
        }

        var operation = requestName[RequestPrefix.Length..^RequestSuffix.Length];
        if (!Enum.TryParse<PB_ENUM_CS_MSGID_DEF>(ResponsePrefix + operation + ResponseSuffix, out var responseId))
        {
            return null;
        }

        var field = pbCSPkgBody.Descriptor.FindFieldByNumber((int)responseId);
        if (field?.FieldType != FieldType.Message)
        {
            return null;
        }

        return request.Reply(responseId, BuildWithEmptyChildren(field.MessageType));
    }

    private static IMessage BuildWithEmptyChildren(MessageDescriptor descriptor)
    {
        var message = descriptor.Parser.ParseFrom(ByteString.Empty);

        foreach (var field in descriptor.Fields.InDeclarationOrder())
        {
            if (field.FieldType == FieldType.Message && !field.IsRepeated && !field.IsMap)
            {
                field.Accessor.SetValue(message, field.MessageType.Parser.ParseFrom(ByteString.Empty));
            }
        }

        return message;
    }
}
