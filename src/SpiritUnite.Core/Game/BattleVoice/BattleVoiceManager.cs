using Microsoft.Extensions.Logging;
using SpiritUnite.Databin;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.BattleVoice;

public sealed class BattleVoiceManager(GameData gameData, ILogger<BattleVoiceManager> logger)
{
    public const int MinGvoiceRoomMembers = 2;

    private const int Closed = 0;
    private const int Opened = 1;
    private const int VoiceDisabled = 0;

    private readonly Lock entitlementGate = new();
    private readonly Dictionary<GVoiceRoomName, HashSet<ulong>> entitledUids = [];

    public event Action<ulong, GVoiceRoomName>? GvoiceRoomRevoked;

    public bool IsVoiceSysAvailable(LMNRoleInfo role, bool isGuestAccount) =>
        !isGuestAccount
        && gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalVoiceEnable) != VoiceDisabled
        && RoleProfile.GetLevel(gameData, role) >= gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResSystemUnlockTypeChatSystem);

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

    public void GrantGvoiceRoom(ulong uid, GVoiceRoomName room)
    {
        lock (entitlementGate)
        {
            if (!entitledUids.TryGetValue(room, out var uids))
            {
                uids = [];
                entitledUids[room] = uids;
            }

            uids.Add(uid);
        }
    }

    public bool IsGvoiceRoomEntitled(ulong uid, GVoiceRoomName room)
    {
        lock (entitlementGate)
        {
            return entitledUids.TryGetValue(room, out var uids) && uids.Contains(uid);
        }
    }

    public void RevokeGvoiceRoom(ulong uid, GVoiceRoomName room)
    {
        bool revoked;
        lock (entitlementGate)
        {
            revoked = entitledUids.TryGetValue(room, out var uids) && uids.Remove(uid);
            if (revoked && uids!.Count == 0)
            {
                entitledUids.Remove(room);
            }
        }

        if (revoked)
        {
            GvoiceRoomRevoked?.Invoke(uid, room);
        }
    }

    public void RevokeGvoiceRoom(GVoiceRoomName room)
    {
        HashSet<ulong>? uids;
        lock (entitlementGate)
        {
            entitledUids.Remove(room, out uids);
        }

        foreach (var uid in uids ?? [])
        {
            GvoiceRoomRevoked?.Invoke(uid, room);
        }
    }

    public void CloseGvoiceRoom(GVoiceRoomName room)
    {
        lock (entitlementGate)
        {
            entitledUids.Remove(room);
        }
    }

    private static bool IsVoiceType(int voiceType) => Enum.IsDefined((PB_ENUM_ENM_OPEN_VOICE_TYPE)voiceType);
}
