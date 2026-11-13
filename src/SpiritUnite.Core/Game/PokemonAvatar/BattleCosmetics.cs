using SpiritUnite.Game.Pokemon;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.PokemonAvatar;

public sealed record BattleCosmetics(
    IReadOnlyDictionary<int, int> PersonaliseButtonIDs,
    IReadOnlyDictionary<int, int> SprayBuffIDs,
    IReadOnlyDictionary<int, int[]> UsingSkillEffectsExpandPackIDs,
    IReadOnlyDictionary<int, int> GloryAnnounceIDs,
    IReadOnlyDictionary<int, int> WearCardFaceIDs)
{
    public static BattleCosmetics None { get; } = new(
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int[]>(),
        new Dictionary<int, int>(),
        new Dictionary<int, int>());

    public void ApplyTo(pbCOMDT_CHOICEHERO choice, int pokemonId)
    {
        var pasterID = choice.WearPasterID;
        var lookID = pasterID == PokemonManager.NoPaster ? pokemonId : pasterID;
        choice.SkinButtonID = PersonaliseButtonIDs.GetValueOrDefault(lookID, CustomizeManager.DEFAULT_PERSONALISE_BUTTON_ID);
        choice.AnnounceID = GloryAnnounceIDs.GetValueOrDefault(lookID, CustomizeManager.DefaultGloryAnnounceID);
        choice.SprayerID = pasterID == PokemonManager.NoPaster ? PokemonAvatarManager.NoPasterSpray : SprayBuffIDs.GetValueOrDefault(pasterID);
        choice.SKillEffectsExpandPackInfo = new pbCOMDT_USING_SKILL_EFFECTS_EXPAND_PACK_LIST
        {
            SKillEffectsExpandPackList = { UsingSkillEffectsExpandPackIDs.GetValueOrDefault(pasterID, []) },
        };
        choice.WearCardFaceID = WearCardFaceIDs.GetValueOrDefault(pokemonId);
    }
}
