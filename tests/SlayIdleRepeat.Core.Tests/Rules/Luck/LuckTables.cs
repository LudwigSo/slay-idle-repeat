using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// The weighted rarity tables <c>24</c> authors as literals, as <see cref="RarityTable"/>s.
/// </summary>
/// <remarks>
/// Only the three that are stated as numbers. <c>CHEST_STANDARD</c>'s per-item table is
/// <c>DropShare(max(1, highestChapterCleared))</c> (<c>24</c> §4.0a rule 1) rather than a fixed row,
/// so there is no such thing as "the standard chest table" to transcribe and none is invented here.
/// <para>
/// Weights are the documents' own percentages, left unnormalised: a table is normalised by the
/// operation that draws from it, and pre-dividing them here would hide whether the production code
/// normalises at all.
/// </para>
/// </remarks>
internal static class LuckTables
{
    /// <summary><c>24</c> §4.0a — the premium chest's per-item base table: B 20 · A 45 · S 30 · SS 5.</summary>
    internal static RarityTable ChestPremium() => RarityTable.Of(
    [
        new RarityWeight(Rarity.B, 20.0),
        new RarityWeight(Rarity.A, 45.0),
        new RarityWeight(Rarity.S, 30.0),
        new RarityWeight(Rarity.SS, 5.0),
    ]);

    /// <summary><c>24</c> §4.0a — the apex chest's per-item base table: S 50 · SS 50.</summary>
    internal static RarityTable ChestApex() => RarityTable.Of(
    [
        new RarityWeight(Rarity.S, 50.0),
        new RarityWeight(Rarity.SS, 50.0),
    ]);

    /// <summary><c>24</c> §4.5 — the Mount Crate odds: A 70% · S 26% · SS 4%.</summary>
    internal static RarityTable CrateMount() => RarityTable.Of(
    [
        new RarityWeight(Rarity.A, 70.0),
        new RarityWeight(Rarity.S, 26.0),
        new RarityWeight(Rarity.SS, 4.0),
    ]);

    /// <summary>
    /// A degenerate table that can only draw one rarity — a probe, not a balance table.
    /// </summary>
    /// <remarks>
    /// The overshoot rule (<c>24</c> §4.1) says a <em>natural</em> draw that meets a guarantee resets
    /// its counter. Proving that needs a draw whose outcome is known without pity having fired, and
    /// searching a seed for one would make the case depend on a hash the rules are free to change.
    /// A single-row table gets the same outcome from every seed.
    /// </remarks>
    /// <param name="only">The rarity every draw from this table produces.</param>
    internal static RarityTable Only(Rarity only) => RarityTable.Of([new RarityWeight(only, 1.0)]);

    /// <summary>A table that carries no weight at or above <see cref="Rarity.A"/>.</summary>
    /// <remarks>
    /// The refusal probe: flooring this at A leaves nothing to draw, which is the case a resolution
    /// has to reject <em>before</em> drawing so that it consumes no draw index.
    /// </remarks>
    internal static RarityTable BelowA() => RarityTable.Of(
    [
        new RarityWeight(Rarity.C, 60.0),
        new RarityWeight(Rarity.B, 40.0),
    ]);
}
