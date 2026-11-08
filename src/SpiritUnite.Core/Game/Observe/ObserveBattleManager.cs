using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Friend;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Settings;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Observe;

public sealed record ObservablePlayer(ulong Uid, int HeroId);

public sealed record ObservableBattle(
    int DeskID,
    long DeskCrtSeq,
    int RelayEntity,
    int MapId,
    long BattleStartTime,
    int ObserveNum,
    IReadOnlyList<ObservablePlayer> Players)
{
    public pbCOMDT_OB_DESK BuildOBDesk() => new() { DeskID = DeskID, DeskCrtSeq = DeskCrtSeq, RelayEntity = RelayEntity };

    public bool Matches(pbCOMDT_OB_DESK desk) => desk.DeskID == DeskID && desk.DeskCrtSeq == DeskCrtSeq && desk.RelayEntity == RelayEntity;
}

public sealed record ObserveChoice(PB_ENUM_RES_ERROR_CODE Result, ObservableBattle? Battle, ulong ObserveUID)
{
    public static ObserveChoice Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null, 0);
}

public sealed class ObserveBattleManager(
    FriendRepository friends,
    SettingsManager settings,
    RoleInfoStore roles,
    ProfileManager profiles,
    RankedManager ranked,
    GameData gameData)
{
    public const int MaxGreatBattleNum = (int)PB_MACRO_COM_MAX_GREAT_BATTLE_NUM.PbComMaxGreatBattleNum;
    public const int NotShowInList = 1;
    public const int ShowInList = 0;

    private const int ObserveBattleSettingIndex = (int)PB_ENUM_ENUM_SYNC_SETTINGS.PbEnmObservebattle;
    private const int ObserveBattleSettingOff = -1;

    private readonly int greatBattleRankedDivision = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalGreatBattleRankedDivision);
    private readonly Lock gate = new();
    private readonly HashSet<ulong> hiddenFromLists = [];

    public int SetObservePrivacy(ulong uid, int notShowInList)
    {
        var hidden = notShowInList == NotShowInList;
        lock (gate)
        {
            if (hidden)
            {
                hiddenFromLists.Add(uid);
            }
            else
            {
                hiddenFromLists.Remove(uid);
            }
        }

        return hidden ? NotShowInList : ShowInList;
    }

    public async Task<pbCOMDT_FRIEND_BATTLE_INFO[]> BuildFriendBattleListAsync(ulong uid, IReadOnlyList<ObservableBattle> battles)
    {
        var friendUids = (await friends.GetFriendsAsync(uid)).Select(friend => friend.FriendUid).ToHashSet();
        var list = new List<pbCOMDT_FRIEND_BATTLE_INFO>();
        foreach (var (battle, player) in ListedPlayers(battles).Where(entry => friendUids.Contains(entry.Player.Uid)))
        {
            if (!await AllowsObserveAsync(player.Uid))
            {
                continue;
            }

            list.Add(new pbCOMDT_FRIEND_BATTLE_INFO
            {
                FriendUid = player.Uid,
                HeroId = player.HeroId,
                ObserveNum = battle.ObserveNum,
                BattleStartTime = battle.BattleStartTime,
                MapId = battle.MapId,
                UserPlantInfo = (await profiles.LoadAsync(player.Uid)).BuildUserPlantInfo(),
            });
        }

        return [.. list];
    }

    public async Task<pbCOMDT_GREAT_BATTLE_INFO[]> BuildGreatBattleListAsync(IReadOnlyList<ObservableBattle> battles)
    {
        var list = new List<pbCOMDT_GREAT_BATTLE_INFO>();
        foreach (var battle in battles)
        {
            if (await FindGreatPlayerAsync(battle) is not ({ } player, var rankedDivision) || await roles.FindAsync(player.Uid) is not { } role)
            {
                continue;
            }

            list.Add(new pbCOMDT_GREAT_BATTLE_INFO
            {
                Uid = player.Uid,
                HeadInfo = RoleBrief.BuildHeadInfo(role),
                RoleName = role.InGameUsrName,
                HeroId = player.HeroId,
                ObserveNum = battle.ObserveNum,
                BattleStartTime = battle.BattleStartTime,
                MapId = battle.MapId,
                Desk = battle.BuildOBDesk(),
                SortValue = rankedDivision,
                UserPlantInfo = (await profiles.LoadAsync(player.Uid)).BuildUserPlantInfo(),
            });
        }

        return [.. list.OrderByDescending(battle => battle.SortValue).Take(MaxGreatBattleNum)];
    }

    public async Task<ObserveChoice> ChooseFriendBattleAsync(ulong uid, ulong friendUid, IReadOnlyList<ObservableBattle> battles)
    {
        if (await friends.FindFriendAsync(uid, friendUid) is null || !await AllowsObserveAsync(friendUid))
        {
            return ObserveChoice.Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveVideoNotExist);
        }

        return ListedPlayers(battles).FirstOrDefault(entry => entry.Player.Uid == friendUid) is ({ } battle, _)
            ? new ObserveChoice(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, battle, friendUid)
            : ObserveChoice.Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveVideoNotExist);
    }

    public async Task<ObserveChoice> ChooseGreatBattleAsync(pbCOMDT_OB_DESK? desk, IReadOnlyList<ObservableBattle> battles)
    {
        if (desk is null || battles.FirstOrDefault(battle => battle.Matches(desk)) is not { } chosen || await FindGreatPlayerAsync(chosen) is not ({ } player, _))
        {
            return ObserveChoice.Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveVideoNotExist);
        }

        return new ObserveChoice(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, chosen, player.Uid);
    }

    private IEnumerable<(ObservableBattle Battle, ObservablePlayer Player)> ListedPlayers(IReadOnlyList<ObservableBattle> battles)
    {
        HashSet<ulong> hidden;
        lock (gate)
        {
            hidden = [.. hiddenFromLists];
        }

        return battles.SelectMany(battle => battle.Players.Where(player => !hidden.Contains(player.Uid)).Select(player => (battle, player)));
    }

    private async Task<(ObservablePlayer? Player, int RankedDivision)> FindGreatPlayerAsync(ObservableBattle battle)
    {
        ObservablePlayer? best = null;
        var bestDivision = 0;
        foreach (var (_, player) in ListedPlayers([battle]))
        {
            await ranked.LoadAsync(player.Uid);
            var division = ranked.PositionOf(player.Uid).RankedDivision;
            if (division >= greatBattleRankedDivision && division > bestDivision && await AllowsObserveAsync(player.Uid))
            {
                best = player;
                bestDivision = division;
            }
        }

        return (best, bestDivision);
    }

    private async Task<bool> AllowsObserveAsync(ulong uid)
    {
        var values = (await settings.LoadAsync(uid)).GameSettingInfo.GameSettingValue;
        return values.Count <= ObserveBattleSettingIndex || values[ObserveBattleSettingIndex] != ObserveBattleSettingOff;
    }
}
