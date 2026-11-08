using Microsoft.Extensions.Logging;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.BattleVoice;

public sealed class BattleVoiceManager(ILogger<BattleVoiceManager> logger)
{
    private const int Closed = 0;
    private const int Opened = 1;

    public PB_ENUM_RES_ERROR_CODE OpenVoice(ulong uid, int voiceType)
    {
        if (!IsVoiceType(voiceType))
        {
            logger.LogInformation("Uid {Uid} opened unknown voice type {VoiceType}", uid, voiceType);
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        logger.LogInformation("Uid {Uid} opened voice {VoiceType}", uid, (PB_ENUM_ENM_OPEN_VOICE_TYPE)voiceType);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public PB_ENUM_RES_ERROR_CODE ChangeVoiceStat(ulong uid, int voiceType, int isOpen)
    {
        if (!IsVoiceType(voiceType) || isOpen is not (Closed or Opened))
        {
            logger.LogInformation("Uid {Uid} sent voice state {IsOpen} for voice type {VoiceType}", uid, isOpen, voiceType);
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        logger.LogInformation("Uid {Uid} voice {VoiceType} is open: {IsOpen}", uid, (PB_ENUM_ENM_OPEN_VOICE_TYPE)voiceType, isOpen == Opened);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static bool IsVoiceType(int voiceType) => Enum.IsDefined((PB_ENUM_ENM_OPEN_VOICE_TYPE)voiceType);
}
