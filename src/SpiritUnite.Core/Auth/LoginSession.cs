namespace SpiritUnite.Auth;

public enum LoginPhase
{
    AwaitingLogin,
    RegistrationRequired,
    AwaitingClientVersion,
    Completing,
    Completed,
    Failed,
}

public sealed class LoginSession
{
    private bool offingRestart;
    private bool firstLoginInToday;

    public LoginPhase Phase { get; private set; } = LoginPhase.AwaitingLogin;
    public int? RegistrationCountryCode { get; private set; }
    public bool AllowsGameplay => Phase is LoginPhase.AwaitingClientVersion or LoginPhase.Completing or LoginPhase.Completed;

    public void RequireRegistration(int countryCode)
    {
        RegistrationCountryCode = countryCode;
        Phase = LoginPhase.RegistrationRequired;
    }

    public void AwaitClientVersion(bool resumesSession, bool isFirstLoginInToday)
    {
        offingRestart = resumesSession;
        firstLoginInToday = isFirstLoginInToday;
        RegistrationCountryCode = null;
        Phase = LoginPhase.AwaitingClientVersion;
    }

    public bool TryBeginCompletion(out bool resumesSession, out bool isFirstLoginInToday)
    {
        resumesSession = offingRestart;
        isFirstLoginInToday = firstLoginInToday;
        if (Phase != LoginPhase.AwaitingClientVersion)
        {
            return false;
        }

        Phase = LoginPhase.Completing;
        return true;
    }

    public void Complete()
    {
        if (Phase != LoginPhase.Completing)
        {
            throw new InvalidOperationException($"Cannot complete login in phase {Phase}");
        }

        Phase = LoginPhase.Completed;
    }

    public void FailCompletion() => Phase = LoginPhase.Failed;

    public void Resume()
    {
        RegistrationCountryCode = null;
        Phase = LoginPhase.Completed;
    }
}
