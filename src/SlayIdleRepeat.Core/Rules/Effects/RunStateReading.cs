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
/// It is a value, not a fake. The balance harness (`05` §9) runs against synthetic state and loads no
/// aggregate; M3's run controller has to hand the evaluator <em>something</em> shaped exactly like
/// this; and the test suite needs a run state it can state literally. All three want the same object,
/// so there is one rather than a production shape plus a parallel test-only shape that can drift.
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
    /// <remarks>Defaults to <c>1</c> — `18` §4's range is <c>1..3</c>, so <c>0</c> is not a stage.</remarks>
    public int StageIndex { get; init; } = 1;

    /// <summary>
    /// <inheritdoc cref="IRunStateView.Chapter" />
    /// </summary>
    /// <remarks>Defaults to <c>1</c>: chapters are numbered from one, so <c>0</c> is not a chapter.</remarks>
    public int Chapter { get; init; } = 1;

    /// <inheritdoc />
    public int Tier { get; init; }

    /// <inheritdoc />
    public int DistinctPerkCategories => PerksByCategory.Count(entry => entry.Value > 0);

    /// <inheritdoc />
    public int PerkCount(string? category)
    {
        if (category is null)
        {
            return PerksByCategory.Sum(entry => entry.Value);
        }

        return PerksByCategory.TryGetValue(category, out var held) ? held : 0;
    }

    /// <inheritdoc />
    public int DieFaceCount(string faceKind)
    {
        ArgumentNullException.ThrowIfNull(faceKind);

        return DieFacesByKind.TryGetValue(faceKind, out var held) ? held : 0;
    }
}
