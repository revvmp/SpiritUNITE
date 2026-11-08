using SpiritUNITE.Databin;
using SpiritUNITE.Game.Badge;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.PokemonAvatar;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Mail;

public sealed class GrantUpdates(GameData gameData, StarRoadManager starRoad)
{
    public async Task<GamePacket[]> BuildAsync(LMNRoleInfo role, IReadOnlyList<GrantOutcome> claims, bool withAwardNotify)
    {
        if (claims.Count == 0)
        {
            return [];
        }

        var packets = new List<GamePacket>();
        foreach (var item in claims.SelectMany(claim => claim.Grant.Items).Where(item => item.ItemType == RoleItemType.AvatarItem))
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateAvatarNtf, new pbSCUpdateAvatarNtf
            {
                UpdateType = (int)PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeGain,
                AvatarInfo = item.ToAvatarInfo(),
            }));
        }

        if (PokemonAvatarManager.BuildGainNtf(claims.SelectMany(claim => claim.Grant.Items)) is { } pokemonAvatarNtf)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonAvatarNtf, pokemonAvatarNtf));
        }

        if (claims.SelectMany(claim => claim.ChangedBagItems).ToArray() is { Length: > 0 } changedBagItems)
        {
            if (changedBagItems.Any(item => item.ItemType == RoleItemType.OutSideItem))
            {
                packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(changedBagItems)));
            }

            if (changedBagItems.Any(item => item.ItemType == RoleItemType.Badge))
            {
                packets.Add(GamePacket.Create(
                    PB_ENUM_CS_MSGID_DEF.PbScidUpdateBadgeNotify,
                    BadgeManager.BuildUpdateBadgeNotify(changedBagItems.Where(item => item.ItemType == RoleItemType.Badge))));
            }
        }

        if (claims.SelectMany(claim => claim.Grant.Items).Any(item => item.ItemType == RoleItemType.LobbyBg))
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGetLobbyBackgroundInfoRsp, new pbSCGetLobbyBackgroundInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                LobbyBackgroundInfo = role.BuildLobbyBackgroundInfo(gameData),
            }));
        }

        var wallet = claims[^1].Wallet;
        var specialCoinsBefore = FindSpecialCoinsBefore(claims);
        foreach (var (coinID, beforeCoinNum) in specialCoinsBefore)
        {
            packets.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, beforeCoinNum, wallet.GetCoinVal(coinID))));
        }

        var pokemonIds = claims.SelectMany(claim => claim.Grant.PokemonIds).ToArray();
        foreach (var pokemonId in pokemonIds)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemonId),
            }));
        }

        if (pokemonIds.Length > 0)
        {
            packets.AddRange(await starRoad.BuildPokemonGainPacketsAsync(role));
            packets.Add(StarRoadManager.BuildAddPokemonRedDot(role));
        }
        else if (specialCoinsBefore.TryGetValue(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, out var licensePointsBefore))
        {
            packets.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, licensePointsBefore));
        }

        if (claims.Any(claim => claim.Grant.Coins.Keys.Any(CoinID.WalletCoins.Contains)))
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() }));
        }

        if (claims.Any(claim => claim.Grant.Coins.ContainsKey(CoinID.Coupons)))
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp()));
        }

        if (withAwardNotify)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, MailManager.BuildResourcesAwardNotify(claims)));
        }

        return [.. packets];
    }

    private static Dictionary<int, long> FindSpecialCoinsBefore(IEnumerable<GrantOutcome> claims)
    {
        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var (coinID, beforeCoinNum) in claims.SelectMany(claim => claim.BeforeCoinNums).Where(coin => CoinID.IsSpecialCoin(coin.Key)))
        {
            specialCoinsBefore.TryAdd(coinID, beforeCoinNum);
        }

        return specialCoinsBefore;
    }
}
