using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Pokemon;

public sealed record UniteLicenseGrant(
    int? PokemonId,
    int TransmuteCoinID,
    long TransmuteNum,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards)
{
    public void AddTo(
        ICollection<int> pokemonIds,
        IDictionary<int, long> coins,
        List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> awards)
    {
        if (PokemonId is { } pokemonId)
        {
            pokemonIds.Add(pokemonId);
        }

        if (TransmuteNum > 0)
        {
            coins[TransmuteCoinID] = coins.TryGetValue(TransmuteCoinID, out var planned) ? planned + TransmuteNum : TransmuteNum;
        }

        awards.AddRange(Awards);
    }
}

public static class UniteLicense
{
    private const int LicensePokemonParamIndex = 0;
    private const int PokemonPerLicense = 1;
    private const int Replaced = 1;

    public static bool IsUniteLicense(GameData gameData, int resID) => FindLicense(gameData, resID) is not null;

    public static UniteLicenseGrant? PlanGrant(
        GameData gameData,
        LMNRoleInfo role,
        int resID,
        int resNum,
        IReadOnlyCollection<int> plannedPokemonIds)
    {
        if (resNum <= 0 || FindLicense(gameData, resID) is not { } license)
        {
            return null;
        }

        var pokemonId = license.ItemParam[LicensePokemonParamIndex];
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        var grantsPokemon = !role.OwnsPokemon(pokemonId) && !plannedPokemonIds.Contains(pokemonId);
        if (grantsPokemon)
        {
            awards.Add(ReplacedAward(resID, PokemonPerLicense, pokemonId, PokemonPerLicense, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon));
        }

        var transmutedCopies = grantsPokemon ? resNum - PokemonPerLicense : resNum;
        if (transmutedCopies == 0)
        {
            return new UniteLicenseGrant(pokemonId, license.Transmuteid, 0, awards);
        }

        if (license.TransmuteNum <= 0 || !CoinID.IsGrantableCoin(license.Transmuteid))
        {
            return null;
        }

        var transmuteNum = (long)license.TransmuteNum * transmutedCopies;
        awards.Add(ReplacedAward(
            resID,
            transmutedCopies,
            license.Transmuteid,
            (int)Math.Min(transmuteNum, int.MaxValue),
            CoinID.GetCoinResourceType(license.Transmuteid)));
        return new UniteLicenseGrant(grantsPokemon ? pokemonId : null, license.Transmuteid, transmuteNum, awards);
    }

    private static pbOutSideItem_Base? FindLicense(GameData gameData, int resID) =>
        gameData.OutSideItemBase.Find(resID) is { OutSideItemType: (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeEos } license
        && license.ItemParam.Count > LicensePokemonParamIndex
        && license.ItemParam[LicensePokemonParamIndex] > 0
            ? license
            : null;

    private static pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO ReplacedAward(
        int originResID,
        int originResCnt,
        int finallyResID,
        int finallyResCnt,
        PB_ENUM_RES_RESOURCES_TYPE finallyResType) => new()
        {
            IsReplaced = Replaced,
            FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO { ResID = finallyResID, ResCnt = finallyResCnt, ResType = (int)finallyResType },
            OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
            {
                ResID = originResID,
                ResCnt = originResCnt,
                ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem,
            },
        };
}
