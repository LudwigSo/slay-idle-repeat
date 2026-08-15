namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// The fight-duration band a boss is tuned to, stated as named numbers rather than magic constants.
/// </summary>
/// <remarks>
/// These are balance-harness assertions, not engine invariants — nothing here throws. A fight that
/// runs 8 s is a tuning failure the harness reports against a whole distribution; an engine that
/// refused it would turn a balance finding into a crash mid-run.
/// </remarks>
internal static class BossDurationGuardrails
{
    /// <summary>Bottom of the intended band at par power.</summary>
    internal const double ParMinSeconds = 35.0;

    /// <summary>Top of the intended band at par power.</summary>
    internal const double ParMaxSeconds = 60.0;

    /// <summary>Never below.</summary>
    internal const double HardFloorSeconds = 12.0;

    /// <summary>
    /// Never above. Matches <see cref="BossBuiltIns.EnrageStartDelaySeconds"/> — the ceiling is the
    /// moment the enrage's termination guarantee starts working.
    /// </summary>
    internal const double HardCeilingSeconds = 70.0;
}
