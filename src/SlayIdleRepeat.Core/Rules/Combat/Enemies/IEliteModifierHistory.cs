namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// The run-scoped memory the no-repeat rule needs — read and write.
/// </summary>
/// <remarks>
/// <para>
/// No Elite may draw the same modifier as the immediately preceding Elite in the same run; that is
/// a fact about a run, and a battle cannot hold it, since two Elite encounters are two battles. This
/// declares the seam; <see cref="EliteModifierHistory"/> is the in-<c>Core</c> implementation, and
/// the real run-scoped implementation belongs on the run-controller side as a projection over the
/// run's state, not on whatever service later applies pity/luck rules — those are a different rule
/// over a different subject.
/// </para>
/// <para>
/// Contract for any implementation: one instance per run, handed to every Elite encounter in it (a
/// fresh instance per battle would make <see cref="PreviousEliteModifier"/> permanently <c>null</c>
/// and silently disable the rule); <c>null</c> means no Elite has been fought in this run yet, not
/// an error; <see cref="RecordEliteModifier"/> is called once per encounter with the modifier
/// actually used, after any redraw.
/// </para>
/// <para>
/// Narrow, and to stay narrow: two members, since only the immediately preceding modifier is needed
/// — not a history, not a count, not a per-modifier tally.
/// </para>
/// </remarks>
internal interface IEliteModifierHistory
{
    /// <summary>
    /// The modifier the previous Elite of this run drew, or <c>null</c> when this run has fought no
    /// Elite yet.
    /// </summary>
    EliteModifier? PreviousEliteModifier { get; }

    /// <summary>Records the modifier this Elite encounter actually used.</summary>
    /// <param name="modifier">The drawn modifier, after any redraw.</param>
    void RecordEliteModifier(EliteModifier modifier);
}

/// <summary>
/// The in-<c>Core</c> implementation of <see cref="IEliteModifierHistory"/> — one nullable value.
/// </summary>
/// <remarks>
/// It exists so that <see cref="EliteModifierDraw"/> is exercised against a real implementation
/// rather than only against a test double, and so that M4-01 has a reference to compare its own
/// against through <c>EliteModifierHistoryContract</c> (steering S7). It holds no clock, no draw and
/// no persistence: the run's snapshot carries <see cref="PreviousEliteModifier"/>, and rehydrating
/// is <see cref="Restore"/>.
/// </remarks>
internal sealed class EliteModifierHistory : IEliteModifierHistory
{
    /// <inheritdoc />
    public EliteModifier? PreviousEliteModifier { get; private set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="modifier"/> is not one of the eight.
    /// </exception>
    public void RecordEliteModifier(EliteModifier modifier)
    {
        if (!Enum.IsDefined(modifier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(modifier), modifier,
                "05 §6.2 declares eight Elite Modifiers and this is not one of them. Recording an " +
                "undeclared value would exclude nothing on the next draw, which is the rule quietly " +
                "switching itself off.");
        }

        PreviousEliteModifier = modifier;
    }

    /// <summary>Rehydrates a run's memory from what its snapshot carried.</summary>
    /// <param name="previous">The persisted previous modifier, or <c>null</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="previous"/> has a value that is not one of the eight.
    /// </exception>
    internal static EliteModifierHistory Restore(EliteModifier? previous)
    {
        var history = new EliteModifierHistory();

        if (previous is { } modifier)
        {
            history.RecordEliteModifier(modifier);
        }

        return history;
    }
}
