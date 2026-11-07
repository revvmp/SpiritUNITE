using SpiritUNITE.Databin;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Match;

public sealed record CpuMatchFillerRules(
    int RankedMatchUnlockLevel,
    int RankedSeasonId,
    FakePlayerWinPointRange? RankedWinPoint,
    IReadOnlyList<int> PartyRankedDivisions);

public sealed record FakePlayerSeat(pbAiInfoConfig Row, int PosOfCamp, int AnonymousUserNameID);

public sealed record MatchSuccRequest(
    int MapId,
    int CampPlayerNum,
    int ConfirmTimeoutMs,
    long SuccTimeStamp,
    int TeamMMRScore,
    CpuMatchFillerRules FillerRules);

public sealed record MatchCamps(
    IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> Left,
    IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> Right,
    bool AgainstBots,
    bool FillWithFakePlayers);

public static class CpuMatch
{
    public const int ManMachine = 1;

    public const int WarmBattle = 1;

    private const int FakePlayerFillAILevel = (int)PB_ENUM_RES_AI_DIFFICULTYLEVEL_TYPE.PbResAiDifficultylevelTypeWarmbattle;
    private const int SoloPartyFakePlayerLevel = 1;
    private const int NoFakePlayerLevel = 0;
    private const int SoloPartySize = 1;
    private const int NoNpcType = 0;
    private const int PkmavatarHeadType = (int)PB_ENUM_ENUM_SOCIAL_HEAD_TYPE.PbEnumSocialHeadTypePkmavatar;
    private const int PkmavatarHeadPercent = 35;
    private const int Percent = 100;
    private const int OpenHeadFrame = 1;
    private const int BotLevel = (int)PB_ENUM_RES_AI_DIFFICULTYLEVEL_TYPE.PbResAiDifficultylevelTypeEasy;
    private const int TimeoutWarmBattleType = (int)PB_ENUM_ENM_WARMBATTLE_TYPE.PbEnmWarmbattleTimeout;
    private const int TimeoutWarmBattleAILevel = (int)PB_ENUM_RES_AI_DIFFICULTYLEVEL_TYPE.PbResAiDifficultylevelTypeEasy;
    private const int NpcConfirmStepMs = 500;
    private const int FirstNpcConfirmStep = 1;
    private const int LastNpcConfirmStep = 20;
    private const int NoFriendShipLevel = 0;
    private const int LowestFakePlayerAcntLevel = 2;
    private const int HighestFakePlayerAcntLevel = 65;
    private const int NoAvatarItemID = 0;
    private const int BpLevelPercent = 30;
    private const int LowestFakePlayerBpLevel = 2;
    private const int HighestFakePlayerBpLevel = 3;
    private const int PokemonEffectPercent = 64;
    private const int FirstEffectLevel = 2;
    private const int CurrentSeasonWinPointPercent = 72;
    private const int CurrentSeasonOnlyPercent = 6;
    private const int EarlierSeasonPercent = 11;
    private const int FirstSeasonId = 1;
    private const int MaxSeasonsBack = 17;
    private const int PlacedWinPointPercent = 78;
    private const int PlacedDivisionOnlyPercent = 11;
    private const int PlacedMasterPointPercent = 7;
    private const int MaxFakeMasterPoint = 316;
    private const int MaxCardPosX = 12000;
    private const int MaxCardPosY = 21000;

    private static readonly int[] EffectCountPercents = [52, 36, 7, 5];

    public static bool IsCpuMatch(MatchRoom room) =>
        room.RoomType == PB_ENUM_COM_ROOM_TYPE.PbComRoomTypeMatch && room.ManMachine == ManMachine;

