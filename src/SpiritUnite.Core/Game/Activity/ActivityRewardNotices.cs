using SpiritUnite.Game.Bag;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.StarRoad;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Activity;

public sealed class ActivityRewardNotices(StarRoadManager starRoad)
{
    public async Task<GamePacket[]> BuildAsync(LMNRoleInfo role, ActivityRewardGroup? rewards)
    {
        if (rewards is null)
        {
            return [];
        }

        var packets = new List<GamePacket>();
        foreach (var (coinID, beforeCoinNum) in rewards.SpecialCoinsBefore)
        {
            packets.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, beforeCoinNum, rewards.Wallet.GetCoinVal(coinID))));
        }

        packets.AddRange(rewards.Wallet.BuildCoinValPushes(rewards.ChangedCoinIDs));

        if (rewards.ChangedItems.Count > 0)
        {
            packets.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify,
                BagManager.BuildOutSideItemUpdateNotify(rewards.ChangedItems)));
        }

        packets.AddRange(PokemonAvatarManager.BuildGainPushes(rewards.Items));
        foreach (var pokemonId in rewards.PokemonIds)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemonId),
            }));
        }

        if (rewards.PokemonIds.Count > 0)
        {
            packets.AddRange(await starRoad.BuildPokemonGainPacketsAsync(role));
            packets.Add(StarRoadManager.BuildAddPokemonRedDot(role));
        }
        else if (rewards.SpecialCoinsBefore.TryGetValue(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, out var licensePointsBefore))
        {
            packets.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, licensePointsBefore));
        }

        return [.. packets];
    }
}
