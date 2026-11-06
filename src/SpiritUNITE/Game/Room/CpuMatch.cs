using SpiritUNITE.Databin;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed record CpuMatchFillerRules(int RankedMatchUnlockLevel, int RankedSeasonId);

public sealed record FakePlayerSeat(pbAiInfoConfig Row, int PosOfCamp, int AnonymousUserNameID);

public static class CpuMatch
{
    public const int ManMachine = 1;

    public const int WarmBattle = 1;

    private const int AILevel = 1;
    private const int SoloPartyFakePlayerLevel = 1;
    private const int NoFakePlayerLevel = 0;
    private const int SoloPartySize = 1;
    private const int NoNpcType = 0;
    private const int PkmavatarHeadType = (int)PB_ENUM_ENUM_SOCIAL_HEAD_TYPE.PbEnumSocialHeadTypePkmavatar;
    private const int PkmavatarHeadPercent = 35;
    private const int Percent = 100;
    private const int OpenHeadFrame = 1;
    private const int BotLevel = 2;
    private const int NpcConfirmStepMs = 500;
    private const int FirstNpcConfirmStep = 1;
    private const int LastNpcConfirmStep = 20;
    private const int NoFriendShipLevel = 0;
    private const int LowestFakePlayerAcntLevel = 2;
    private const int HighestFakePlayerAcntLevel = 54;
    private const int NearestContestantCameraDis = 2;
    private const int FarthestContestantCameraDis = 48;
    private const int LeftmostContestantDir = -49;
    private const int RightmostContestantDir = 46;

    public static bool IsCpuMatch(MatchRoom room) =>
        room.RoomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch && room.ManMachine == ManMachine;

    public static BotMatchSucc WarmMatchSucc(
        GameData gameData,
        MatchRoom room,
        IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> members,
        int teamMMRScore,
        CpuMatchFillerRules fillerRules,
        long succTimeStamp)
    {
        var npcConfigs = new Dictionary<ulong, pbAiInfoConfig>();
        var fakeRows = gameData.AiInfoConfig.Rows
            .Where(row => row.NPCType == PveMatch.GenericNpcType)
            .OrderBy(row => row.ID)
            .Take(Math.Max(0, (2 * room.CampPlayerNum) - members.Count))
            .ToArray();
        var titles = PveMatch.BuildTitles(gameData);
        var takenUids = members.Select(member => member.MemberDetail.MemberOfAcnt.Uid).ToHashSet();
        var anonymousUserNameID = 0;
        var nextRow = 0;
        var memInfo = new pbCOMDT_MULTROOMMEMBER_INFO();
        memInfo.CampMem.Add(new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.JudgeCamp });
        foreach (var campClient in new[] { MatchRoom.LeftCamp, MatchRoom.RightCamp })
        {
            var camp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = campClient };
            var campHumans = campClient == MatchRoom.LeftCamp ? members : [];
            var fakeCount = Math.Max(0, room.CampPlayerNum - campHumans.Count);
            for (var posOfCamp = 0; posOfCamp < fakeCount && nextRow < fakeRows.Length; posOfCamp++)
            {
                camp.MemInfo.Add(BuildFakePlayer(
                    gameData,
                    new FakePlayerSeat(fakeRows[nextRow++], posOfCamp, ++anonymousUserNameID),
                    npcConfigs,
                    takenUids,
                    titles,
                    fillerRules,
                    reservedUidCount: 0,
                    SoloPartyFakePlayerLevel));
            }

            foreach (var member in campHumans)
            {
                var seated = PveMatch.SeatHuman(gameData, member, camp.MemInfo.Count, ++anonymousUserNameID);
                if (seated.MemberDetail?.MemberOfAcnt is { } acnt)
                {
                    acnt.TeamMMRScore = teamMMRScore;
                }

                camp.MemInfo.Add(seated);
            }

