using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SpiritUnite.Tools;

public sealed class BackgroundOperations(IHostApplicationLifetime lifetime, ILogger<BackgroundOperations> logger) : IHostedService, IDisposable
{
    private readonly Lock gate = new();
    private readonly HashSet<Task> pending = [];
    private readonly CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
    private OperationLifetime state = OperationLifetime.Running;

    private enum OperationLifetime
    {
        Running,
        Stopping,
        Stopped,
    }

    public bool Run(string name, Func<CancellationToken, Task> action, bool completeOnShutdown = false)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (gate)
        {
            if (state == OperationLifetime.Stopped
                || !completeOnShutdown && (state != OperationLifetime.Running || stopping.IsCancellationRequested))
            {
                return false;
            }

            token = completeOnShutdown ? CancellationToken.None : stopping.Token;
            pending.Add(completion.Task);
        }

        _ = ExecuteAsync(name, action, token, completion);
        return true;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            state = OperationLifetime.Stopping;
        }

        try
        {
            await stopping.CancelAsync();
            while (true)
            {
                Task[] remaining;
                lock (gate)
                {
                    if (pending.Count == 0)
                    {
                        state = OperationLifetime.Stopped;
                        return;
                    }

                    remaining = [.. pending];
                }

                await Task.WhenAll(remaining).WaitAsync(cancellationToken);
            }
        }
        finally
        {
            lock (gate)
            {
                state = OperationLifetime.Stopped;
                if (pending.Count > 0)
                {
                    logger.LogWarning("Shutdown ended with {Count} background operations still running", pending.Count);
                }
            }
        }
    }

    public void Dispose() => stopping.Dispose();

    private async Task ExecuteAsync(string name, Func<CancellationToken, Task> action, CancellationToken token, TaskCompletionSource completion)
    {
        try
        {
            await action(token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            logger.LogDebug("Background operation {Operation} stopped", name);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Background operation {Operation} failed", name);
        }
        finally
        {
            lock (gate)
            {
                pending.Remove(completion.Task);
                completion.TrySetResult();
            }
        }
    }
}
