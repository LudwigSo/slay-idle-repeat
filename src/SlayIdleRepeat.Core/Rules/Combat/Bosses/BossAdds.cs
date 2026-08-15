namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// Boss-summoned adds use standard archetypes at 25-35% of boss power (a band — the engine never
/// picks a value inside it, see <see cref="BossScript.AddsPowerFraction"/>), capped at 3 alive.
/// </summary>
internal static class BossAdds
{
    /// <summary>Bottom of the adds' power band.</summary>
    internal const double MinPowerFraction = 0.25;

    /// <summary>Top of the adds' power band.</summary>
    internal const double MaxPowerFraction = 0.35;

    /// <summary>Max adds alive at once.</summary>
    internal const int MaxAlive = 3;
}
