using SpiritUnite.Databin;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Profile;

public static class ProfileGainPushes
{
    private const int CanopyGainUpdateType = 1;
    private const int CardFaceGainUpdateType = 2;

    public static IEnumerable<GamePacket> Build(GameData gameData, LMNRoleInfo role, IEnumerable<RoleItemInfo> items)
    {
        RoleItemInfo[] granted = [.. items];
        if (granted.Any(item => item.ItemType == RoleItemType.ContestantCardDecoration))
        {
            yield return GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidProfileGainResNtf,
                new pbSCProfileGainResNtf { ContestantResList = ContestantCard.BuildContestantResList(gameData, role) });
        }

        if (granted.Where(item => item.ItemType == RoleItemType.CardFace).ToArray() is { Length: > 0 } cardFaces)
        {
            yield return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateCardFaceNtf, new pbSCUpdateCardFaceNtf
            {
                CardFaceUpdateList =
                {
                    cardFaces.Select(item => new pbCOMDT_CARD_FACE_UPDATE_NODE
                    {
                        UpdateType = CardFaceGainUpdateType,
                        CardFaceInfo = new pbCOMDT_ONE_CARD_FACE { CardFaceID = item.ItemId, NewTag = item.NewTag },
                    }),
                },
            });
        }

        if (granted.Where(item => item.ItemType == RoleItemType.Canopy).ToArray() is { Length: > 0 } canopies)
        {
            yield return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateCanopyNtf, new pbSCUpdateCanopyNtf
            {
                CanopyUpdateList =
                {
                    canopies.Select(item => new pbCOMDT_CANOPY_UPDATE_NODE
                    {
                        UpdateType = CanopyGainUpdateType,
                        Canopy = ProfileManager.ToCanopyInfo(item),
                    }),
                },
            });
        }

        if (CustomizeManager.BuildPersonaliseButtonGainNtf(granted) is { } personaliseButtonNtf)
        {
            yield return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePersonaliseButtonNtf, personaliseButtonNtf);
        }

        foreach (var gloryAnnounceNtf in CustomizeManager.BuildGloryAnnounceGainNtfs(granted))
        {
            yield return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAddGloryAnnounceNtf, gloryAnnounceNtf);
        }
    }
}
