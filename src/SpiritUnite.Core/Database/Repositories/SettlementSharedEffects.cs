using LinqToDB.Data;

namespace SpiritUnite.Database.Repositories;

public sealed class SettlementSharedEffects : IAsyncDisposable
{
    private readonly List<Func<DataConnection, Task>> effects = [];
    private readonly List<Func<Task>> publications = [];
    private readonly Stack<SemaphoreSlim> heldGates = new();

    public bool IsEmpty => effects.Count == 0;

    public async Task EnlistGatedAsync(SemaphoreSlim gateHeldThroughPublish, Func<DataConnection, Task> effect, Func<Task>? publishAfterCommit = null)
    {
        await gateHeldThroughPublish.WaitAsync();
        heldGates.Push(gateHeldThroughPublish);
        effects.Add(effect);
        if (publishAfterCommit is not null)
        {
            publications.Add(publishAfterCommit);
        }
    }

    internal async Task RunAsync(DataConnection database)
    {
        foreach (var effect in effects)
        {
            await effect(database);
        }
    }

    internal async Task PublishAsync()
    {
        try
        {
            foreach (var publication in publications)
            {
                await publication();
            }
        }
        finally
        {
            ReleaseGates();
        }
    }

    public ValueTask DisposeAsync()
    {
        ReleaseGates();
        return ValueTask.CompletedTask;
    }

    private void ReleaseGates()
    {
        while (heldGates.TryPop(out var gate))
        {
            gate.Release();
        }
    }
}
