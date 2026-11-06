using System.Collections.Frozen;
using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Login;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.PveChallenge;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Online;
using SpiritUNITE.Network.Lwip;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Battle;

public sealed class DeskManager
{
    private const int AcntObjType = 1;
    private const int NpcObjType = 2;
    private const int FirstAcntObjId = 1;
    private const int LastAcntObjId = 1999;
    private const int NpcObjIdBase = 2000;
    private const int NpcPlayerAILevel = 1;
    private const int RelayIsUDP = 1;
    private const int WayTipBroad = 1;
    private const int FreePokemon = 1;
    private const int NoHeroID = 0;
    private const int MainBranchIndex = 0;
    private const int TrainerSkillParamIndex = 0;
    private const int TrainerItemType = (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeTraineritem;
    private const int ForeverShowIcon = (int)PB_ENUM_ENM_PICK_POKEMON_SHOW_ICON_TYPE.PbEnmPickPokemonShowIconTypeForever;
    private const int FreeShowIcon = (int)PB_ENUM_ENM_PICK_POKEMON_SHOW_ICON_TYPE.PbEnmPickPokemonShowIconTypeFree;
    private const int LastEffectUpdReason = (int)PB_ENUM_ENM_PICK_UPD_TIME_REASON.PbEnmPickUpdTimeLasteffect;
    private const int RandomTeamPickMode = 1;
    private const int ShownInBox = 0;
    private const int SingleForm = 0;
    private const int WayTipTimeLeadSeconds = 1;
    private const int NoPasterRule = (int)PB_ENUM_RES_CHOOSE_PASTER_RULE.PbResChoosePasterRuleDefault;
    private const int FixedPasterRule = (int)PB_ENUM_RES_CHOOSE_PASTER_RULE.PbResChoosePasterRuleAssign;
    private const int FixedPasterIndex = 0;
    private const int FallbackNpcTrainerItemID = 1002002004;
    private const int CatchGameType = (int)PB_ENUM_RES_MATCHGAMETYPE.PbMatchGameTypeCatch;
    private const int TrialLicenseItemType = (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePokemontimelimited;
    private const int TrialLicensePokemonParamIndex = 0;
    private const int CardShowIcon = (int)PB_ENUM_ENM_PICK_POKEMON_SHOW_ICON_TYPE.PbEnmPickPokemonShowIconTypeCard;
    private const int NoPokemon = 0;
    private const long NoCardItemId = 0;
    private const int LevelableHeldItem = 1;
    private const int HeldItemGroupIdParamIndex = 1;
    private const int HeldItemBindPokemonParamIndex = 2;
    private const int NoBindPokemon = 0;
    private const int HeldItemSystemConfigId = 1;
    private const int FirstAILevel = 1;
    private const int NoHeldItemLevel = 0;
    private const int ForbidQuitGame = 1;
    private const int BotPickStepMs = 500;
    private const int FirstBotChooseStep = 22;
    private const int LastBotChooseStep = 76;
    private const int FirstBotLockStep = 3;
    private const int LastBotLockStep = 6;
    private const int PickReminderLeadSec = 1;
    private const int WarmBattleType = (int)PB_ENUM_ENM_WARMBATTLE_TYPE.PbEnmWarmbattleRookie;
    private const int NoWarmBattle = 0;
    private const int NoWayTipBroad = 0;
    private const int NoHeldItemAttrIndex = 0;
    private const int FirstHeldItemLevel = 1;
    private const int WayTipShowPercent = 50;
    private const int Percent = 100;

    private static readonly FrozenDictionary<int, int> NpcTrainerItemByHeroID = new Dictionary<int, int>
    {
        [100829] = 1002002002,
        [100004] = 1002002001,
        [100131] = 1002002001,
        [100246] = 1002002001,
        [100661] = 1002002005,
        [100884] = 1002002001,
        [190280] = 1002002001,
        [190845] = 1002002003,
        [190359] = 1002002004,
        [100448] = 1002002004,
        [100025] = 1002002004,
        [100039] = 1002002004,
        [100700] = 1002002004,
        [190079] = 1002002005,
        [190066] = 1002002001,
        [100220] = 1002002001,
    }.ToFrozenDictionary();

    private readonly GameData gameData;
    private readonly RoleInfoStore roles;
    private readonly PokemonManager pokemon;
    private readonly BagManager bag;
    private readonly ProfileManager profiles;
    private readonly PveChallengeProgress pveChallenge;
    private readonly LobbyRoles lobbyRoles;
    private readonly LwipTickets relayTickets;
    private readonly RelayRoles relayRoles;
    private readonly GVoiceOptions gVoice;
    private readonly HeroPickOptions heroPick;
    private readonly LoginManager login;
    private readonly TimeProvider clock;
    private readonly ILogger<DeskManager> logger;
    private readonly IReadOnlyDictionary<int, pbPokemon_Base> pokemonByHeroID;
    private readonly NpcHeldItem[] npcHeldItems;
    private readonly FrozenDictionary<(int GroupId, int Level), int> levelUpDesignIds;
    private readonly Lock gate = new();
    private readonly Dictionary<int, BattleDesk> desksById = [];
    private readonly Dictionary<ulong, BattleDesk> desksByUid = [];
    private int nextDeskID = 1;

    public event Action<BattleDesk>? RelayHandedOff;

    public event Func<ulong, Task>? DeskClosed;

    public DeskManager(
        GameData gameData,
        RoleInfoStore roles,
        PokemonManager pokemon,
        BagManager bag,
        ProfileManager profiles,
        PveChallengeProgress pveChallenge,
        LobbyRoles lobbyRoles,
        LwipTickets relayTickets,
        RelayRoles relayRoles,
        IOptions<GVoiceOptions> gVoice,
        IOptions<HeroPickOptions> heroPick,
        LoginManager login,
        TimeProvider clock,
        ILogger<DeskManager> logger)
    {
        this.gameData = gameData;
        this.roles = roles;
        this.pokemon = pokemon;
        this.bag = bag;
        this.profiles = profiles;
        this.pveChallenge = pveChallenge;
        this.lobbyRoles = lobbyRoles;
        this.relayTickets = relayTickets;
        this.relayRoles = relayRoles;
        this.gVoice = gVoice.Value;
        this.heroPick = heroPick.Value;
        this.login = login;
        this.clock = clock;
        this.logger = logger;
        pokemonByHeroID = gameData.PokemonBase.Rows
            .Where(row => row.MainUnitId != NoHeroID)
            .GroupBy(row => row.MainUnitId)
            .ToDictionary(group => group.Key, group => group.First());
        levelUpDesignIds = gameData.HeldItemLevelUpDesign.Rows
            .GroupBy(design => (design.GroupId, design.Level))
            .ToFrozenDictionary(group => group.Key, group => group.First().Id);
        npcHeldItems = BuildNpcHeldItems(gameData);
        lobbyRoles.RoleOffline += OnRoleOfflineAsync;
    }

    public async Task<RoomOutcome> BeginDeskAsync(MatchRoom room)
    {
        var memberUids = room.Members.Select(member => member.Uid).ToArray();
        var humanProfiles = new Dictionary<ulong, HumanProfile>();
        foreach (var uid in memberUids)
        {
            if (roles.Find(uid) is not { } role)
            {
                return AbortAll(memberUids, PB_ENUM_RES_ERROR_CODE.PbErrPrepareDeskFail);
            }

            humanProfiles[uid] = await LoadHumanProfileAsync(role);
        }

        if (room.MatchSucc is not { } matchSucc || gameData.SceneMap.Find(room.MapId) is not { } scene)
        {
            return AbortAll(memberUids, PB_ENUM_RES_ERROR_CODE.PbErrPrepareDeskFail);
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        BattleDesk desk;
        var outcome = new RoomOutcome();
        var releasedUids = new HashSet<ulong>();
        lock (gate)
        {
            ulong[] offlineUids = [.. memberUids.Where(uid => !lobbyRoles.IsOnline(uid))];
            if (offlineUids.Length > 0)
            {
                logger.LogInformation("Room {RoomID} gets no desk: uids {Uids} went offline while it was prepared", room.RoomID, offlineUids);
                return AbortAll(memberUids, PB_ENUM_RES_ERROR_CODE.PbErrMatchAbort);
            }

            foreach (var uid in memberUids)
            {
                if (desksByUid.GetValueOrDefault(uid) is { } staleDesk)
                {
                    releasedUids.UnionWith(DropSeat(staleDesk, uid, outcome));
                }
            }

            releasedUids.ExceptWith(memberUids);

            desk = CreateDesk(room, scene, matchSucc, humanProfiles, now);
            desksById[desk.DeskID] = desk;
            foreach (var uid in desk.HumanUids)
            {
                desksByUid[uid] = desk;
            }

            if (FindRoute(desk) is { } route)
            {
                PreparePveNpcPicks(desk, route, now, outcome);
            }

            foreach (var human in desk.Humans)
            {
                outcome.Push(human.Uid, PB_ENUM_CS_MSGID_DEF.PbScidMultGameBeginPickNtf, BuildBeginPickNtf(desk, human));
            }
        }

        logger.LogInformation(
            "Desk {DeskID} formed from room {RoomID} on map {MapId} for uids {Uids}; pick open for {PickTimeOut} s",
            desk.DeskID,
            room.RoomID,
            desk.DeskInfo.MapId,
            desk.HumanUids,
            desk.PickTimeOut);
        _ = ExpirePickAsync(desk);
        if (desk.BotsPickLive)
        {
            foreach (var npc in desk.Players.Where(player => player.IsNpc))
            {
                _ = PickBotLiveAsync(desk, npc);
            }
        }

        await RaiseDeskClosedAsync(releasedUids);
        return outcome;
    }

    public RoomOutcome ChooseHero(ulong uid, int heroID)
    {
        lock (gate)
        {
            if (FindPickingHuman(uid, out var desk, out var player) is { } refusal)
            {
                return refusal;
            }

            if (player.IsHeroLocked)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrHadLockHero);
            }

            if (!player.UsableHeroes.TryGetValue(heroID, out var choice))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCannotChooseHero);
            }

            if (player.ChosenHeroID == heroID)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrHadChooseHero);
            }

            if (desk.IsHeroTakenInCamp(player.CampClient, heroID, player))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrChooseSameHeroInCamp);
            }

            var outcome = new RoomOutcome();
            ChooseHumanHero(desk, player, choice, clock.GetUtcNow().ToUnixTimeSeconds(), outcome);
            return outcome.Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidChooseHeroRsp, new pbSCChooseHeroRsp
            {
                ResultInfo = new pbResultInfo(),
                HeroId = heroID,
            });
        }
    }

    public RoomOutcome UnlockHero(ulong uid, int heroID)
    {
        lock (gate)
        {
            if (FindPickingHuman(uid, out var desk, out var player) is { } refusal)
            {
                return refusal;
            }

            if (player.ChosenHeroID == NoHeroID)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrHasNotChoosePokemon);
            }

            if (player.ChosenHeroID != heroID)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrLockHeroNotSameAsChoose);
            }

            if (!player.IsHeroLocked)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDeskStateErr);
            }

            player.ChoiceHero.IsHeroLocked = DeskPlayer.HeroUnlocked;
            return new RoomOutcome()
                .PushAll(desk.HumanUidsOf(player.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroUnlockHero))
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidUnLockHeroRsp, new pbSCUnLockHeroRsp { ResultInfo = new pbResultInfo() });
        }
    }

    public RoomOutcome CancelChooseHero(ulong uid)
    {
        lock (gate)
        {
            if (FindPickingHuman(uid, out var desk, out var player) is { } refusal)
            {
                return refusal;
            }

            if (player.IsHeroLocked)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrHadLockHero);
            }

            if (player.ChosenHeroID == NoHeroID)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrHasNotChoosePokemon);
            }

            player.ChoiceHero = new pbCOMDT_CHOICEHERO();
            return new RoomOutcome()
                .PushAll(desk.HumanUidsOf(player.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroCancelChooseHero))
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidCancelChooseHeroRsp, new pbSCCancelChooseHeroRsp { ResultInfo = new pbResultInfo() });
        }
    }

    public RoomOutcome ChangeWayTip(ulong uid, int wayTipType)
    {
        lock (gate)
        {
            if (FindPickingHuman(uid, out var desk, out var player) is { } refusal)
            {
                return refusal;
            }

            if (player.ChosenHeroID == NoHeroID)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrHasNotChoosePokemon);
            }

            if (!IsLaneWayTip(wayTipType))
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrChgWayTipTypeInvalid);
            }

            if (player.ChoiceHero.WayTipType == wayTipType)
            {
                return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrChgWayTipTypeSame);
            }

            player.ChoiceHero.WayTipType = wayTipType;
            player.ChoiceHero.ChgWayTipTime = (int)(clock.GetUtcNow().ToUnixTimeSeconds() + WayTipTimeLeadSeconds);
            return new RoomOutcome()
                .PushAll(desk.HumanUidsOf(player.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildBroadcastWayTipChgNtf(player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroChgWayTip))
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidChgWayTipRsp, new pbSCChgWayTipRsp
                {
                    ResultInfo = new pbResultInfo(),
                    WayTipType = wayTipType,
                });
        }
    }

    public RoomOutcome ChangeHeldItems(ulong uid, int pokemonId, IReadOnlyList<int> heldItemIds) =>
        ChangeHeroLoadout(uid, pokemonId, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroHelditem, (player, choice) =>
        {
            choice.HeldItemList = BuildHeldItemList(heldItemIds, player.HeldItemLevels);
        });

    public RoomOutcome ChangeTrainerItem(ulong uid, int pokemonId, int trainerItemID) =>
        ChangeHeroLoadout(uid, pokemonId, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroChgTrainerItem, (_, choice) =>
        {
            choice.CarryTrainerItemID = trainerItemID;
            choice.CarryTrainerSkillID = TrainerSkillOf(trainerItemID);
        });

    public RoomOutcome ChangePaster(ulong uid, int pokemonId, int pasterID) =>
        ChangeHeroLoadout(uid, pokemonId, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroPasterChg, (player, choice) =>
        {
            choice.WearPasterID = pasterID;
            foreach (var detail in player.CampPlayerInfo.UsableHero.HeroDetail.Where(detail => detail.HeroID == choice.HeroID))
            {
                detail.WearPasterID = pasterID;
            }
        });

    public async Task<RoomMessage[]> DeliverAsync(ulong requesterUid, RoomOutcome outcome)
    {
        await PushAllAsync(outcome.Pushes.Where(push => push.Uid != requesterUid));
        return [.. outcome.Pushes.Where(push => push.Uid == requesterUid).Select(push => push.Message)];
    }

    private RoomOutcome ChangeHeroLoadout(
        ulong uid,
        int pokemonId,
        PB_ENUM_ENM_CHOICE_HERO_CHG_REASON chgReason,
        Action<DeskPlayer, pbCOMDT_CHOICEHERO> change)
    {
        lock (gate)
        {
            var outcome = new RoomOutcome();
            if (FindPickingHuman(uid, out var desk, out var player) is not null
                || HeroIDOf(pokemonId) is not (> NoHeroID and var heroID)
                || !player.UsableHeroes.TryGetValue(heroID, out var template))
            {
                return outcome;
            }

            change(player, template);
            if (player.ChosenHeroID != heroID)
            {
                return outcome;
            }

            change(player, player.ChoiceHero);
            return outcome.PushAll(desk.HumanUidsOf(player.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(player, chgReason));
        }
    }

    private void ChooseHumanHero(BattleDesk desk, DeskPlayer player, pbCOMDT_CHOICEHERO choice, long now, RoomOutcome outcome)
    {
        player.ChoiceHero = choice.Clone();
        player.ChoiceHero.ChgWayTipTime = (int)(now + WayTipTimeLeadSeconds);
        outcome.PushAll(desk.HumanUidsOf(player.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildBroadcastWayTipChgNtf(player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroChooseHero));
    }

    private static bool IsLaneWayTip(int wayTipType) =>
        wayTipType is (int)PB_ENUM_ENM_WAY_TIP_TYPE.PbEnmWayTipTypeUp
            or (int)PB_ENUM_ENM_WAY_TIP_TYPE.PbEnmWayTipTypeMiddle
            or (int)PB_ENUM_ENM_WAY_TIP_TYPE.PbEnmWayTipTypeBottom;

    public RoomOutcome ConfirmHero(ulong uid, int heroID)
    {
        lock (gate)
        {
            if (FindLockableHero(uid, heroID, out _, out _) is { } refusal)
            {
                return refusal;
            }

            return new RoomOutcome().Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidConfirmHeroRsp, new pbSCConfirmHeroRsp
            {
                ResultInfo = new pbResultInfo(),
                HeroId = heroID,
            });
        }
    }

    public RoomOutcome LockHero(ulong uid, int heroID)
    {
        BattleDesk desk;
        RoomOutcome outcome;
        lock (gate)
        {
            if (FindLockableHero(uid, heroID, out desk, out var player) is { } refusal)
            {
                return refusal;
            }

            player.ChoiceHero.IsHeroLocked = DeskPlayer.HeroLocked;
            outcome = new RoomOutcome()
                .PushAll(desk.HumanUidsOf(player.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroLockHero))
                .Push(uid, PB_ENUM_CS_MSGID_DEF.PbScidLockHeroRsp, new pbSCLockHeroRsp { ResultInfo = new pbResultInfo() });
            LogTrialLicenseLock(desk, player);
            if (desk.Phase == DeskPhase.WaitingRelay && desk.BotsPickLive && desk.AllHumansLocked)
            {
                FinalizeBots(desk, clock.GetUtcNow().ToUnixTimeSeconds(), outcome);
                return outcome;
            }

            if (desk.Phase != DeskPhase.Picking || !IsPickComplete(desk))
            {
                return outcome;
            }

            BeginPickWait(desk, outcome);
        }

        StartPickWait(desk);
        return outcome;
    }

    private void FinalizeBots(BattleDesk desk, long now, RoomOutcome outcome)
    {
        var unfinishedCamps = desk.Players
            .Where(player => player.IsNpc && !player.IsHeroLocked)
            .Select(player => player.CampClient)
            .ToHashSet();
        foreach (var npc in desk.Players.Where(player => player.IsNpc && unfinishedCamps.Contains(player.CampClient)))
        {
            if (npc.IsHeroLocked)
            {
                outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(npc, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroChooseHero));
            }
            else if (npc.ChosenHeroID != NoHeroID || ChooseLiveBot(desk, npc, now, outcome))
            {
                LockNpc(desk, npc, outcome);
            }
        }

        if (unfinishedCamps.Count > 0)
        {
            logger.LogInformation("Desk {DeskID}: every trainer locked after the pick reminder; NPCs of camps {Camps} were finished at once", desk.DeskID, unfinishedCamps);
        }
    }

    private void PreparePveNpcPicks(BattleDesk desk, pbPveChallengeStageRoute route, long now, RoomOutcome outcome)
    {
        var enemies = Enemies(desk).ToHashSet();
        var enemyHeroIDs = HeroIDsOf(route.EnemyldList);
        int[]? fixedAllyHeroIDs = IsCatchDesk(desk) && route.TeamPickMode != RandomTeamPickMode
            ? HeroIDsOf(route.TeamldList)
            : null;
        foreach (var npc in desk.Players.Where(player => player.IsNpc))
        {
            if (enemies.Contains(npc))
            {
                PickNpc(desk, npc, enemyHeroIDs, showsWayTip: true, now, outcome);
            }
            else if (fixedAllyHeroIDs is not null)
            {
                PickNpc(desk, npc, fixedAllyHeroIDs, showsWayTip: true, now, outcome);
            }
        }
    }

    private async Task PickBotLiveAsync(BattleDesk desk, DeskPlayer npc)
    {
        try
        {
            await Task.Delay(RandomBotPickDelay(FirstBotChooseStep, LastBotChooseStep), clock);
            var chosen = new RoomOutcome();
            lock (gate)
            {
                if (!IsPicking(desk) || npc.ChosenHeroID != NoHeroID)
                {
                    return;
                }

                ChooseLiveBot(desk, npc, clock.GetUtcNow().ToUnixTimeSeconds(), chosen);
            }

            await PushAllAsync(chosen);
            await Task.Delay(RandomBotPickDelay(FirstBotLockStep, LastBotLockStep), clock);
            var locked = new RoomOutcome();
            bool pickComplete;
            lock (gate)
            {
                if (!IsPicking(desk) || npc.ChosenHeroID == NoHeroID || npc.IsHeroLocked)
                {
                    return;
                }

                LockNpc(desk, npc, locked);
                pickComplete = desk.Phase == DeskPhase.Picking && IsPickComplete(desk);
                if (pickComplete)
                {
                    BeginPickWait(desk, locked);
                }
            }

            await PushAllAsync(locked);
            if (pickComplete)
            {
                StartPickWait(desk);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Bot pick for NPC {FakeUid} on desk {DeskID} failed", npc.Uid, desk.DeskID);
        }
    }

    private static TimeSpan RandomBotPickDelay(int firstStep, int lastStep) =>
        TimeSpan.FromMilliseconds(BotPickStepMs * Random.Shared.Next(firstStep, lastStep + 1));

    private static bool IsPickComplete(BattleDesk desk) =>
        desk.BotsPickLive ? desk.Players.All(player => player.IsHeroLocked) : desk.AllHumansLocked;

    private void BeginPickWait(BattleDesk desk, RoomOutcome outcome)
    {
        desk.Phase = DeskPhase.WaitingRelay;
        outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidDeskUpdPickWaitTimeNtf, new pbSCDeskUpdPickWaitTimeNtf
        {
            LeftSec = desk.PickReminderSec,
            UpdReason = LastEffectUpdReason,
        });
        if (!desk.BotsPickLive)
        {
            PickAllies(desk, clock.GetUtcNow().ToUnixTimeSeconds(), outcome);
        }
    }

    private void StartPickWait(BattleDesk desk)
    {
        logger.LogInformation("Desk {DeskID}: every pick locked; relay hand-off in {PickReminderSec} s", desk.DeskID, desk.PickReminderSec);
        _ = HandOffAfterPickWaitAsync(desk);
    }

    private bool IsPicking(BattleDesk desk) => IsCurrent(desk, DeskPhase.Picking) || IsCurrent(desk, DeskPhase.WaitingRelay);

    private async Task<HumanProfile> LoadHumanProfileAsync(LMNRoleInfo role)
    {
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var usedPokemon = (await pokemon.LoadUsedPokemonAsync(role)).ToDictionary(used => used.PokemonId);
        var ownedItems = await bag.GetOwnedOutSideItemsAsync(role.GameUlongUID);
        var heldItemLevels = ownedItems
            .Where(item => gameData.OutSideItemBase.Find(item.ItemId)?.OutSideItemCategory == BagManager.HeldItemCategory)
            .ToDictionary(item => item.ItemId, item => item.Level);
        int[] trainerItemIDs =
        [
            .. ownedItems
                .Where(item => gameData.OutSideItemBase.Find(item.ItemId)?.OutSideItemType == TrainerItemType)
                .Select(item => item.ItemId)
                .Order(),
        ];
        var loadout = new HumanLoadout(heldItemLevels, trainerItemIDs);
        var heroDetails = new List<pbCOMDT_ACNT_USABLE_HERO_DETAIL>();
        var usableHeroes = new Dictionary<int, pbCOMDT_CHOICEHERO>();
        var ownedPokemonIds = role.BuildPokemonList().Select(owned => owned.PokemonId).ToArray();
        foreach (var pokemonId in ownedPokemonIds)
        {
            if (HeroIDOf(pokemonId) is not (> NoHeroID and var heroID) || usableHeroes.ContainsKey(heroID))
            {
                continue;
            }

            var used = usedPokemon.GetValueOrDefault(pokemonId);
            heroDetails.Add(new pbCOMDT_ACNT_USABLE_HERO_DETAIL
            {
                HeroID = heroID,
                WearPasterID = used?.WearPasterID ?? PokemonManager.NoPaster,
                PokemonShowIcon = ForeverShowIcon,
                PokemonTotalPoint = used?.PokemonTotalPoint ?? 0,
                PokemonSeasonPoint = used?.PokemonSeasonPoint ?? 0,
            });
            usableHeroes[heroID] = BuildHumanChoiceHero(heroID, used, loadout);
        }

        foreach (var pokemonId in pokemon.FreePokemonList.Except(ownedPokemonIds))
        {
            if (HeroIDOf(pokemonId) is not (> NoHeroID and var heroID) || usableHeroes.ContainsKey(heroID))
            {
                continue;
            }

            heroDetails.Add(new pbCOMDT_ACNT_USABLE_HERO_DETAIL
            {
                HeroID = heroID,
                IsFreePokemon = FreePokemon,
                PokemonShowIcon = FreeShowIcon,
            });
            usableHeroes[heroID] = BuildHumanChoiceHero(heroID, usedPokemon.GetValueOrDefault(pokemonId), loadout);
        }

        var trialLicenses = ownedItems
            .Select(item => (CardItemId: item.ItemId, HeroID: TrialLicenseHeroIDOf(item.ItemId)))
            .Where(trial => trial.HeroID > NoHeroID)
            .OrderBy(trial => trial.HeroID)
            .ThenBy(trial => trial.CardItemId);
        foreach (var (cardItemId, heroID) in trialLicenses)
        {
            if (usableHeroes.ContainsKey(heroID))
            {
                continue;
            }

            heroDetails.Add(new pbCOMDT_ACNT_USABLE_HERO_DETAIL
            {
                HeroID = heroID,
                CardItemId = cardItemId,
                PokemonShowIcon = CardShowIcon,
            });
            usableHeroes[heroID] = BuildHumanChoiceHero(heroID, used: null, loadout);
        }

        return new HumanProfile(
            ProfileManager.BuildTrainerImageInfo(style),
            profiles.BuildHeadIconInfo(role, style).CurHeadIconInfo,
            heroDetails,
            usableHeroes,
            loadout);
    }

    private BattleDesk CreateDesk(
        MatchRoom room,
        pbScene_Map scene,
        pbCOMDT_MATCH_SUCC_DETAIL matchSucc,
        IReadOnlyDictionary<ulong, HumanProfile> humanProfiles,
        long now)
    {
        var humans = new List<DeskPlayer>();
        var npcs = new List<DeskPlayer>();
        var takenAcntObjIds = new HashSet<int>();
        foreach (var camp in matchSucc.MemInfo.CampMem)
        {
            foreach (var member in camp.MemInfo)
            {
                if (member.MemberDetail.MemberOfAcnt is { } acnt && humanProfiles.TryGetValue(acnt.Uid, out var profile))
                {
                    humans.Add(BuildHumanPlayer(room, member, acnt, camp.CampClient, PickAcntObjId(takenAcntObjIds), profile));
                }
                else if (member.MemberDetail.MemberOfNpc?.Detail is { } npc)
                {
                    npcs.Add(BuildNpcPlayer(member, npc, camp.CampClient, NpcObjIdBase + npcs.Count + 1, room.MatchNpcConfigs.GetValueOrDefault(npc.Uid)));
                }
            }
        }

        var deskInfo = new pbCOMDT_DESKINFO
        {
            MapId = room.MapId,
            AILevel = matchSucc.RoomInfo.AILevel,
            GameType = scene.GameType,
            RoomType = (int)room.RoomType,
            ManMachine = room.ManMachine,
            RoomRule = new pbCOMDT_FRIENDLY_ROOM_RULE_INFO(),
            IsForbidQuitGame = room.PveChallengeSelectNode is null ? ForbidQuitGame : 0,
            LegendPokemonInfo = new pbCOMDT_LEGEND_POKEMON_INFO(),
            IsWarmBattle = matchSucc.RoomInfo.IsWarmBattle,
            WarmBattleType = matchSucc.RoomInfo.IsWarmBattle != NoWarmBattle ? WarmBattleType : NoWarmBattle,
        };
        return new BattleDesk(
            nextDeskID++,
            now,
            deskInfo,
            room.PveChallengeSelectNode?.Clone(),
            [.. humans.OrderByDescending(human => human.PosOfCamp), .. npcs],
            scene.PickTimeOut,
            scene.PickReminderSec);
    }

    private DeskPlayer BuildHumanPlayer(
        MatchRoom room,
        pbCOMDT_ROOMMEMBER_DT member,
        pbCOMDT_ROOMMEMBER_OF_ACNT acnt,
        int campClient,
        int objId,
        HumanProfile profile)
    {
        var campPlayerInfo = new pbCSDT_CAMPPLAYERINFO
        {
            PlayerInfo = new pbCOMDT_PLAYERINFO
            {
                ObjType = AcntObjType,
                PosOfCamp = member.PosOfCamp,
                ObjId = objId,
                Level = acnt.MemberLevel,
                Name = acnt.MemberName,
                Detail = new pbCOMDT_PLAYERINFO_DETAIL
                {
                    PlayerOfAcnt = new pbCOMDT_PLAYERINFO_OF_ACNT
                    {
                        HeadId = acnt.MemberHeadId,
                        Uid = acnt.Uid,
                        Gender = acnt.Gender,
                        TrainerImageInfo = profile.TrainerImageInfo.Clone(),
                        RelayMD5 = login.FindRelayMD5(acnt.Uid),
                        CurBubbleInfo = new pbCOMDT_BUBBLE_BASE_INFO(),
                        UserPlantInfo = acnt.UserPlantInfo.Clone(),
                    },
                },
                ChoiceHero = new pbCOMDT_CHOICEHERO(),
                ObjCampClient = campClient,
                RoleAvatarInfo = acnt.RoleAvatarInfo.Clone(),
                ContestRoleAvatarInfo = acnt.RoleAvatarInfo.Clone(),
                IntimacyInfo = new pbCOMDT_BATTLE_INTIMACY_INFO(),
                PickInfo = new pbCOMDT_MULTI_GAME_PICK_INFO(),
                CurHeadIconInfo = profile.CurHeadIconInfo.Clone(),
                TowerDefenseBuffInfo = new pbCOMDT_TOWER_DEFENSE_BUFF_INFO(),
                TmpNameList = acnt.TmpNameList.Clone(),
                PveChallengeBuffInfo = new pbCOMDT_PVE_CHALLENGE_BUFF_INFO(),
                DefaultRoleAvatarInfo = acnt.DefaultRoleAvatarInfo.Clone(),
                BattleMemberConfigList = acnt.BattleMemberConfigList.Clone(),
                TeamFlag = acnt.TeamFlag,
            },
            OpenID = acnt.Uid.ToString(CultureInfo.InvariantCulture),
            UsableHero = new pbCOMDT_ACNT_USABLE_HERO { HeroDetail = { profile.HeroDetails } },
            UsablePasterList = new pbCOMDT_POKEMON_PASTER_LIST { SkillEffectsExpandPackInfo = new pbCOMDT_SKILL_EFFECTS_EXPAND_PACK_LIST() },
            UsableTrainerItemList = new pbCOMDT_TRAINRE_ITEM_LIST { TrainerItemList = { profile.Loadout.TrainerItemIDs } },
            RoomSvrUniq = new pbCOMDT_ROOMSVR_UNIQ
            {
                RoomsvrEntity = MatchRoom.RoomEntity,
                RoomsvrRoomID = room.RoomID,
                RoomsvrRoomSeq = room.RoomSeq,
            },
            VirtualRegionInfo = new pbCOMDT_VIRTUAL_REGION_RANK_INFO(),
        };
        return new DeskPlayer(
            acnt.Uid,
            isNpc: false,
            campPlayerInfo,
            profile.UsableHeroes.ToDictionary(entry => entry.Key, entry => entry.Value.Clone()),
            profile.Loadout.HeldItemLevels,
            npcConfig: null);
    }

    private static DeskPlayer BuildNpcPlayer(
        pbCOMDT_ROOMMEMBER_DT member,
        pbCOMDT_FAKEACNT_DETAIL npc,
        int campClient,
        int objId,
        pbAiInfoConfig? npcConfig)
    {
        var campPlayerInfo = new pbCSDT_CAMPPLAYERINFO
        {
            PlayerInfo = new pbCOMDT_PLAYERINFO
            {
                ObjType = NpcObjType,
                PosOfCamp = member.PosOfCamp,
                ObjId = objId,
                Level = npc.AcntLevel,
                Name = npc.UserName,
                Detail = new pbCOMDT_PLAYERINFO_DETAIL
                {
                    PlayerOfNpc = new pbCOMDT_PLAYERINFO_OF_NPC
                    {
                        FakeUid = npc.Uid,
                        FakeLevel = npc.AcntLevel,
                        Gender = npc.Gender,
                        HeadId = npc.HeadId,
                        ContestantAction = npc.ContestantAction,
                        ContestantEmoji = npc.ContestantEmoji,
                        ContestantBackgroud = npc.ContestantBackgroud,
                        ContestantFrame = npc.ContestantFrame,
                        ContestantCameraDis = npc.ContestantCameraDis,
                        ContestantDir = npc.ContestantDir,
                        ContestantPos = new pbCOMDT_TRAINER_IMAGE_POSITION(),
                        AILevel = NpcPlayerAILevel,
                        UserPlantInfo = npc.UserPlantInfo.Clone(),
                        Effect = new pbCOMDT_CONTESTANT_EFFECT_LIST(),
                        PokemonEffect = new pbCOMDT_CONTESTANT_POKEMON_EFFECT_INFO { Position = new pbCOMDT_TRAINER_IMAGE_POSITION() },
                    },
                },
                ChoiceHero = BuildEmptyNpcChoiceHero(),
                ObjCampClient = campClient,
                RoleAvatarInfo = npc.AvatarInfo.Clone(),
                ContestRoleAvatarInfo = npc.AvatarInfo.Clone(),
                TmpNameList = npc.TmpNameList.Clone(),
                DefaultRoleAvatarInfo = npc.DefaultRoleAvatarInfo.Clone(),
            },
        };
        return new DeskPlayer(npc.Uid, isNpc: true, campPlayerInfo, [], new Dictionary<int, int>(), npcConfig);
    }

    private pbCOMDT_CHOICEHERO BuildHumanChoiceHero(int heroID, UsedPokemonInfo? used, HumanLoadout loadout)
    {
        var lastTrainerItemID = used?.LastCarryTrainerItemID ?? PokemonManager.NoTrainerItem;
        var trainerItemID = loadout.TrainerItemIDs.Contains(lastTrainerItemID)
            ? lastTrainerItemID
            : loadout.TrainerItemIDs.FirstOrDefault(PokemonManager.NoTrainerItem);
        return new pbCOMDT_CHOICEHERO
        {
            HeroID = heroID,
            HeldItemList = BuildHeldItemList(used?.HeldItemID ?? [], loadout.HeldItemLevels),
            SkillRecommendInfo = new pbCOMDT_SKILLRECOMMENDINFO(),
            WearPasterID = used?.WearPasterID ?? PokemonManager.NoPaster,
            WayTipType = WayTipTypeOf(heroID),
            CarryTrainerItemID = trainerItemID,
            CarryTrainerSkillID = TrainerSkillOf(trainerItemID),
            PokemonTotalPoint = used?.PokemonTotalPoint ?? 0,
            PokemonSeasonPoint = used?.PokemonSeasonPoint ?? 0,
            CarryBadge = BuildEmptyCarryBadge(),
        };
    }

    private pbCOMDT_CHOICEHERO BuildNpcChoiceHero(int heroID, pbAiInfoConfig? npcConfig, int heldItemLevel, bool showsWayTip, long now)
    {
        var choice = BuildEmptyNpcChoiceHero();
        if (heldItemLevel != NoHeldItemLevel && PickNpcHeldItem(heldItemLevel) is { } heldItem)
        {
            choice.HeldItemList.HeldItemInfo.Add(heldItem);
        }

        var trainerItemID = NpcTrainerItemByHeroID.GetValueOrDefault(heroID, FallbackNpcTrainerItemID);
        choice.HeroID = heroID;
        choice.WearPasterID = PickNpcPasterID(heroID, npcConfig);
        choice.CarryTrainerItemID = trainerItemID;
        choice.CarryTrainerSkillID = TrainerSkillOf(trainerItemID);
        if (showsWayTip)
        {
            choice.WayTipType = WayTipTypeOf(heroID);
            choice.WayTipBroad = WayTipBroad;
            choice.ChgWayTipTime = (int)(now + WayTipTimeLeadSeconds);
        }

        return choice;
    }

    private int PickNpcPasterID(int heroID, pbAiInfoConfig? npcConfig) => npcConfig switch
    {
        { ChoosePasterRule: NoPasterRule } => PokemonManager.NoPaster,
        { ChoosePasterRule: FixedPasterRule } fixedPasterNpc => fixedPasterNpc.ChoosePasterID.ElementAtOrDefault(FixedPasterIndex),
        _ => PickRandomPasterID(heroID),
    };

    private int PickRandomPasterID(int heroID)
    {
        if (pokemonByHeroID.GetValueOrDefault(heroID) is not { } hero)
        {
            return PokemonManager.NoPaster;
        }

        var cfgNow = UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow());
        int[] pasterIDs =
        [
            PokemonManager.NoPaster,
            .. gameData.PokemonAvatarBase.Rows
                .Where(avatar => avatar.PokemonId == hero.PokemonId && avatar.OnTime <= cfgNow)
                .Select(avatar => avatar.PokemonAvatarId),
        ];
        return pasterIDs[Random.Shared.Next(pasterIDs.Length)];
    }

    private static pbCOMDT_CHOICEHERO BuildEmptyNpcChoiceHero() => new()
    {
        HeldItemList = new pbCOMDT_HELDITEM_LIST(),
        SkillRecommendInfo = new pbCOMDT_SKILLRECOMMENDINFO(),
        CarryEquipmentPlanInfo = new pbCOMDT_CARRYEQUIPMENT_PLAN_INFO(),
        SpecialEffect = new pbCOMDT_POKEMON_SPECIALEFFECT_INFO(),
        CarryBadge = BuildEmptyCarryBadge(),
        SKillEffectsExpandPackInfo = new pbCOMDT_USING_SKILL_EFFECTS_EXPAND_PACK_LIST(),
    };

    private static pbCOMDT_CARRYE_BADGE_INFO BuildEmptyCarryBadge() => new()
    {
        ScheduleData = new pbCOMDT_BADGE_SCHEDUE_DATA(),
        BadgeEffect = new pbCOMDT_BADGE_EFFECT_INFO(),
    };

    private int HeroIDOf(int pokemonId) => gameData.PokemonBase.Find(pokemonId)?.MainUnitId ?? NoHeroID;

    private int TrialLicenseHeroIDOf(int outSideItemId) =>
        gameData.OutSideItemBase.Find(outSideItemId) is { OutSideItemType: TrialLicenseItemType } item
        && item.ItemParam.ElementAtOrDefault(TrialLicensePokemonParamIndex) is > NoPokemon and var pokemonId
            ? HeroIDOf(pokemonId)
            : NoHeroID;

    private void LogTrialLicenseLock(BattleDesk desk, DeskPlayer player)
    {
        var cardItemId = player.CampPlayerInfo.UsableHero.HeroDetail
            .FirstOrDefault(detail => detail.HeroID == player.ChosenHeroID)?.CardItemId ?? NoCardItemId;
        if (cardItemId != NoCardItemId)
        {
            logger.LogInformation(
                "Desk {DeskID}: uid {Uid} locked hero {HeroID} from trial license {CardItemId}; the license is not consumed",
                desk.DeskID,
                player.Uid,
                player.ChosenHeroID,
                cardItemId);
        }
    }

    private static bool IsCatchDesk(BattleDesk desk) => desk.DeskInfo.GameType == CatchGameType;

    private int NpcHeldItemLevelOf(BattleDesk desk) =>
        IsCatchDesk(desk)
            ? gameData.HeldItemSystemConfig.Find(HeldItemSystemConfigId)?.LevelStages.ElementAtOrDefault(desk.DeskInfo.AILevel - FirstAILevel) ?? NoHeldItemLevel
            : NoHeldItemLevel;

    private pbCOMDT_HELDITEM_INFO? PickNpcHeldItem(int level)
    {
        NpcHeldItem[] candidates = [.. npcHeldItems.Where(item => levelUpDesignIds.ContainsKey((item.GroupId, level)))];
        if (candidates.Length == 0)
        {
            return null;
        }

        var picked = candidates[Random.Shared.Next(candidates.Length)];
        return new pbCOMDT_HELDITEM_INFO
        {
            HeldItemID = picked.OutSideItemId,
            HeldItemAttrIndex = levelUpDesignIds[(picked.GroupId, level)],
        };
    }

    private static NpcHeldItem[] BuildNpcHeldItems(GameData gameData) =>
    [
        .. gameData.OutSideItemBase.Rows
            .Where(item => item.OutSideItemCategory == BagManager.HeldItemCategory
                && item.IsCanLevelUp == LevelableHeldItem
                && item.OutSideItemTypeParam.Count > HeldItemGroupIdParamIndex
                && item.OutSideItemTypeParam.ElementAtOrDefault(HeldItemBindPokemonParamIndex) == NoBindPokemon)
            .OrderBy(item => item.OutSideItemId)
            .Select(item => new NpcHeldItem(item.OutSideItemId, item.OutSideItemTypeParam[HeldItemGroupIdParamIndex])),
    ];

    private int[] HeroIDsOf(IEnumerable<int> pokemonIds) =>
        [.. pokemonIds.Select(HeroIDOf).Where(heroID => heroID != NoHeroID)];

    private int WayTipTypeOf(int heroID) =>
        pokemonByHeroID.GetValueOrDefault(heroID)?.BranchRecommend.ElementAtOrDefault(MainBranchIndex)
        ?? (int)PB_ENUM_ENM_WAY_TIP_TYPE.PbEnmWayTipTypeNull;

    private int TrainerSkillOf(int trainerItemID) =>
        gameData.OutSideItemBase.Find(trainerItemID) is { OutSideItemType: TrainerItemType } item
        && item.OutSideItemTypeParam.Count > TrainerSkillParamIndex
            ? item.OutSideItemTypeParam[TrainerSkillParamIndex]
            : 0;

    private pbPveChallengeStageRoute? FindRoute(BattleDesk desk) =>
        desk.PveChallengeSelectNode is { } node
            ? pveChallenge.FindRoute(node.StageID, node.NodeID, node.DifficultyLevel)
            : null;

    private static IEnumerable<DeskPlayer> Enemies(BattleDesk desk) =>
        desk.Players.Where(player => player.IsNpc && !desk.CampOf(player.CampClient).Any(other => !other.IsNpc));

    private static IEnumerable<DeskPlayer> Allies(BattleDesk desk) =>
        desk.Players.Where(player => player.IsNpc && desk.CampOf(player.CampClient).Any(other => !other.IsNpc));

    private void PickAllies(BattleDesk desk, long now, RoomOutcome outcome)
    {
        var route = FindRoute(desk);
        var allies = Allies(desk).Where(ally => !ally.IsHeroLocked).ToArray();
        var allyHeroIDs = route is null || route.TeamPickMode == RandomTeamPickMode
            ? PickRandomBotHeroIDs(desk, allies)
            : HeroIDsOf(route.TeamldList);
        foreach (var ally in allies)
        {
            PickNpc(desk, ally, allyHeroIDs, showsWayTip: false, now, outcome);
        }
    }

    private int[] PickRandomBotHeroIDs(BattleDesk desk, IReadOnlyList<DeskPlayer> bots)
    {
        var pool = NpcHeroPool(desk);
        var picked = new List<int>(bots.Count);
        foreach (var bot in bots)
        {
            var heroID = PickBotHeroID(desk, bot, pool, picked);
            if (heroID == NoHeroID)
            {
                break;
            }

            picked.Add(heroID);
        }

        return [.. picked];
    }

    private int PickBotHeroID(BattleDesk desk, DeskPlayer bot, IReadOnlyList<int> pool, IReadOnlyCollection<int> pendingHeroIDs)
    {
        var campHeroIDs = desk.CampOf(bot.CampClient)
            .Where(player => player != bot && player.ChosenHeroID != NoHeroID)
            .Select(player => player.ChosenHeroID)
            .Concat(pendingHeroIDs)
            .ToHashSet();
        var campOccupationTypes = campHeroIDs.Select(OccupationTypeOf).ToHashSet();
        int[] freeHeroIDs = [.. pool.Where(heroID => !campHeroIDs.Contains(heroID))];
        int[] freeOccupationTypes = [.. freeHeroIDs.Select(OccupationTypeOf).Where(type => !campOccupationTypes.Contains(type)).Distinct()];
        var heroID = freeHeroIDs.Length == 0
            ? NoHeroID
            : freeOccupationTypes.Length == 0
                ? RandomOf(freeHeroIDs)
                : RandomHeroOfOccupation(freeHeroIDs, RandomOf(freeOccupationTypes));
        logger.LogInformation(
            "Desk {DeskID}: NPC {FakeUid} in camp {Camp} picked hero {HeroID} (OccupationType {OccupationType}) from a pool of {PoolSize}; camp OccupationTypes before {CampOccupationTypes}",
            desk.DeskID,
            bot.Uid,
            bot.CampClient,
            heroID,
            OccupationTypeOf(heroID),
            pool.Count,
            campOccupationTypes.Order());
        return heroID;
    }

    private int RandomHeroOfOccupation(int[] heroIDs, int occupationType) =>
        RandomOf([.. heroIDs.Where(heroID => OccupationTypeOf(heroID) == occupationType)]);

    private static int RandomOf(int[] choices) => choices[Random.Shared.Next(choices.Length)];

    private int[] NpcHeroPool(BattleDesk desk)
    {
        if (gameData.SceneMap.Find(desk.DeskInfo.MapId) is not { } scene)
        {
            return [];
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        return
        [
            .. gameData.PokemonRelease.Rows
                .Where(release => IsReleased(release, now) && heroPick.AllowsNpcHero(release.Order))
                .Select(release => gameData.PokemonBase.Find(release.PokemonId))
                .OfType<pbPokemon_Base>()
                .Where(row => row.HideInBox <= ShownInBox && IsOriginalForm(row))
                .Where(row => gameData.UnitBase.Find(row.MainUnitId)?.HeroMode.Contains(scene.OpenHeroMode) == true)
                .Select(row => row.MainUnitId)
                .Distinct(),
        ];
    }

    private static bool IsReleased(pbPokemon_Release release, long now) =>
        release.OnTime == 0 || UTCTimeUtil.CfgTime2ServerUnixTime(release.OnTime) <= now;

    private static bool IsOriginalForm(pbPokemon_Base row) =>
        row.IsMultiform == SingleForm || row.OriginalForm == row.PokemonId;

    private int OccupationTypeOf(int heroID) => pokemonByHeroID.GetValueOrDefault(heroID)?.OccupationType ?? 0;

    private void PickNpc(
        BattleDesk desk,
        DeskPlayer npc,
        IReadOnlyList<int> preferredHeroIDs,
        bool showsWayTip,
        long now,
        RoomOutcome outcome)
    {
        if (ChooseNpc(desk, npc, preferredHeroIDs, showsWayTip, now, outcome))
        {
            LockNpc(desk, npc, outcome);
        }
    }

    private bool ChooseNpc(
        BattleDesk desk,
        DeskPlayer npc,
        IReadOnlyList<int> preferredHeroIDs,
        bool showsWayTip,
        long now,
        RoomOutcome outcome)
    {
        var heroID = preferredHeroIDs
            .Concat(NpcHeroPool(desk))
            .FirstOrDefault(candidate => !desk.IsHeroTakenInCamp(npc.CampClient, candidate, npc));
        if (heroID == NoHeroID)
        {
            logger.LogWarning("Desk {DeskID}: no free Pokemon left for NPC {FakeUid}", desk.DeskID, npc.Uid);
            return false;
        }

        npc.ChoiceHero = BuildNpcChoiceHero(heroID, npc.NpcConfig, NpcHeldItemLevelOf(desk), showsWayTip, now);
        outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(npc, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroChooseHero));
        return true;
    }

    private bool ChooseLiveBot(BattleDesk desk, DeskPlayer npc, long now, RoomOutcome outcome)
    {
        var heroID = PickBotHeroID(desk, npc, NpcHeroPool(desk), []);
        if (heroID == NoHeroID)
        {
            logger.LogWarning("Desk {DeskID}: no Pokemon left for NPC {FakeUid}", desk.DeskID, npc.Uid);
            return false;
        }

        var showsWayTip = Random.Shared.Next(Percent) < WayTipShowPercent;
        npc.ChoiceHero = BuildNpcChoiceHero(heroID, npc.NpcConfig, NpcHeldItemLevelOf(desk), showsWayTip, now);
        outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(npc, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroChooseHero));
        return true;
    }

    private static void LockNpc(BattleDesk desk, DeskPlayer npc, RoomOutcome outcome)
    {
        npc.ChoiceHero.IsHeroLocked = DeskPlayer.HeroLocked;
        outcome.PushAll(desk.HumanUids, PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(npc, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroLockHero));
    }

    private RoomOutcome? FindPickingHuman(ulong uid, out BattleDesk desk, out DeskPlayer player)
    {
        player = null!;
        if (!desksByUid.TryGetValue(uid, out desk!) || desk.FindHuman(uid) is not { } human)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk);
        }

        player = human;
        return desk.Phase is DeskPhase.Picking or DeskPhase.WaitingRelay
            ? null
            : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrDeskStateInvalid);
    }

    private RoomOutcome? FindLockableHero(ulong uid, int heroID, out BattleDesk desk, out DeskPlayer player)
    {
        if (FindPickingHuman(uid, out desk, out player) is { } refusal)
        {
            return refusal;
        }

        if (player.IsHeroLocked)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrHadLockHero);
        }

        if (player.ChosenHeroID == NoHeroID)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrHasNotChoosePokemon);
        }

        return player.ChosenHeroID == heroID ? null : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrLockHeroNotSameAsChoose);
    }

    private async Task ExpirePickAsync(BattleDesk desk)
    {
        try
        {
            var reminderDelay = TimeSpan.FromSeconds(Math.Max(0, desk.PickTimeOut - desk.PickReminderSec - PickReminderLeadSec));
            await Task.Delay(reminderDelay, clock);
            await RemindPickEndAsync(desk);
            await Task.Delay(TimeSpan.FromSeconds(desk.PickTimeOut) - reminderDelay, clock);
            await CompletePickAsync(desk, DeskPhase.WaitingRelay);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Pick expiry for desk {DeskID} failed", desk.DeskID);
        }
    }

    private async Task RemindPickEndAsync(BattleDesk desk)
    {
        var outcome = new RoomOutcome();
        lock (gate)
        {
            if (!IsCurrent(desk, DeskPhase.Picking))
            {
                return;
            }

            BeginPickWait(desk, outcome);
        }

        logger.LogInformation(
            "Desk {DeskID}: {PickReminderSec} s of the pick left; the relay hand-off follows at the {PickTimeOut} s pick timeout",
            desk.DeskID,
            desk.PickReminderSec,
            desk.PickTimeOut);
        await PushAllAsync(outcome);
    }

    private async Task CompletePickAsync(BattleDesk desk, DeskPhase expectedPhase)
    {
        RoomOutcome outcome;
        bool handedOff;
        ulong[] releasedUids = [];
        lock (gate)
        {
            if (!IsCurrent(desk, expectedPhase))
            {
                return;
            }

            outcome = LockUnlockedHumans(desk);
            LockUnlockedBots(desk, outcome);
            handedOff = desk.AllHumansLocked;
            if (handedOff)
            {
                PickAllies(desk, clock.GetUtcNow().ToUnixTimeSeconds(), outcome);
                HandOffToRelay(desk, outcome);
            }
            else
            {
                releasedUids = Forget(desk);
                outcome = AbortAll(desk.HumanUids, PB_ENUM_RES_ERROR_CODE.PbErrHasNotChoosePokemon);
            }
        }

        logger.LogInformation(
            "Desk {DeskID}: pick ended in phase {Phase}; {Result}",
            desk.DeskID,
            expectedPhase,
            handedOff
                ? $"picks were locked and the desk handed to the relay at {relayRoles.AdvertisedHost}:{relayRoles.AdvertisedPort}"
                : "a trainer had no Pokemon to pick and the desk was aborted");
        if (handedOff)
        {
            RelayHandedOff?.Invoke(desk);
        }

        await PushAllAsync(outcome);
        await RaiseDeskClosedAsync(releasedUids);
    }

    private RoomOutcome LockUnlockedHumans(BattleDesk desk)
    {
        var outcome = new RoomOutcome();
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        foreach (var human in desk.Humans.Where(human => !human.IsHeroLocked))
        {
            if (human.ChosenHeroID == NoHeroID)
            {
                int[] candidates =
                [
                    .. human.CampPlayerInfo.UsableHero.HeroDetail
                        .Select(detail => detail.HeroID)
                        .Where(candidate => !desk.IsHeroTakenInCamp(human.CampClient, candidate, human)),
                ];
                if (candidates.Length == 0)
                {
                    continue;
                }

                var heroID = candidates[Random.Shared.Next(candidates.Length)];
                ChooseHumanHero(desk, human, human.UsableHeroes[heroID], now, outcome);
            }

            human.ChoiceHero.IsHeroLocked = DeskPlayer.HeroLocked;
            outcome.PushAll(desk.HumanUidsOf(human.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(human, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroLockHero));
        }

        return outcome;
    }

    private void LockUnlockedBots(BattleDesk desk, RoomOutcome outcome)
    {
        if (!desk.BotsPickLive)
        {
            return;
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        foreach (var npc in desk.Players.Where(player => player.IsNpc && !player.IsHeroLocked))
        {
            if (npc.ChosenHeroID != NoHeroID || ChooseLiveBot(desk, npc, now, outcome))
            {
                LockNpc(desk, npc, outcome);
            }
        }
    }

    private async Task HandOffAfterPickWaitAsync(BattleDesk desk)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(desk.PickReminderSec), clock);
            await CompletePickAsync(desk, DeskPhase.WaitingRelay);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Relay hand-off for desk {DeskID} failed", desk.DeskID);
        }
    }

    public BattleDesk? FindRelayDesk(ulong uid)
    {
        lock (gate)
        {
            return desksByUid.GetValueOrDefault(uid) is { Phase: DeskPhase.Relay } desk ? desk : null;
        }
    }

    public bool Finish(BattleDesk desk, out ulong[] releasedUids)
    {
        lock (gate)
        {
            if (desksById.GetValueOrDefault(desk.DeskID) != desk)
            {
                releasedUids = [];
                return false;
            }

            releasedUids = Forget(desk);
        }

        logger.LogInformation("Desk {DeskID} finished", desk.DeskID);
        return true;
    }

    private void HandOffToRelay(BattleDesk desk, RoomOutcome outcome)
    {
        desk.Phase = DeskPhase.Relay;
        foreach (var human in desk.Humans)
        {
            var token = RandomNumberGenerator.GetInt32(1, int.MaxValue);
            human.RelayTicket = new RelayTicket(token, relayTickets.Issue(human.Uid, token));
        }

        foreach (var human in desk.Humans)
        {
            outcome.PushAll(desk.HumanUidsOf(human.CampClient), PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf, BuildChoiceHeroChgNtf(human, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceHeroLockHero));
        }

        foreach (var human in desk.Humans)
        {
            outcome.Push(human.Uid, PB_ENUM_CS_MSGID_DEF.PbScidConnRelayNtf, BuildConnRelayNtf(desk, human));
        }
    }

    private async Task OnRoleOfflineAsync(ulong uid)
    {
        BattleDesk? desk;
        var outcome = new RoomOutcome();
        ulong[] releasedUids;
        lock (gate)
        {
            if (!desksByUid.TryGetValue(uid, out desk) || desk.Phase == DeskPhase.Relay)
            {
                return;
            }

            releasedUids = CloseBeforeRelay(desk, uid, outcome);
        }

        logger.LogInformation("Desk {DeskID} closed: uid {Uid} went offline in phase {Phase}", desk.DeskID, uid, desk.Phase);
        await PushAllAsync(outcome);
        await RaiseDeskClosedAsync(releasedUids);
    }

    private ulong[] DropSeat(BattleDesk desk, ulong uid, RoomOutcome outcome)
    {
        if (desk.Phase != DeskPhase.Relay)
        {
            var releasedUids = CloseBeforeRelay(desk, uid, outcome);
            logger.LogInformation("Desk {DeskID} closed: uid {Uid} joined a new desk in phase {Phase}", desk.DeskID, uid, desk.Phase);
            return releasedUids;
        }

        desksByUid.Remove(uid);
        if (desk.FindHuman(uid) is { RelayTicket: { } ticket } human)
        {
            relayTickets.Revoke(ticket.ZSDCode);
            human.RelayTicket = null;
        }

        logger.LogInformation("Desk {DeskID}: uid {Uid} left its relay seat for a new desk", desk.DeskID, uid);
        return [];
    }

    private ulong[] CloseBeforeRelay(BattleDesk desk, ulong leavingUid, RoomOutcome outcome)
    {
        ulong[] others = [.. Forget(desk).Where(other => other != leavingUid)];
        outcome.PushAll(others, PB_ENUM_CS_MSGID_DEF.PbScidMultGameAbortNtf, BuildAbortNtf(PB_ENUM_RES_ERROR_CODE.PbErrMatchAbort));
        return others;
    }

    private bool IsCurrent(BattleDesk desk, DeskPhase phase) =>
        desksById.GetValueOrDefault(desk.DeskID) == desk && desk.Phase == phase;

    private ulong[] Forget(BattleDesk desk)
    {
        foreach (var human in desk.Humans)
        {
            if (human.RelayTicket is { } ticket)
            {
                relayTickets.Revoke(ticket.ZSDCode);
                human.RelayTicket = null;
            }
        }

        desksById.Remove(desk.DeskID);
        ulong[] releasedUids = [.. desk.HumanUids.Where(uid => desksByUid.GetValueOrDefault(uid) == desk)];
        foreach (var uid in releasedUids)
        {
            desksByUid.Remove(uid);
        }

        return releasedUids;
    }

    private Task RaiseDeskClosedAsync(IEnumerable<ulong> releasedUids) =>
        UidEvents.RaiseEachAsync(DeskClosed, releasedUids, logger, nameof(DeskClosed));

    private static RoomOutcome AbortAll(IEnumerable<ulong> uids, PB_ENUM_RES_ERROR_CODE result) =>
        RoomOutcome.Refused(result).PushAll(uids, PB_ENUM_CS_MSGID_DEF.PbScidMultGameAbortNtf, BuildAbortNtf(result));

    private static pbSCMultGameAbortNtf BuildAbortNtf(PB_ENUM_RES_ERROR_CODE result) => new()
    {
        ResultInfo = new pbResultInfo { ResultId = (int)result },
    };

    private Task PushAllAsync(RoomOutcome outcome) => PushAllAsync(outcome.Pushes);

    private async Task PushAllAsync(IEnumerable<RoomPush> pushes)
    {
        foreach (var group in pushes.GroupBy(push => push.Uid))
        {
            var packets = group.Select(push => GamePacket.Create(push.Message.MsgID, push.Message.Message)).ToArray();
            if (!await lobbyRoles.PushAsync(group.Key, packets))
            {
                logger.LogInformation("Desk notices for uid {Uid} were not delivered: the role is offline", group.Key);
            }
        }
    }

    private pbSCMultGameBeginPickNtf BuildBeginPickNtf(BattleDesk desk, DeskPlayer recipient) => new()
    {
        DeskInfo = desk.DeskInfo.Clone(),
        PlayerInfo = new pbCSDT_DESK_PLAYERINFO { CampPlayerInfo = { desk.Players.Select(player => BuildCampPlayerInfoFor(player, recipient)) } },
        LeftSec = desk.PickTimeOut,
        GVoiceInfo = gVoice.BuildGVoiceInfo(),
        OBDeskInfo = desk.BuildOBDesk(),
        CampClient = recipient.CampClient,
        RecentlyUsedPokemonList = new pbCOMDT_RECENTLY_USED_POKEMON_SORT_LIST_INFO(),
        PveChallengeSelectNode = desk.PveChallengeSelectNode?.Clone() ?? new pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE(),
    };

    private static pbCSDT_CAMPPLAYERINFO BuildCampPlayerInfoFor(DeskPlayer player, DeskPlayer recipient)
    {
        var campPlayerInfo = player.CampPlayerInfo.Clone();
        if (!player.IsNpc && player != recipient)
        {
            campPlayerInfo.UsableHero = null;
            campPlayerInfo.UsablePasterList = null;
            campPlayerInfo.UsableTrainerItemList = null;
        }

        return campPlayerInfo;
    }

    private pbSCConnRelayNtf BuildConnRelayNtf(BattleDesk desk, DeskPlayer player) => new()
    {
        RelayTGW = new pbCOMDT_TGWINFO
        {
            IsUDP = RelayIsUDP,
            VPort = relayRoles.AdvertisedPort,
            RelayUrl = relayRoles.AdvertisedHost,
            ZSDCode = player.RelayTicket?.ZSDCode ?? string.Empty,
            IdcTag = gVoice.IdcTag,
            Token = player.RelayTicket?.Token ?? 0,
            GVoiceInfo = gVoice.BuildGVoiceInfo(),
        },
        Camp = RoomNotices.CampIndexOf(player.CampClient),
        DeskInfo = desk.BuildOBDesk(),
        CampClient = player.CampClient,
    };

    public pbSCConnRelayRecoverNtf BuildConnRelayRecoverNtf(BattleDesk desk, DeskPlayer player)
    {
        var connRelay = BuildConnRelayNtf(desk, player);
        return new pbSCConnRelayRecoverNtf
        {
            RelayTGW = connRelay.RelayTGW,
            Camp = connRelay.Camp,
            DeskInfo = connRelay.DeskInfo,
            CampClient = connRelay.CampClient,
            MapID = desk.DeskInfo.MapId,
        };
    }

    private static pbSCChoiceHeroChgNtf BuildChoiceHeroChgNtf(DeskPlayer player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON chgReason) => new()
    {
        Uid = player.Uid,
        ChgReason = (int)chgReason,
        ChoiceHeroInfo = player.ChoiceHero.Clone(),
        CampClient = player.CampClient,
    };

    private static pbSCChoiceHeroChgNtf BuildBroadcastWayTipChgNtf(DeskPlayer player, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON chgReason)
    {
        var ntf = BuildChoiceHeroChgNtf(player, chgReason);
        if (ntf.ChoiceHeroInfo.WayTipType != (int)PB_ENUM_ENM_WAY_TIP_TYPE.PbEnmWayTipTypeNull)
        {
            ntf.ChoiceHeroInfo.WayTipBroad = WayTipBroad;
        }

        return ntf;
    }

    private pbCOMDT_HELDITEM_LIST BuildHeldItemList(IEnumerable<int> heldItemIds, IReadOnlyDictionary<int, int> heldItemLevels) => new()
    {
        HeldItemInfo =
        {
            heldItemIds.Select(heldItemId => new pbCOMDT_HELDITEM_INFO
            {
                HeldItemID = heldItemId,
                HeldItemAttrIndex = HeldItemAttrIndexOf(heldItemId, heldItemLevels.GetValueOrDefault(heldItemId)),
            }),
        },
    };

    private int HeldItemAttrIndexOf(int heldItemId, int level) =>
        gameData.OutSideItemBase.Find(heldItemId) is { } item
        && item.OutSideItemTypeParam.ElementAtOrDefault(HeldItemGroupIdParamIndex) is var groupId
        && levelUpDesignIds.TryGetValue((groupId, Math.Max(FirstHeldItemLevel, level)), out var designId)
            ? designId
            : NoHeldItemAttrIndex;

    private static int PickAcntObjId(HashSet<int> takenAcntObjIds)
    {
        int objId;
        do
        {
            objId = Random.Shared.Next(FirstAcntObjId, LastAcntObjId + 1);
        }
        while (!takenAcntObjIds.Add(objId));

        return objId;
    }

    private sealed record NpcHeldItem(int OutSideItemId, int GroupId);

    private sealed record HumanLoadout(IReadOnlyDictionary<int, int> HeldItemLevels, IReadOnlyList<int> TrainerItemIDs);

    private sealed record HumanProfile(
        pbCOMDT_TRAINER_IMAGE_INFO TrainerImageInfo,
        pbCOMDT_HEAD_ICON_BASE_INFO CurHeadIconInfo,
        IReadOnlyList<pbCOMDT_ACNT_USABLE_HERO_DETAIL> HeroDetails,
        IReadOnlyDictionary<int, pbCOMDT_CHOICEHERO> UsableHeroes,
        HumanLoadout Loadout);
}
