using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.RallyChallenge;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets;

namespace SpiritUnite.Game.Activity;

public sealed class EventBattleRecorder(GameData gameData, RallyChallengeManager rallyChallenge, AllPkmChallengeManager allPkmChallenge)
{
    public async Task<EventBattleSettlementPlan> PrepareSettlementAsync(LMNRoleInfo role, bool won, int mapId, int pokemonId, DateTimeOffset now) => new(
        RallyChallengeManager.IsChallengeBattle(gameData.SceneMap.Find(mapId)) ? await rallyChallenge.PrepareSettlementAsync(role.GameUlongUID, won, now) : null,
        await allPkmChallenge.PrepareSettlementAsync(role, won, pokemonId, now));

    public async Task<GamePacket[]> ApplySettlementAsync(SettlementKey key, EventBattleSettlementPlan plan) =>
    [
        .. plan.Rally is { } rally ? await rallyChallenge.ApplySettlementAsync(key, rally) : [],
        .. plan.AllPokemon is { } allPokemon ? await allPkmChallenge.ApplySettlementAsync(key, allPokemon) : [],
    ];

    public async Task<GamePacket[]> RecordAsync(LMNRoleInfo role, bool won, int mapId, int pokemonId)
    {
        var rallyPushes = RallyChallengeManager.IsChallengeBattle(gameData.SceneMap.Find(mapId))
            ? await rallyChallenge.RecordBattleAsync(role.GameUlongUID, won)
            : [];
        return [.. rallyPushes, .. await allPkmChallenge.RecordBattleAsync(role, won, pokemonId)];
    }
}

public sealed record EventBattleSettlementPlan(RallyChallengeSettlementPlan? Rally, AllPkmChallengeSettlementPlan? AllPokemon);
