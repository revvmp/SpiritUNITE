using System.Security.Cryptography;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Room;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

public enum DeskPhase
{
    Picking,
    WaitingRelay,
    Relay,
}

public sealed class DeskPlayer(
    ulong uid,
    bool isNpc,
    pbCSDT_CAMPPLAYERINFO campPlayerInfo,
    Dictionary<int, pbCOMDT_CHOICEHERO> usableHeroes,
    IReadOnlyDictionary<int, int> heldItemLevels,
    pbAiInfoConfig? npcConfig)
{
    public const int HeroLocked = 1;
    public const int HeroUnlocked = 0;
    public const int NoHeroID = 0;

    public ulong Uid { get; } = uid;
    public bool IsNpc { get; } = isNpc;
    public pbCSDT_CAMPPLAYERINFO CampPlayerInfo { get; } = campPlayerInfo;
    public Dictionary<int, pbCOMDT_CHOICEHERO> UsableHeroes { get; } = usableHeroes;
    public IReadOnlyDictionary<int, int> HeldItemLevels { get; } = heldItemLevels;
    public pbAiInfoConfig? NpcConfig { get; } = npcConfig;
    public bool IsGuestAccount { get; init; }

    public pbCOMDT_PLAYERINFO PlayerInfo => CampPlayerInfo.PlayerInfo;
    public int CampClient => PlayerInfo.ObjCampClient;
    public int PosOfCamp => PlayerInfo.PosOfCamp;

    public pbCOMDT_CHOICEHERO ChoiceHero
    {
        get => PlayerInfo.ChoiceHero;
        set => PlayerInfo.ChoiceHero = value;
    }

    public int ChosenHeroID => ChoiceHero.HeroID;
    public bool IsHeroLocked => ChoiceHero.IsHeroLocked == HeroLocked;
    public int ObjId => PlayerInfo.ObjId;
    public bool IsJudge => IsReferee(CampClient);
    public RelayTicket? RelayTicket { get; set; }
    public IReadOnlyDictionary<int, RandomPickHero> RandomPickHeroes { get; set; } = new Dictionary<int, RandomPickHero>();
    public pbCOMDT_RECENTLY_USED_POKEMON_SORT_LIST_INFO RecentlyUsedPokemonList { get; init; } = new();
    public BattleCosmetics Cosmetics { get; set; } = BattleCosmetics.None;

    public static bool IsReferee(int campClient) =>
        campClient is >= (int)PB_ENUM_COM_CAMP_CLIENT.PbCampClientJudgeBegin and <= (int)PB_ENUM_COM_CAMP_CLIENT.PbCampClientJudgeEnd;
}

public sealed record RelayTicket(int Token, string ZSDCode);

public sealed record RandomPickHero(pbCOMDT_ACNT_USABLE_HERO_DETAIL Detail, pbCOMDT_CHOICEHERO Choice);

public sealed class BattleDesk(
    int deskID,
    long deskCrtSeq,
    pbCOMDT_DESKINFO deskInfo,
    pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE? pveChallengeSelectNode,
    IReadOnlyList<DeskPlayer> players,
    int pickTimeOut,
    int pickReminderSec)
{
    public int DeskID { get; } = deskID;
    public Guid SettlementID { get; init; } = Guid.NewGuid();
    public long DeskCrtSeq { get; } = deskCrtSeq;
    public pbCOMDT_DESKINFO DeskInfo { get; } = deskInfo;
    public pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE? PveChallengeSelectNode { get; } = pveChallengeSelectNode;
    public IReadOnlyList<DeskPlayer> Players { get; } = players;
    public IReadOnlyList<DeskPlayer> Judges { get; init; } = [];
    public int PickTimeOut { get; } = pickTimeOut;
    public int PickReminderSec { get; } = pickReminderSec;
    public DeskPhase Phase { get; set; } = DeskPhase.Picking;
    public BanPickDraft? Draft { get; set; }
    public FriendlyRoomRules FriendRoomRule { get; init; } = FriendlyRoomRules.None;
    public int RelayEntity { get; init; } = RandomNumberGenerator.GetInt32(int.MinValue, 0);

    public IEnumerable<DeskPlayer> Humans => Players.Where(player => !player.IsNpc);

    public IEnumerable<DeskPlayer> Attendees => Humans.Concat(Judges);

    public ulong[] HumanUids => [.. Attendees.Select(player => player.Uid)];

    public ulong[] HumanUidsOf(int campClient) => [.. Attendees.Where(player => player.CampClient == campClient || player.IsJudge).Select(player => player.Uid)];

    public bool AllHumansLocked => Humans.All(player => player.IsHeroLocked);

    public bool BotsPickLive => PveChallengeSelectNode is null;

    public DeskPlayer? FindPlayer(int objId) => Players.FirstOrDefault(player => player.ObjId == objId);

    public DeskPlayer? FindHuman(ulong uid) => Players.FirstOrDefault(player => !player.IsNpc && player.Uid == uid);

    public DeskPlayer? FindJudge(ulong uid) => Judges.FirstOrDefault(judge => judge.Uid == uid);

    public DeskPlayer? FindAttendee(ulong uid) => FindHuman(uid) ?? FindJudge(uid);

    public IEnumerable<DeskPlayer> CampOf(int campClient) => Players.Where(player => player.CampClient == campClient);

    public bool IsHeroTakenInCamp(int campClient, int heroID, DeskPlayer except) =>
        CampOf(campClient).Any(player => player != except && player.ChosenHeroID == heroID);

    public pbCOMDT_OB_DESK BuildOBDesk() => new()
    {
        DeskID = DeskID,
        DeskCrtSeq = DeskCrtSeq,
        RelayEntity = RelayEntity,
    };
}