            memInfo.CampMem.Add(camp);
        }

        var matchRsp = PveMatch.BuildMatchSuccRsp(room.MapId, AILevel, memInfo, succTimeStamp, PveMatch.ReadyConfirmUiType);
        matchRsp.MatchResult.MatchResDetail.MatchSucc.RoomInfo.IsWarmBattle = WarmBattle;
        return new BotMatchSucc(matchRsp, npcConfigs);
    }

    public static BotMatchSucc MatchSucc(
        GameData gameData,
        MatchRoom room,
        IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> members,
        int teamMMRScore,
        CpuMatchFillerRules fillerRules,
        long succTimeStamp)
    {
        var npcConfigs = new Dictionary<ulong, pbAiInfoConfig>();
        var genericRows = gameData.AiInfoConfig.Rows
            .Where(row => row.NPCType == PveMatch.GenericNpcType)
            .OrderBy(row => row.ID)
            .ToArray();
        var botRows = genericRows.Take(room.CampPlayerNum).ToArray();
        var fakePlayerRows = genericRows.Skip(botRows.Length).Take(Math.Max(0, room.CampPlayerNum - members.Count)).ToArray();
        var titles = PveMatch.BuildTitles(gameData);
        var anonymousUserNameID = 0;

        var partyCamp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.LeftCamp };
        foreach (var member in members)
        {
            var seated = PveMatch.SeatHuman(gameData, member, partyCamp.MemInfo.Count, ++anonymousUserNameID);
            if (seated.MemberDetail?.MemberOfAcnt is { } acnt)
            {
                acnt.TeamMMRScore = teamMMRScore;
            }

            partyCamp.MemInfo.Add(seated);
        }

        var takenUids = members.Select(member => member.MemberDetail.MemberOfAcnt.Uid).ToHashSet();
        var botUid = 0UL;
        var fakePlayerLevel = members.Count == SoloPartySize ? SoloPartyFakePlayerLevel : NoFakePlayerLevel;
        foreach (var row in fakePlayerRows)
        {
            partyCamp.MemInfo.Add(BuildFakePlayer(
                gameData,
                new FakePlayerSeat(row, partyCamp.MemInfo.Count, ++anonymousUserNameID),
                npcConfigs,
                takenUids,
                titles,
                fillerRules,
                botRows.Length,
                fakePlayerLevel));
        }

        var botCamp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.RightCamp };
        foreach (var row in botRows)
        {
            botCamp.MemInfo.Add(PveMatch.BuildNpc(
                gameData,
                row,
                botCamp.MemInfo.Count,
                PveMatch.RememberNpc(npcConfigs, ++botUid, row),
                ++anonymousUserNameID,
                titles,
                BotLevel));
        }

        var memInfo = new pbCOMDT_MULTROOMMEMBER_INFO();
        memInfo.CampMem.Add(new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.JudgeCamp });
        memInfo.CampMem.Add(partyCamp);
        memInfo.CampMem.Add(botCamp);
        var matchRsp = PveMatch.BuildMatchSuccRsp(room.MapId, AILevel, memInfo, succTimeStamp, PveMatch.ReadyConfirmUiType);
        return new BotMatchSucc(matchRsp, npcConfigs);
    }

    public static TimeSpan PickNpcConfirmDelay() =>
        TimeSpan.FromMilliseconds(NpcConfirmStepMs * Random.Shared.Next(FirstNpcConfirmStep, LastNpcConfirmStep + 1));

    private static pbCOMDT_ROOMMEMBER_DT BuildFakePlayer(
        GameData gameData,
        FakePlayerSeat seat,
        Dictionary<ulong, pbAiInfoConfig> npcConfigs,
        HashSet<ulong> takenUids,
        IReadOnlyDictionary<int, string> titles,
        CpuMatchFillerRules fillerRules,
        int reservedUidCount,
        int fakePlayerLevel)
    {
        var fakePlayer = PveMatch.BuildNpc(
            gameData,
            seat.Row,
            seat.PosOfCamp,
            PveMatch.RememberNpc(npcConfigs, PickFakePlayerUid(takenUids, reservedUidCount), seat.Row),
            seat.AnonymousUserNameID,
            titles,
            fakePlayerLevel);
        DressFakePlayer(gameData, fakePlayer.MemberDetail.MemberOfNpc.Detail, fillerRules);
        return fakePlayer;
    }

    private static void DressFakePlayer(GameData gameData, pbCOMDT_FAKEACNT_DETAIL detail, CpuMatchFillerRules fillerRules)
    {
        detail.NPCType = NoNpcType;
        detail.FriendShipLevel = NoFriendShipLevel;
        if (Random.Shared.Next(Percent) < PkmavatarHeadPercent && PickOpenHeadFrame(gameData) is { } headIconID)
        {
            detail.UserPlantInfo = new pbCOMDT_ROLE_HEAD_URL_INFO
            {
                HeadType = PkmavatarHeadType,
                CurPersonStyleInfo = new pbCOMDT_PLAYER_PERSON_STYLE_CUR_INFO { CurHeadResID = headIconID },
                NPSetting = new pbCOMDT_NOBLE_PREVILEG_GAME_SETTING_INFO(),
            };
        }

        detail.AcntLevel = Random.Shared.Next(LowestFakePlayerAcntLevel, HighestFakePlayerAcntLevel + 1);
        detail.RankedDivision = detail.AcntLevel >= fillerRules.RankedMatchUnlockLevel
            ? new pbCOMDT_RANKED_DIVISION { SeasonId = fillerRules.RankedSeasonId }
            : new pbCOMDT_RANKED_DIVISION();
        detail.ContestantCameraDis = Random.Shared.Next(NearestContestantCameraDis, FarthestContestantCameraDis + 1);
        detail.ContestantDir = Random.Shared.Next(LeftmostContestantDir, RightmostContestantDir + 1);
    }

    private static int? PickOpenHeadFrame(GameData gameData)
    {
        var open = gameData.PersonStyleHeadFrame.Rows.Where(row => row.IsOpen == OpenHeadFrame).ToArray();
        return open.Length == 0 ? null : open[Random.Shared.Next(open.Length)].HeadIconID;
    }

    private static ulong PickFakePlayerUid(HashSet<ulong> takenUids, int botCount)
    {
        ulong uid;
        do
        {
            uid = (ulong)Random.Shared.NextInt64(long.MaxValue);
        }
        while (uid <= (ulong)botCount || !takenUids.Add(uid));

        return uid;
    }
}
