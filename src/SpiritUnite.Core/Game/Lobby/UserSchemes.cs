using Microsoft.Extensions.Options;
using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.Lobby;

public sealed class UserSchemes(GameData gameData, IOptionsMonitor<LobbyOptions> options, TimeProvider clock)
{
    private const int DefaultSchemeId = 1;
    private const long OpenRegisterTime = 0;
    private const long EndlessRegisterTime = long.MaxValue;

    private readonly IReadOnlyList<IGrouping<int, pbUserSchemeSelect>> unrestrictedRowsByBusinessType = gameData.UserSchemeSelect.Rows
        .Where(row => row.UID.All(uid => uid == 0) && row.NationID.All(nationID => nationID == 0))
        .GroupBy(row => row.BusinessType)
        .OrderBy(group => group.Key)
        .ToArray();

    public IReadOnlyList<UserSchemeOptions> Current => options.CurrentValue.UserSchemes ?? Official;

    public UserSchemeOptions[] Official => SelectForRegistrationAt(clock.GetUtcNow());

    public int GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE businessType) =>
        Current.FirstOrDefault(scheme => scheme.BusinessType == (int)businessType)?.SchemeId ?? DefaultSchemeId;

    private UserSchemeOptions[] SelectForRegistrationAt(DateTimeOffset registerTime)
    {
        var registerCfgTime = UTCTimeUtil.ServerTime2CfgTime(registerTime);
        var windowed = new List<UserSchemeOptions>();
        var unwindowed = new List<UserSchemeOptions>();
        foreach (var rows in unrestrictedRowsByBusinessType)
        {
            if (rows.LastOrDefault(row => HasRegisterWindow(row) && IsInRegisterWindow(row, registerCfgTime)) is { } windowedRow)
            {
                windowed.Add(ToOptions(windowedRow));
            }
            else if (rows.FirstOrDefault(row => !HasRegisterWindow(row)) is { } unwindowedRow)
            {
                unwindowed.Add(ToOptions(unwindowedRow));
            }
        }

        return [.. windowed, .. unwindowed];
    }

    private static bool HasRegisterWindow(pbUserSchemeSelect row) =>
        row.RegisterStartTime != OpenRegisterTime || row.RegisterEndTime != OpenRegisterTime;

    private static bool IsInRegisterWindow(pbUserSchemeSelect row, long registerCfgTime) =>
        row.RegisterStartTime <= registerCfgTime
        && registerCfgTime < (row.RegisterEndTime == OpenRegisterTime ? EndlessRegisterTime : row.RegisterEndTime);

    private static UserSchemeOptions ToOptions(pbUserSchemeSelect row) => new() { BusinessType = row.BusinessType, SchemeId = row.SchemeId };
}
