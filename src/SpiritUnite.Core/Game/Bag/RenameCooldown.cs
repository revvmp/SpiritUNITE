namespace SpiritUnite.Game.Bag;

public sealed record RenameCooldown(long Now, long CooldownSeconds)
{
    private const long NeverRenamed = 0;

    public int RemainSeconds(long lastRenameTime) =>
        lastRenameTime == NeverRenamed
            ? 0
            : (int)Math.Clamp(lastRenameTime + CooldownSeconds - Now, 0, int.MaxValue);
}
