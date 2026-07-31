namespace DeathClipper;

/// <summary>
/// Converts frame-by-frame monitored player state into a single clip request on the death edge.
/// </summary>
internal sealed class DeathDetector
{
    private bool initialized;
    private bool wasDead;
    private bool wasInCombat;
    private bool clippedThisPull;
    private DateTime lastClipUtc = DateTime.MinValue;

    public bool Observe(
        bool playerAvailable,
        bool isDead,
        bool isInCombat,
        DateTime nowUtc,
        Configuration configuration)
    {
        if (!playerAvailable)
        {
            Reset();
            return false;
        }

        if (!initialized)
        {
            initialized = true;
            wasDead = isDead;
            wasInCombat = isInCombat;
            clippedThisPull = false;
            return false;
        }

        if (isInCombat && !wasInCombat)
            clippedThisPull = false;

        if (!isInCombat && wasInCombat)
            clippedThisPull = false;

        var deathEdge = isDead && !wasDead;
        var cooldownElapsed = nowUtc - lastClipUtc >= TimeSpan.FromSeconds(configuration.CooldownSeconds);
        var eligible = configuration.Enabled
                       && deathEdge
                       && (!configuration.OnlyInCombat || isInCombat || wasInCombat)
                       && (!configuration.OncePerPull || !clippedThisPull)
                       && cooldownElapsed;

        wasDead = isDead;
        wasInCombat = isInCombat;

        return eligible;
    }

    public void MarkClipSaved(DateTime nowUtc)
    {
        lastClipUtc = nowUtc;
        clippedThisPull = true;
    }

    public void Reset()
    {
        initialized = false;
        wasDead = false;
        wasInCombat = false;
        clippedThisPull = false;
    }
}