    public static BotMatchSucc WarmMatchSucc(GameData gameData, MatchSuccRequest request, IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> members, int warmBattleType)
    {
        var npcConfigs = new Dictionary<ulong, pbAiInfoConfig>();
        var fakeRows = GenericRows(gameData).Take(Math.Max(0, (2 * request.CampPlayerNum) - members.Count)).ToArray();
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
            var fakeCount = Math.Max(0, request.CampPlayerNum - campHumans.Count);
            var firstHumanSeat = campHumans.Count > 0 ? Random.Shared.Next(fakeCount + 1) : fakeCount;
            for (var posOfCamp = 0; posOfCamp < fakeCount + campHumans.Count; posOfCamp++)
            {
                var humanIndex = posOfCamp - firstHumanSeat;
                if (humanIndex >= 0 && humanIndex < campHumans.Count)
                {
                    camp.MemInfo.Add(SeatHuman(gameData, campHumans[humanIndex], posOfCamp, ++anonymousUserNameID, request.TeamMMRScore));
                }
                else if (nextRow < fakeRows.Length)
                {
                    camp.MemInfo.Add(BuildFakePlayer(
                        gameData,
                        new FakePlayerSeat(fakeRows[nextRow++], posOfCamp, ++anonymousUserNameID),
                        npcConfigs,
                        takenUids,
                        titles,
                        request.FillerRules,
                        reservedUidCount: 0,
                        SoloPartyFakePlayerLevel));
                }
            }

            memInfo.CampMem.Add(camp);
        }

