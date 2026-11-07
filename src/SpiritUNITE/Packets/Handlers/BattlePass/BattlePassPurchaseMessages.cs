using Google.Protobuf;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.BattlePass;

public sealed record pbCSBattlePassBuyPassReq(int PassType, IReadOnlyList<int> ResList);

public sealed record pbCSBattlePassPackBuyReq(int PackBuyType);

public static class BattlePassPurchaseMessages
{
    public const PB_ENUM_CS_MSGID_DEF PbCsidBattlePassBuyPassReq = (PB_ENUM_CS_MSGID_DEF)121009;
    public const int PbScidBattlePassBuyPassRsp = 121010;
    public const PB_ENUM_CS_MSGID_DEF PbCsidBattlePassPackBuyReq = (PB_ENUM_CS_MSGID_DEF)121019;
    public const int PbScidBattlePassPackBuyRsp = 121020;

    private const int ReqPassTypeField = 1;
    private const int ReqResListField = 3;
    private const int RspResultInfoField = 1;
    private const int RspPassTypeField = 2;
    private const int RspResListField = 4;
    private const int PackReqPackBuyTypeField = 1;
    private const int PackRspResultInfoField = 1;
    private const int PackRspPackBuyTypeField = 2;

    public static pbCSBattlePassBuyPassReq ParseReq(byte[] pkgData)
    {
        var input = new CodedInputStream(pkgData);
        var passType = 0;
        var resList = new List<int>();
        while (input.ReadTag() is var tag and not 0)
        {
            switch (WireFormat.GetTagFieldNumber(tag), WireFormat.GetTagWireType(tag))
            {
                case (ReqPassTypeField, WireFormat.WireType.Varint):
                    passType = input.ReadInt32();
                    break;
                case (ReqResListField, WireFormat.WireType.Varint):
                    resList.Add(input.ReadInt32());
                    break;
                case (ReqResListField, WireFormat.WireType.LengthDelimited):
                    var packed = new CodedInputStream(input.ReadBytes().ToByteArray());
                    while (!packed.IsAtEnd)
                    {
                        resList.Add(packed.ReadInt32());
                    }

                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return new pbCSBattlePassBuyPassReq(passType, resList);
    }

    public static byte[] BuildRsp(pbResultInfo resultInfo, int passType, IReadOnlyList<int> resList)
    {
        using var buffer = new MemoryStream();
        using (var output = new CodedOutputStream(buffer))
        {
            output.WriteTag(RspResultInfoField, WireFormat.WireType.LengthDelimited);
            output.WriteMessage(resultInfo);
            if (passType != 0)
            {
                output.WriteTag(RspPassTypeField, WireFormat.WireType.Varint);
                output.WriteInt32(passType);
            }

            if (resList.Count > 0)
            {
                output.WriteTag(RspResListField, WireFormat.WireType.LengthDelimited);
                output.WriteLength(resList.Sum(CodedOutputStream.ComputeInt32Size));
                foreach (var resID in resList)
                {
                    output.WriteInt32(resID);
                }
            }
        }

        return buffer.ToArray();
    }

    public static pbCSBattlePassPackBuyReq ParsePackReq(byte[] pkgData)
    {
        var input = new CodedInputStream(pkgData);
        var packBuyType = 0;
        while (input.ReadTag() is var tag and not 0)
        {
            if (tag == WireFormat.MakeTag(PackReqPackBuyTypeField, WireFormat.WireType.Varint))
            {
                packBuyType = input.ReadInt32();
            }
            else
            {
                input.SkipLastField();
            }
        }

        return new pbCSBattlePassPackBuyReq(packBuyType);
    }

    public static byte[] BuildPackRsp(pbResultInfo resultInfo, int packBuyType)
    {
        using var buffer = new MemoryStream();
        using (var output = new CodedOutputStream(buffer))
        {
            output.WriteTag(PackRspResultInfoField, WireFormat.WireType.LengthDelimited);
            output.WriteMessage(resultInfo);
            if (packBuyType != 0)
            {
                output.WriteTag(PackRspPackBuyTypeField, WireFormat.WireType.Varint);
                output.WriteInt32(packBuyType);
            }
        }

        return buffer.ToArray();
    }
}
