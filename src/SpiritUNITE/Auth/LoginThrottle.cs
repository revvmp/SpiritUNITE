using System.Net;
using SpiritUNITE.Network;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Auth;

public readonly record struct LoginAttempt(ulong AccountId, string ClientPartition);

public sealed class LoginThrottle(TimeProvider clock)
{
    private const int MaximumClientFailures = 10;
    private const int AccountFailuresBeforeDelay = 30;
    private const int MaximumTrackedClientLogins = 100_000;
    private const int MaximumTrackedAccounts = 100_000;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AttackedAccountDelay = TimeSpan.FromSeconds(2);

    private readonly FailureWindows<LoginAttempt> clientFailures = new(clock, FailureWindow, MaximumTrackedClientLogins);
    private readonly FailureWindows<ulong> accountFailures = new(clock, FailureWindow, MaximumTrackedAccounts);

    public LoginAttempt? TryBeginAttempt(ulong accountId, IPAddress? clientAddress)
    {
        var attempt = new LoginAttempt(accountId, ClientAddress.Partition(clientAddress));
        return clientFailures.TryAdd(attempt, MaximumClientFailures) ? attempt : null;
    }

    public Task DelayIfAttackedAsync(LoginAttempt attempt, CancellationToken cancellationToken) =>
        accountFailures.Failures(attempt.AccountId) >= AccountFailuresBeforeDelay
            ? Task.Delay(AttackedAccountDelay, clock, cancellationToken)
            : Task.CompletedTask;

    public void RecordFailure(LoginAttempt attempt) => accountFailures.Add(attempt.AccountId);

    public void RecordSuccess(LoginAttempt attempt) => clientFailures.Remove(attempt);

    public void Abandon(LoginAttempt attempt) => clientFailures.Release(attempt);
}
