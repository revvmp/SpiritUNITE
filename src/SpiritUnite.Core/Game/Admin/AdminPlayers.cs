using System.Globalization;
using SpiritUnite.Auth;
using SpiritUnite.Databin;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Credit;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Online;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Admin;

public sealed record AdminPlayerRow(ulong Uid, string ShortId, string Name, string? UserName, bool IsGuest, bool Online);

public sealed record AdminPlayerPage(int Total, int PageSize, IReadOnlyList<AdminPlayerRow> Players);

public sealed record AdminCoinBalance(int CoinId, long Amount);

public sealed record AdminBan(long BanEndTime, string Reason);

public sealed record AdminPlayerDetail(
    ulong Uid,
    string ShortId,
    string Name,
    string? UserName,
    string? Email,
    bool IsGuest,
    bool Online,
    bool InBattle,
    long? LastLoginTime,
    long CreateTime,
    int Level,
    int Exp,
    int CreditPoint,
    long BattleBanEndTime,
    AdminBan? Ban,
    IReadOnlyList<AdminCoinBalance> Coins,
    IReadOnlyList<int> Pokemon);

public sealed record AdminOverview(int Online, int InBattle, int Trainers, int Accounts, long StartTime);

public sealed class AdminPlayers(
    AccountManager accounts,
    RoleRepository roles,
    RoleInfoStore roleStore,
    WalletManager wallets,
    CreditManager credit,
    CreditPointTaskManager creditPointTasks,
    LobbyRoles lobbyRoles,
    RelayRoles relayRoles,
    GameData gameData,
    TimeProvider clock)
{
    public const int MaximumSearchLength = 64;
    public const int PageSize = 50;
    private const int SearchLimit = 50;
    private const int MaximumBanDays = 36500;
    private const long SecondsPerDay = 86400;

    private readonly long startTime = clock.GetUtcNow().ToUnixTimeSeconds();

    public async Task<AdminOverview> OverviewAsync() => new(
        lobbyRoles.OnlineUids.Count,
        relayRoles.OnlineCount,
        await roles.CountAsync(),
        await accounts.CountAsync(),
        startTime);

    public async Task<AdminPlayerRow[]> ListOnlineAsync()
    {
        var names = await roles.FindNamesAsync([.. lobbyRoles.OnlineUids]);
        return [.. names.OrderBy(role => role.InGameUsrName, StringComparer.OrdinalIgnoreCase).Select(role => ToRow(role.GameUlongUID, role.InGameUsrName, null, false))];
    }

    public async Task<AdminPlayerPage> ListAsync(int page)
    {
        var total = await accounts.CountAsync();
        var profiles = await accounts.ListProfilesAsync(Math.Max(0, page) * PageSize, PageSize);
        var names = (await roles.FindNamesAsync([.. profiles.Select(profile => profile.AccountId)]))
            .ToDictionary(role => role.GameUlongUID, role => role.InGameUsrName);
        return new AdminPlayerPage(
            total,
            PageSize,
            [.. profiles.Select(profile => ToRow(profile.AccountId, names.GetValueOrDefault(profile.AccountId, string.Empty), profile.UserName, profile.IsGuest))]);
    }

    public async Task<AdminPlayerRow[]> SearchAsync(string text)
    {
        var query = text.Trim();
        if (query.Length is 0 or > MaximumSearchLength)
        {
            return [];
        }

        var names = new Dictionary<ulong, string>();
        var profiles = new Dictionary<ulong, AccountProfile>();
        foreach (var uid in ExactUids(query))
        {
            foreach (var role in await roles.FindNamesAsync([uid]))
            {
                names[role.GameUlongUID] = role.InGameUsrName;
            }

            if (await accounts.FindProfileAsync(uid) is { } profile)
            {
                profiles[uid] = profile;
            }
        }

        foreach (var role in await roles.SearchNamesAsync(query, SearchLimit))
        {
            names[role.GameUlongUID] = role.InGameUsrName;
        }

        foreach (var profile in await accounts.SearchProfilesAsync(query, SearchLimit))
        {
            profiles[profile.AccountId] = profile;
        }

        var missingNames = profiles.Keys.Where(uid => !names.ContainsKey(uid)).ToArray();
        foreach (var role in await roles.FindNamesAsync(missingNames))
        {
            names[role.GameUlongUID] = role.InGameUsrName;
        }

        foreach (var uid in names.Keys.Where(uid => !profiles.ContainsKey(uid)).ToArray())
        {
            if (await accounts.FindProfileAsync(uid) is { } profile)
            {
                profiles[uid] = profile;
            }
        }

        return
        [
            .. names.Keys.Union(profiles.Keys)
                .Order()
                .Take(SearchLimit)
                .Select(uid => ToRow(uid, names.GetValueOrDefault(uid, string.Empty), profiles.GetValueOrDefault(uid)?.UserName, profiles.GetValueOrDefault(uid)?.IsGuest ?? false)),
        ];
    }

    public async Task<AdminPlayerDetail?> FindAsync(ulong uid)
    {
        var profile = await accounts.FindProfileAsync(uid);
        var role = roleStore.Find(uid) ?? await roles.FindAsync(uid);
        if (profile is null && role is null)
        {
            return null;
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var ban = await accounts.FindActiveBanAsync(uid, now);
        var wallet = role is null ? null : await wallets.LoadAsync(uid);
        return new AdminPlayerDetail(
            uid,
            ShortIdOf(uid),
            role?.InGameUsrName ?? string.Empty,
            profile?.UserName,
            profile?.Email,
            profile?.IsGuest ?? false,
            lobbyRoles.IsOnline(uid),
            relayRoles.IsOnline(uid),
            profile?.LastLoginAt is { } lastLogin ? new DateTimeOffset(DateTime.SpecifyKind(lastLogin, DateTimeKind.Utc)).ToUnixTimeSeconds() : null,
            role is null ? 0 : await roles.FindCreateTimeAsync(uid),
            role is null ? 0 : RoleProfile.GetLevel(gameData, role),
            role?.RoleTotalExp ?? 0,
            role is null ? 0 : await credit.GetCreditPointAsync(uid),
            role is null ? 0 : await credit.FindBattleBanEndAsync(uid) ?? 0,
            ban is null ? null : new AdminBan(ban.BanEndTime, ban.Reason),
            wallet is null ? [] : [.. wallet.Balances.Where(balance => balance.Value != 0).Select(balance => new AdminCoinBalance(balance.Key, balance.Value)).OrderBy(balance => balance.CoinId)],
            role is null ? [] : [.. role.GetOwnedPokemonIds().Order()]);
    }

    public Task<bool> KickAsync(ulong uid) => KickAsync(uid, kickOffResult: null);

    private async Task<bool> KickAsync(ulong uid, PB_ENUM_RES_ERROR_CODE? kickOffResult)
    {
        var inBattle = relayRoles.IsOnline(uid);
        relayRoles.Close(uid);
        return await lobbyRoles.KickAsync(uid, kickOffResult) || inBattle;
    }

    public static bool IsBanDays(int days) => days is >= 0 and <= MaximumBanDays;

    public async Task<bool> BanAsync(ulong uid, int days, string reason)
    {
        if (!IsBanDays(days))
        {
            return false;
        }

        var banEndTime = days == 0 ? AccountBanState.Permanent : clock.GetUtcNow().ToUnixTimeSeconds() + (days * SecondsPerDay);
        if (!await accounts.BanAsync(uid, new AccountBanState(banEndTime, reason.Trim())))
        {
            return false;
        }

        await KickAsync(uid, PB_ENUM_RES_ERROR_CODE.PbErrKickOffBan);
        return true;
    }

    public Task<bool> UnbanAsync(ulong uid) => accounts.UnbanAsync(uid);

    public async Task<string?> RenameAsync(ulong uid, string name)
    {
        if (await roles.FindAsync(uid) is null)
        {
            return "This account has no trainer yet";
        }

        return await roleStore.RenameAsync(uid, name.Trim()) switch
        {
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc => null,
            PB_ENUM_RES_ERROR_CODE.PbErrRegisterNameDup => "That name is taken",
            PB_ENUM_RES_ERROR_CODE.PbErrRegisterNameNull => "Enter a name",
            _ => "Names are up to 12 characters with at most 5 digits",
        };
    }

    public async Task<bool> SetCreditPointAsync(ulong uid, int creditPoint)
    {
        if (!await credit.SetCreditPointAsync(uid, creditPoint))
        {
            return false;
        }

        await lobbyRoles.PushAsync(uid, await creditPointTasks.SyncAsync(uid));
        return true;
    }

    public Task<bool> ClearBattleBanAsync(ulong uid) => credit.ClearBattleBanAsync(uid);

    private AdminPlayerRow ToRow(ulong uid, string name, string? userName, bool isGuest) =>
        new(uid, ShortIdOf(uid), name, userName, isGuest, lobbyRoles.IsOnline(uid));

    private static string ShortIdOf(ulong uid) => RoleProfile.HasShortId(uid) ? RoleProfile.BuildShortId(uid) : string.Empty;

    private static IEnumerable<ulong> ExactUids(string query)
    {
        if (ulong.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) && uid != 0)
        {
            yield return uid;
        }

        if (RoleProfile.TryParseShortId(query, out var shortIdUid) && shortIdUid != 0)
        {
            yield return shortIdUid;
        }
    }
}
