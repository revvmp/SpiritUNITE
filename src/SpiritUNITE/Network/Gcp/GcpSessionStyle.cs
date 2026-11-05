namespace SpiritUNITE.Network.Gcp;

public sealed record GcpSessionStyle(
    uint FirstHeadSequence,
    bool WritesPkgHeadUidAndSvrPkgSeq,
    bool EchoesAccountInAuthRsp,
    bool SendsBingoSession,
    bool AnswersClientStop)
{
    public static readonly GcpSessionStyle Lobby = new(
        FirstHeadSequence: 0,
        WritesPkgHeadUidAndSvrPkgSeq: true,
        EchoesAccountInAuthRsp: false,
        SendsBingoSession: false,
        AnswersClientStop: false);

    public static readonly GcpSessionStyle Relay = new(
        FirstHeadSequence: 1,
        WritesPkgHeadUidAndSvrPkgSeq: false,
        EchoesAccountInAuthRsp: true,
        SendsBingoSession: true,
        AnswersClientStop: true);
}
