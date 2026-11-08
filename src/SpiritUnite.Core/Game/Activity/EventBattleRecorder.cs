using SpiritUnite.Databin;
using SpiritUnite.Game.RallyChallenge;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets;

namespace SpiritUnite.Game.Activity;

public sealed class EventBattleRecorder(GameData gameData, RallyChallengeManager rallyChallenge, AllPkmChallengeManager allPkmChallenge)
{
    public async Task<GamePacket[]> RecordAsync(LMNRoleInfo role, bool won, int mapId, int pokemonId)
    {
        var rallyPushes = RallyChallengeManager.IsChallengeBattle(gameData.SceneMap.Find(mapId))
            ? await rallyChallenge.RecordBattleAsync(role.GameUlongUID, won)
            : [];
        return [.. rallyPushes, .. await allPkmChallenge.RecordBattleAsync(role, won, pokemonId)];
    }
}
