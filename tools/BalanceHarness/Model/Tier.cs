namespace SlayIdleRepeat.BalanceHarness.Model;

/// <summary>
/// 🔒 `29` §4's three content tiers — the keys <c>tuning/par_power.json#/parPower</c> and
/// <c>content/enemies/enemies.json#/enemyLevel/tierBonus</c> are both authored against.
/// </summary>
/// <remarks>
/// The member names are the authored JSON keys verbatim, so <see cref="object.ToString"/> is the
/// pointer segment and no mapping table is needed. Those two documents are the only files in the
/// repository that key on tier names.
/// </remarks>
public enum Tier
{
    /// <summary>`29` §4 — the base tier, <c>TierMult</c> 1.0.</summary>
    NORMAL,

    /// <summary>`29` §4 — <c>TierMult</c> 4.0.</summary>
    HEROIC,

    /// <summary>`29` §4 — <c>TierMult</c> 16.0.</summary>
    MYTHIC,
}

/// <summary>The three tiers as a set, in authored order.</summary>
public static class Tiers
{
    /// <summary>`29` §4's three tiers, in the order <c>par_power.json</c> writes them.</summary>
    public static IReadOnlyList<Tier> All { get; } = [Tier.NORMAL, Tier.HEROIC, Tier.MYTHIC];
}
