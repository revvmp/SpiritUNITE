using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

public sealed class RefereePause(int campClient, long pausedAt, long pauseGameTimeStamp, long pauseCancelTime)
{
    public int CampClient { get; } = campClient;
    public long PausedAt { get; } = pausedAt;
    public long PauseGameTimeStamp { get; } = pauseGameTimeStamp;
    public long PauseCancelTime { get; } = pauseCancelTime;
    public PB_ENUM_COM_PAUSE_CANCEL_TYPE CancelType { get; private set; } = PB_ENUM_COM_PAUSE_CANCEL_TYPE.PbComPauseCancelType0;
    public int CancelCampClient { get; private set; }
    public long ResumeAt { get; private set; }

    public bool IsCancelled => CancelType != PB_ENUM_COM_PAUSE_CANCEL_TYPE.PbComPauseCancelType0;

    public void Cancel(PB_ENUM_COM_PAUSE_CANCEL_TYPE cancelType, int cancelCampClient, long resumeAt)
    {
        CancelType = cancelType;
        CancelCampClient = cancelCampClient;
        ResumeAt = resumeAt;
    }
}
