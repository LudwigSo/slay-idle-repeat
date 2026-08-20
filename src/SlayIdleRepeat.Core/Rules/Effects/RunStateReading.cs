namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>An immutable set of run-state readings — the in-<c>Core</c> implementation of <see cref="IRunStateView"/>.</summary>
/// <remarks>
/// <para>
/// A value, not a fake: the run controller has to hand the evaluator something shaped exactly like
/// this, and the test suite needs a run state it can state literally. Both want the same object,
/// rather than a production shape plus a parallel test-only shape that can drift.
/// </para>
/// <para>
/// <see cref="DistinctPerkCategories"/> is derived, never stored — a record with both a perk table
/// and an independently-set category count could be constructed reporting three perks across five
/// categories with no test catching it. Deriving it removes the state that could disagree.
/// </para>
/// </remarks>
internal sealed record RunStateReading : IRunStateView
{
    /// <summary>Perks held, by category. <see cref="PerkCount"/> and <see cref="DistinctPerkCategories"/> are both read off this one table.</summary>
    /// <remarks>Ordinal comparison — a culture-aware dictionary would match "OFFENSE" to a different key on a Turkish locale.</remarks>
    public IReadOnlyDictionary<string, int> PerksByCategory { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <inheritdoc />
    public int PetCount { get; init; }

    /// <inheritdoc />
    public long GoldHeld { get; init; }

    /// <inheritdoc />
    public int BattlesWonThisRun { get; init; }

    /// <summary><inheritdoc cref="IRunStateView.StageIndex" /></summary>
    /// <remarks>
    /// <c>required</c>, with no default: counters may default to zero (no perks or gold are real
    /// readings of a fresh run), but a positional index has no value that means "forgotten" rather
    /// than "stage 1" — defaulting it would hide a caller that forgot to set it.
    /// </remarks>
    public required int StageIndex { get; init; }

    /// <summary><inheritdoc cref="IRunStateView.Chapter" /></summary>
    /// <remarks><c>required</c>, for the reason on <see cref="StageIndex"/>.</remarks>
    public required int Chapter { get; init; }

    /// <inheritdoc />
    public int Tier { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// A <c>foreach</c> rather than <c>Count(predicate)</c>: read at every resolution pass of every
    /// always-on effect that scales on it, and the LINQ form allocates a delegate on each call.
    /// </remarks>
    public int DistinctPerkCategories
    {
        get
        {
            var categories = 0;

            foreach (var entry in PerksByCategory)
            {
                if (entry.Value > 0)
                {
                    categories++;
                }
            }

            return categories;
        }
    }

    /// <inheritdoc />
    public int PerkCount(string? category)
    {
        if (category is not null)
        {
            return PerksByCategory.TryGetValue(category, out var held) ? held : 0;
        }

        // Accumulated in a long and narrowed once: Enumerable.Sum(int) is checked and would throw
        // OverflowException on a large table, which this layer's readings are not supposed to do.
        var total = 0L;

        foreach (var entry in PerksByCategory)
        {
            total += entry.Value;
        }

        return total is >= int.MinValue and <= int.MaxValue
            ? (int)total
            : throw new EffectContextException(
                "PERK_COUNT",
                $"the run's perk table sums to {total.ToString(System.Globalization.CultureInfo.InvariantCulture)}, which is not a perk count",
                "18 §4 types PERK_COUNT as an int. A table this size is a construction defect, and " +
                "silently truncating it would hand a scaled effect an arbitrary multiplier.");
    }
}
