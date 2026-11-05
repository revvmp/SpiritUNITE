using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed class SettingsRepository(DataOptions options)
{
    public async Task<RoleSettings?> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var saved = await database.GetTable<RoleSetting>()
            .FirstOrDefaultAsync(setting => setting.GameUlongUID == gameUlongUID);
        return saved is null ? null : ToRoleSettings(saved);
    }

    public async Task SaveAsync(ulong gameUlongUID, RoleSettings settings)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(ToEntity(gameUlongUID, settings));
    }

    private static RoleSettings ToRoleSettings(RoleSetting saved) => new(
        pbCOMDT_NEWBIE_STATE_INFO.Parser.ParseFrom(saved.ClientBitState),
        pbCOMDT_RED_DOT_SWITCH_INFO.Parser.ParseFrom(saved.RedDotSwitchInfo),
        pbCOMDT_LOBBY_MENU_SORT_INFO.Parser.ParseFrom(saved.LobbyMenuSortInfo),
        saved.QuickSpeakInfo is null ? null : pbCOMDT_QUICK_SPEAK_INFO.Parser.ParseFrom(saved.QuickSpeakInfo),
        pbCOMDT_CLT_GAME_SETTING_INFO.Parser.ParseFrom(saved.GameSettingInfo),
        saved.ABMode,
        saved.CountryCode,
        pbCOMDT_RED_DOT_INFO.Parser.ParseFrom(saved.RedDotInfo),
        pbCOMDT_BUB_TIP_INFO.Parser.ParseFrom(saved.BubTipInfo));

    private static RoleSetting ToEntity(ulong gameUlongUID, RoleSettings settings) => new()
    {
        GameUlongUID = gameUlongUID,
        ClientBitState = settings.ClientBitState.ToByteArray(),
        RedDotSwitchInfo = settings.RedDotSwitchInfo.ToByteArray(),
        LobbyMenuSortInfo = settings.MenuInfo.ToByteArray(),
        QuickSpeakInfo = settings.QuickSpeakInfo?.ToByteArray(),
        GameSettingInfo = settings.GameSettingInfo.ToByteArray(),
        ABMode = settings.ABMode,
        CountryCode = settings.CountryCode,
        RedDotInfo = settings.RedDotInfo.ToByteArray(),
        BubTipInfo = settings.BubTipInfo.ToByteArray(),
    };
}
