namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// An immutable set of run-state readings — the in-<c>Core</c> implementation of
/// <see cref="IRunStateView"/>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Shipped in the same change as the interface</b>, with the shared contract suite that both
/// this and every later implementation are run through (steering S7: M0's first port shipped without
/// its suite and its two implementations already disagreed).
/// </para>
/// <para>
/// It is a value, not a fake. M2's kickoff sanctions <em>"a read-only run-state view interface with
/// an in-<c>Core</c> test double"</em>; M3's run controller has to hand the evaluator
/// <em>something</em> shaped exactly like this; and the test suite needs a run state it can state
/// literally. Both want the same object, so there is one rather than a production shape plus a
/// parallel test-only shape that can drift.
/// </para>
/// <para>
/// ⚠️ The balance harness (`05` §9) is <b>not</b> among the consumers, although it runs against
/// synthetic state and loads no aggregate. <c>tools/BalanceHarness</c> is a separate assembly and
/// <c>SlayIdleRepeat.Core</c> grants <c>InternalsVisibleTo</c> to <c>SlayIdleRepeat.Core.Tests</c>
/// alone (`30` §11.3), so it can never name an <c>internal</c> type. Recorded because it was offered
/// as a justification once and is a structurally impossible one.
/// </para>
/// <para>
/// 🔒 <b><see cref="DistinctPerkCategories"/> is derived, never stored.</b> A record with both a perk
/// table and an independently-set category count can be constructed reporting three perks across five
/// categories, and every test written against it would still pass. Deriving it removes the state that
/// could disagree.
/// </para>
/// </remarks>
internal sealed record RunStateReading : IRunStateView
{
    /// <summary>
    /// Perks held, by `06` §2 category. <see cref="PerkCount"/> and
    /// <see cref="DistinctPerkCategories"/> are both read off this one table.
    /// </summary>
    /// <remarks>
    /// 🔒 Ordinal comparison, as every id lookup in this repository (`18` §8, `14` §8.2): a
    /// culture-aware dictionary would match <c>"OFFENSE"</c> to a different key on a Turkish locale.
    /// </remarks>
    public IReadOnlyDictionary<string, int> PerksByCategory { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Die faces, by `04` §1 face kind. Read by <see cref="DieFaceCount"/>.</summary>
    public IReadOnlyDictionary<string, int> DieFacesByKind { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <inheritdoc />
    public int PetCount { get; init; }

    /// <inheritdoc />
    public long GoldHeld { get; init; }

    /// <inheritdoc />
    public int BattlesWonThisRun { get; init; }

    /// <summary>
    /// <inheritdoc cref="IRunStateView.StageIndex" />
    /// </summary>
    /// <remarks>
    /// 🔒 <b><c>required</c>, with no default.</b> An earlier draft defaulted this to <c>1</c>,
    /// reasoning that `18` §4's range is <c>1..3</c> so <c>0</c> is not a stage — arguing <em>from</em>
    /// the hole to a filled value, which is the shape steering S6 forbids. It also made the type
    /// inconsistent: two of nine readings would carry a bespoke default and seven the CLR's, so a
    /// caller who forgot the stage got a working stage-1 reading while one who forgot the gold got a
    /// silent zero. The line drawn instead is that <b>counters may default to zero</b> — no perks and
    /// no gold are real readings of a fresh run — and <b>positional indices may not</b>.
    /// </remarks>
    public required int StageIndex { get; init; }

    /// <summary>
    /// <inheritdoc cref="IRunStateView.Chapter" />
    /// </summary>
    /// <remarks><c>required</c>, for the reason on <see cref="StageIndex"/>.</remarks>
    public required int Chapter { get; init; }

    /// <inheritdoc />
    public int Tier { get; init; }

    /// <inheritdoc />
    /// <remarks>
    /// A <c>foreach</c> rather than <c>Count(predicate)</c>: this is read at every resolution pass of
    /// every <c>ALWAYS</c> effect that scales on it (`PK_ARSENAL`, `18` §1.1), and the LINQ form boxes
    /// a dictionary enumerator and allocates a delegate on each one.
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

        // 🔒 Accumulated in a `long` and narrowed once. Enumerable.Sum(int) is CHECKED, so a perk
        // table summing past int.MaxValue would raise an OverflowException — an exception type from
        // outside the two this layer declares, out of a read that is supposed to be a pure reading.
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

    /// <inheritdoc />
    public int DieFaceCount(string faceKind)
    {
        ArgumentNullException.ThrowIfNull(faceKind);

        return DieFacesByKind.TryGetValue(faceKind, out var held) ? held : 0;
    }
}
