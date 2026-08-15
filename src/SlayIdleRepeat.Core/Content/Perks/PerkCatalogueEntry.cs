namespace SlayIdleRepeat.Core.Content.Perks;

/// <summary>
/// 🔒 M3-06 — `06` §1-§3's catalogue row, read out of <c>content/perks/perks.json</c>, shaped for
/// what the draft engine needs (id, category, rarity, tier count) — <b>not</b> `18` §8 step 1's
/// full perk source for <c>EffectSourceCatalogue</c>, which is a separate, richer type M3-07 still
/// owns (<c>SubjectSetFloorTests.Pending</c>'s own <c>PerkDefinition</c> entry, reserved for it and
/// deliberately not this type — see the remarks below for why the two must not collide).
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Named <see cref="PerkCatalogueEntry"/>, not <c>PerkDefinition</c>, and that is a deliberate
/// dodge.</b> <c>PerkDefinition</c> is a reserved simple name:
/// <c>SubjectSetFloorTests.Pending</c> already keys `18` §8 step 1's tenth Effect DSL source
/// ("perks, in draft order") on a type of exactly that name arriving in <c>Core</c>, and treats its
/// arrival as M3-07 shipping the real 98-perk catalogue <c>EffectSourceCatalogue</c> reads. This
/// task's type is narrower — no effect data, no draft-order guarantee <c>EffectSourceCatalogue</c>
/// could rely on — so naming it <c>PerkDefinition</c> would silently discharge that unrelated gate
/// with a shape M3-07 never authored (exactly the "guessed type becomes load-bearing" trap S6
/// exists to catch). Discharges <c>GapRegister</c>'s <c>DraftedPerks</c> entry on the <em>other</em>
/// arm instead — <c>Model.DraftedPerks</c> being authored — see that entry's removal.
/// </para>
/// <para>
/// 🔒 <b>Deliberately does not parse each tier's embedded Effect DSL.</b> `06` §5 forbids per-perk
/// code — a perk's effects are DSL data interpreted by the one generic resolver M2 owns — and the
/// draft engine's whole job is choosing <em>which perk id</em> and <em>which tier</em> a player is
/// offered, never evaluating what a tier's effects do. Reading the full
/// <c>Content.Effects.EffectDefinition</c> per tier here would import a dependency the draft never
/// uses and would duplicate <c>BossCatalogue</c>'s effect reader for no consumer. <see cref="TierCount"/>
/// is the one fact about a perk's tiers the draft needs: how many exist, so Tier III removal
/// (`06` §1.1) has a ceiling to check against even for a future perk authored with fewer than three
/// (`06` §1.1's own text: <em>"some legendaries... cap at Tier II"</em> — none of the 46 rows
/// authored today do, but the field does not assume otherwise).
/// </para>
/// </remarks>
public sealed record PerkCatalogueEntry
{
    /// <summary>The <c>PK_*</c> id, unique across the catalogue.</summary>
    public required string Id { get; init; }

    /// <summary>Player-facing name.</summary>
    public required string Name { get; init; }

    /// <summary>`06` §2's category — the composition-diversity rule's own key.</summary>
    public required PerkCategory Category { get; init; }

    /// <summary>`06` §4's rarity band.</summary>
    public required PerkRarity Rarity { get; init; }

    /// <summary>The icon asset id.</summary>
    public required string IconId { get; init; }

    /// <summary>The player-facing description template — <c>{value}</c> substituted at render time.</summary>
    public required string Description { get; init; }

    /// <summary>
    /// How many internal tiers this perk authors — `06` §1.1 fixes it at 3 for every row today, and
    /// <c>perk.schema.json</c>'s <c>tiers</c> array is <c>minItems: 3, maxItems: 3</c>. Read off the
    /// data rather than assumed, so a future perk authored with fewer (`06` §1.1's own "some
    /// legendaries cap at Tier II") is honoured rather than silently forced to 3.
    /// </summary>
    public required int TierCount { get; init; }

    /// <summary>`06` §5 — perk ids this perk may not be drafted alongside. Empty on every row today.</summary>
    public IReadOnlyList<string> Excludes { get; init; } = [];

    /// <summary>`06` §5 — perk ids that must already be owned before this one may be drafted. Empty today.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>`06` §5 — the fresh-pool draw tags this perk belongs to. Every row today carries <c>["standard"]</c>.</summary>
    public required IReadOnlyList<string> PoolTags { get; init; }
}
