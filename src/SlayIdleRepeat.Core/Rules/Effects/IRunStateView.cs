namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The read-only run-state contract behind the conditions that read the run rather than the battle:
/// <c>PERK_COUNT</c>, <c>DISTINCT_PERK_CATEGORIES</c>, <c>PET_COUNT</c>, <c>GOLD_HELD</c>,
/// <c>BATTLES_WON_THIS_RUN</c>, <c>STAGE_INDEX</c>, <c>CHAPTER</c> and <c>TIER</c>.
/// </summary>
/// <remarks>
/// <para>
/// A cross-milestone contract: the state it names lives on the <c>Run</c> aggregate and the run
/// controller, neither of which exists yet. Conditions read through this interface so the evaluator
/// can be finished and frozen now, and so later implementations have one shape to satisfy.
/// </para>
/// <para>
/// The <c>Run</c> aggregate cannot implement this directly — the internal layering forbids
/// <c>Model</c> referencing <c>Rules</c>. The contract is honoured by a projection that lives on this
/// side of the seam and reads the aggregate's getters; <see cref="RunStateReading"/> is that shape.
/// </para>
/// <para>
/// Not a port: it declares no I/O and isn't a registered port, just a read-only view of domain state
/// that happens not to be written yet.
/// </para>
/// <para>
/// An effect that reads any of these in a Ghost Duel is a content error, not a runtime one — such
/// clauses are meant to be skipped via <c>IS_PVP</c>. The evaluator fails loudly rather than
/// substituting a zero when a duel context carries no run view.
/// </para>
/// </remarks>
internal interface IRunStateView
{
    /// <summary><c>PERK_COUNT</c> — optionally by category.</summary>
    /// <param name="category">
    /// One of the perk categories, or <c>null</c> for every perk held. Compared ordinally; an
    /// unknown category is <c>0</c>, not an error.
    /// </param>
    int PerkCount(string? category);

    /// <summary><c>DISTINCT_PERK_CATEGORIES</c> — how many perk categories the run holds at least one perk from.</summary>
    int DistinctPerkCategories { get; }

    /// <summary><c>PET_COUNT</c> — how many pets the run has equipped.</summary>
    /// <remarks>
    /// Distinct from the <c>ALL_PETS</c> target, which counts pets present in the current battle's
    /// roster — they agree in an ordinary fight but this one is also answerable outside a battle.
    /// </remarks>
    int PetCount { get; }

    /// <summary><c>GOLD_HELD</c> — the Gold currently held, uncapped.</summary>
    /// <remarks>
    /// <c>long</c> rather than <c>int</c>: a currency balance that silently wraps at 2,147,483,647 in
    /// a game with an uncapped gold-scaling perk is the kind of hole that surfaces once, in
    /// production, on somebody's best run.
    /// <para>
    /// The real ceiling is the <c>double</c> every reading is widened to for evaluation, exact only
    /// to about 1.4 × 10¹³ Gold — above that a 4 dp round-trip can return a non-integer, and above
    /// 2⁵³ the conversion collapses adjacent balances onto one reading. Lossy, but identical on every
    /// platform, so it's a precision limit rather than a determinism break.
    /// </para>
    /// </remarks>
    long GoldHeld { get; }

    /// <summary><c>BATTLES_WON_THIS_RUN</c> — battles won so far in the current run.</summary>
    int BattlesWonThisRun { get; }

    /// <summary><c>STAGE_INDEX</c> — the run's current stage.</summary>
    int StageIndex { get; }

    /// <summary><c>CHAPTER</c> — the run's chapter, as an int.</summary>
    int Chapter { get; }

    /// <summary><c>TIER</c> — the run's difficulty tier.</summary>
    /// <remarks>
    /// No tier enum exists in the repository yet, so this is the tier's ordinal — what a numeric
    /// condition comparison actually needs. When difficulty tiers get a real enum, this becomes its
    /// ordinal and nothing here changes.
    /// </remarks>
    int Tier { get; }
}