        var matchRsp = PveMatch.BuildMatchSuccRsp(
            request.MapId,
            WarmBattleAILevel(gameData, warmBattleType),
            memInfo,
            request.SuccTimeStamp,
            PveMatch.ReadyConfirmUiType,
            request.ConfirmTimeoutMs);
        matchRsp.MatchResult.MatchResDetail.MatchSucc.RoomInfo.IsWarmBattle = WarmBattle;
        return new BotMatchSucc(matchRsp, npcConfigs, warmBattleType);
    }

    private static int WarmBattleAILevel(GameData gameData, int warmBattleType) =>
        warmBattleType == TimeoutWarmBattleType
            ? TimeoutWarmBattleAILevel
            : gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalFirstnWarmbattleDifficulty);

    public static BotMatchSucc MatchSucc(GameData gameData, MatchSuccRequest request, MatchCamps camps)
    {
        var npcConfigs = new Dictionary<ulong, pbAiInfoConfig>();
        var genericRows = GenericRows(gameData).ToArray();
        var botRows = camps.AgainstBots ? genericRows.Take(request.CampPlayerNum).ToArray() : [];
        var fakeRows = new Queue<pbAiInfoConfig>(genericRows.Skip(botRows.Length));
        var titles = PveMatch.BuildTitles(gameData);
        var takenUids = camps.Left.Concat(camps.Right).Select(member => member.MemberDetail.MemberOfAcnt.Uid).ToHashSet();
        var fakePlayerLevel = camps.Left.Count + camps.Right.Count == SoloPartySize ? SoloPartyFakePlayerLevel : NoFakePlayerLevel;
        var anonymousUserNameID = 0;

        pbCOMDT_MULTROOMMEMBER_CAMP SeatCamp(int campClient, IReadOnlyList<pbCOMDT_ROOMMEMBER_DT> humans)
        {
            var camp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = campClient };
            foreach (var member in humans)
            {
                camp.MemInfo.Add(SeatHuman(gameData, member, camp.MemInfo.Count, ++anonymousUserNameID, request.TeamMMRScore));
            }

            while (camps.FillWithFakePlayers && camp.MemInfo.Count < request.CampPlayerNum && fakeRows.TryDequeue(out var row))
            {
                camp.MemInfo.Add(BuildFakePlayer(
                    gameData,
                    new FakePlayerSeat(row, camp.MemInfo.Count, ++anonymousUserNameID),
                    npcConfigs,
                    takenUids,
                    titles,
                    request.FillerRules,
                    botRows.Length,
                    fakePlayerLevel));
            }

            return camp;
        }

        var memInfo = new pbCOMDT_MULTROOMMEMBER_INFO();
        memInfo.CampMem.Add(new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.JudgeCamp });
        memInfo.CampMem.Add(SeatCamp(MatchRoom.LeftCamp, camps.Left));
        if (camps.AgainstBots)
        {
            var botCamp = new pbCOMDT_MULTROOMMEMBER_CAMP { CampClient = MatchRoom.RightCamp };
            var botUid = 0UL;
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

            memInfo.CampMem.Add(botCamp);
        }
        else
        {
            memInfo.CampMem.Add(SeatCamp(MatchRoom.RightCamp, camps.Right));
        }

        var matchRsp = PveMatch.BuildMatchSuccRsp(
            request.MapId,
            camps.AgainstBots ? gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalMachineMatchAiDifficuty) : FakePlayerFillAILevel,
            memInfo,
            request.SuccTimeStamp,
            PveMatch.ReadyConfirmUiType,
            request.ConfirmTimeoutMs);
        return new BotMatchSucc(matchRsp, npcConfigs);
    }

    public static TimeSpan PickNpcConfirmDelay() =>
        TimeSpan.FromMilliseconds(NpcConfirmStepMs * Random.Shared.Next(FirstNpcConfirmStep, LastNpcConfirmStep + 1));

    private static IEnumerable<pbAiInfoConfig> GenericRows(GameData gameData) =>
        gameData.AiInfoConfig.Rows.Where(row => row.NPCType == PveMatch.GenericNpcType).OrderBy(row => row.ID);

    private static pbCOMDT_ROOMMEMBER_DT SeatHuman(
        GameData gameData,
        pbCOMDT_ROOMMEMBER_DT member,
        int posOfCamp,
        int anonymousUserNameID,
        int teamMMRScore)
    {
        var seated = PveMatch.SeatHuman(gameData, member, posOfCamp, anonymousUserNameID);
        if (seated.MemberDetail?.MemberOfAcnt is { } acnt)
        {
            acnt.TeamMMRScore = teamMMRScore;
        }

        return seated;
    }

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
        var wornAvatarItemIDs = detail.AvatarInfo.AvatarIDList.Where(avatarItemID => avatarItemID != NoAvatarItemID).ToArray();
        detail.AvatarInfo.AvatarIDList.Clear();
        detail.AvatarInfo.AvatarIDList.Add(wornAvatarItemIDs);
        if (Random.Shared.Next(Percent) < BpLevelPercent)
        {
            detail.BpLevel = Random.Shared.Next(LowestFakePlayerBpLevel, HighestFakePlayerBpLevel + 1);
        }

        detail.Effect = BuildFakeEffects(gameData);
        detail.PokemonEffect = BuildFakePokemonEffect(gameData);
        detail.RankedDivision = fillerRules.RankedWinPoint is { } winPoint
            ? fillerRules.PartyRankedDivisions.Count > 0
                ? BuildPlacedRankedFakePlayerDivision(winPoint, fillerRules.RankedSeasonId, fillerRules.PartyRankedDivisions)
                : BuildRankedFakePlayerDivision(winPoint, fillerRules.RankedSeasonId)
            : detail.AcntLevel >= fillerRules.RankedMatchUnlockLevel
                ? new pbCOMDT_RANKED_DIVISION { SeasonId = fillerRules.RankedSeasonId }
                : new pbCOMDT_RANKED_DIVISION();
        detail.ContestantCameraDis = PickGlobalRange(
            gameData,
            PB_ENUM_RES_RESGLOBAL.PbResglobalFakeAcntCardCameraDisBegin,
            PB_ENUM_RES_RESGLOBAL.PbResglobalFakeAcntCardCameraDisEnd);
        detail.ContestantDir = PickGlobalRange(
            gameData,
            PB_ENUM_RES_RESGLOBAL.PbResglobalFakeAcntCardDirBegin,
            PB_ENUM_RES_RESGLOBAL.PbResglobalFakeAcntCardDirEnd);
    }

    private static pbCOMDT_RANKED_DIVISION BuildPlacedRankedFakePlayerDivision(FakePlayerWinPointRange winPoint, int seasonId, IReadOnlyList<int> rankedDivisions)
    {
        var rankedDivision = rankedDivisions[Random.Shared.Next(rankedDivisions.Count)];
        var roll = Random.Shared.Next(Percent);
        return roll switch
        {
            < PlacedWinPointPercent => new pbCOMDT_RANKED_DIVISION
            {
                RankedDivision = rankedDivision,
                WinPoint = Random.Shared.Next(winPoint.Min, winPoint.Max + 1),
                SeasonId = seasonId,
            },
            < PlacedWinPointPercent + PlacedDivisionOnlyPercent => new pbCOMDT_RANKED_DIVISION { RankedDivision = rankedDivision },
            < PlacedWinPointPercent + PlacedDivisionOnlyPercent + PlacedMasterPointPercent => new pbCOMDT_RANKED_DIVISION
            {
                RankedDivision = rankedDivision,
                SeasonId = seasonId,
                MasterPoint = Random.Shared.Next(MaxFakeMasterPoint + 1),
            },
            _ => new pbCOMDT_RANKED_DIVISION { RankedDivision = rankedDivision, SeasonId = seasonId },
        };
    }

    private static pbCOMDT_RANKED_DIVISION BuildRankedFakePlayerDivision(FakePlayerWinPointRange winPoint, int seasonId)
    {
        var roll = Random.Shared.Next(Percent);
        return roll switch
        {
            < CurrentSeasonWinPointPercent => new pbCOMDT_RANKED_DIVISION
            {
                WinPoint = Random.Shared.Next(winPoint.Min, winPoint.Max + 1),
                SeasonId = seasonId,
            },
            < CurrentSeasonWinPointPercent + CurrentSeasonOnlyPercent => new pbCOMDT_RANKED_DIVISION { SeasonId = seasonId },
            < CurrentSeasonWinPointPercent + CurrentSeasonOnlyPercent + EarlierSeasonPercent => new pbCOMDT_RANKED_DIVISION
            {
                SeasonId = Math.Max(FirstSeasonId, seasonId - Random.Shared.Next(1, MaxSeasonsBack + 1)),
            },
            _ => new pbCOMDT_RANKED_DIVISION(),
        };
    }

    private static pbCOMDT_CONTESTANT_EFFECT_LIST BuildFakeEffects(GameData gameData)
    {
        var effects = new pbCOMDT_CONTESTANT_EFFECT_LIST();
        var decorations = DecorationsOf(gameData, ContestantCard.EffectType);
        var effectCount = PickEffectCount();
        foreach (var (decorationID, index) in decorations.OrderBy(_ => Random.Shared.Next()).Take(effectCount).Select((decorationID, index) => (decorationID, index)))
        {
            effects.EffectInfoList.Add(new pbCOMDT_CONTESTANT_EFFECT_INFO
            {
                ResId = decorationID,
                Position = PickCardPosition(),
                Level = FirstEffectLevel + index,
            });
        }

        return effects;
    }

    private static pbCOMDT_CONTESTANT_POKEMON_EFFECT_INFO BuildFakePokemonEffect(GameData gameData)
    {
        var decorations = DecorationsOf(gameData, ContestantCard.PokemonEffectType);
        return decorations.Length > 0 && Random.Shared.Next(Percent) < PokemonEffectPercent
            ? new pbCOMDT_CONTESTANT_POKEMON_EFFECT_INFO { ResId = decorations[Random.Shared.Next(decorations.Length)], Position = PickCardPosition() }
            : new pbCOMDT_CONTESTANT_POKEMON_EFFECT_INFO { Position = new pbCOMDT_TRAINER_IMAGE_POSITION() };
    }

    private static int[] DecorationsOf(GameData gameData, int decorationType) =>
        [.. gameData.ContestantCardDecoration.Rows.Where(row => row.DecorationType == decorationType).Select(row => row.DecorationID)];

    private static int PickEffectCount()
    {
        var roll = Random.Shared.Next(Percent);
        for (var effectCount = 0; effectCount < EffectCountPercents.Length; effectCount++)
        {
            roll -= EffectCountPercents[effectCount];
            if (roll < 0)
            {
                return effectCount;
            }
        }

        return 0;
    }

    private static pbCOMDT_TRAINER_IMAGE_POSITION PickCardPosition() => new()
    {
        PosX = Random.Shared.Next(-MaxCardPosX, MaxCardPosX + 1),
        PosY = Random.Shared.Next(-MaxCardPosY, MaxCardPosY + 1),
    };

    private static int PickGlobalRange(GameData gameData, PB_ENUM_RES_RESGLOBAL begin, PB_ENUM_RES_RESGLOBAL end) =>
        Random.Shared.Next(gameData.GetGlobalIntData(begin), gameData.GetGlobalIntData(end) + 1);

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
