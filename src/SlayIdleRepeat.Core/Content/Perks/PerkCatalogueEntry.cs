namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>
/// A catalogue row, read out of <c>content/perks/perks.json</c>, shaped for what the draft engine
/// needs (id, category, rarity, tier count) — not the full, richer perk source a future effect
/// catalogue reads.
/// </summary>
/// <remarks>
/// <para>
/// Named <see cref="PerkCatalogueEntry"/> and not <c>PerkDefinition</c> deliberately:
/// <c>PerkDefinition</c> is a reserved name that a test keys the arrival of the real, full perk
/// catalogue on, and this narrower type (no effect data, no draft-order guarantee) must not
/// silently discharge that unrelated gate.
/// </para>
/// <para>
/// Deliberately does not parse each tier's embedded Effect DSL: the draft engine's whole job is
/// choosing which perk id and which tier a player is offered, never evaluating what a tier's
/// effects do. <see cref="TierCount"/> is the one fact about a perk's tiers the draft needs.
/// </para>
/// </remarks>
public sealed record PerkCatalogueEntry
{
    /// <summary>The <c>PK_*</c> id, unique across the catalogue.</summary>
    public required string Id { get; init; }

    /// <summary>Player-facing name.</summary>
    public required string Name { get; init; }

    /// <summary>The category — the composition-diversity rule's own key.</summary>
    public required PerkCategory Category { get; init; }

    /// <summary>The rarity band.</summary>
    public required PerkRarity Rarity { get; init; }

    /// <summary>The icon asset id.</summary>
    public required string IconId { get; init; }

    /// <summary>The player-facing description template — <c>{value}</c> substituted at render time.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// How many internal tiers this perk authors. Read off the data rather than assumed, so a
    /// future perk authored with fewer than three is honoured rather than silently forced to 3.
    /// </summary>
    public required int TierCount { get; init; }

    /// <summary>Perk ids this perk may not be drafted alongside. Empty on every row today.</summary>
    public IReadOnlyList<string> Excludes { get; init; } = [];

    /// <summary>Perk ids that must already be owned before this one may be drafted. Empty today.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>The fresh-pool draw tags this perk belongs to. Every row today carries <c>["standard"]</c>.</summary>
    public required IReadOnlyList<string> PoolTags { get; init; }
}
