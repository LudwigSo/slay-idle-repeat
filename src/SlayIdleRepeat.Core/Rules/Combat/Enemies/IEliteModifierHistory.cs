namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// 🔒 The <b>run-scoped</b> memory `05` §6.2's no-repeat rule needs — read <b>and</b> write.
/// </summary>
/// <remarks>
/// <para>
/// `05` §6.2, 🔒: <em>"No Elite may draw the same modifier as the immediately preceding Elite in the
/// same run — redraw on collision."</em> That is one fact about a <em>run</em>, and a battle cannot
/// hold it: two Elite encounters are two battles.
/// </para>
/// <para>
/// 🔒 <b>This is a cross-milestone contract, shaped after <c>IRunStateView</c> and
/// <c>IRunTriggerCounters</c>.</b> `24` §4.10's B2 protection owns the rule and <c>LuckService</c>
/// (M4-01) is what implements it; the run state itself lives on the <c>Run</c> aggregate (M1-05)
/// under the run controller (M3). None of the three exists here, and the M2 kickoff's A4 ruling is
/// that the run layer is <b>declared, not wired</b>. So M2-11 declares this seam, ships
/// <see cref="EliteModifierHistory"/> as the in-<c>Core</c> implementation, and runs both through
/// the shared contract suite in the same change (steering S7).
/// </para>
/// <para>
/// 🔒 <b>What M4-01 must honour, stated plainly, because this is the whole contract.</b>
/// </para>
/// <list type="number">
/// <item><b>One instance per run, handed to every Elite encounter in it.</b> A fresh instance per
/// battle makes <see cref="PreviousEliteModifier"/> permanently <c>null</c>, the redraw never
/// fires, and the rule is silently off while every test of the draw still passes.</item>
/// <item><b><c>null</c> means "no Elite has been fought in this run yet"</b> — a reading, not an
/// error. The first Elite of a run draws from all eight.</item>
/// <item><b><see cref="RecordEliteModifier"/> is called with the modifier that was actually used</b>,
/// once per Elite encounter, after the draw. Recording a rejected redraw would ratchet the
/// exclusion onto the wrong modifier.</item>
/// <item><b>It is the run's snapshot that persists it.</b> Nothing here writes to disk, and there is
/// deliberately no placeholder run controller in M2.</item>
/// <item><b>M4-01 must not fold this into a pity ladder.</b> `05` §6.2 is a hard exclusion of one
/// value, not a probability nudge; <c>24</c>'s pity mechanisms are a different rule over a different
/// subject, and no pity mechanism is invented here.</item>
/// </list>
/// <para>
/// ⚠️ The <c>Run</c> aggregate cannot implement this directly and must not try: `30` §11.4 states
/// that <em>"<c>Model</c> never references <c>Rules</c>"</em> and
/// <c>AccessibilityBoundaryTests.Core_internal_layering_holds</c> enforces it. M3 honours it with a
/// projection on this side of the seam, exactly as <c>RunStateReading</c> is for the read-only view.
/// </para>
/// <para>
/// 🔒 <b>Narrow, and to stay narrow.</b> Two members. `05` §6.2 needs the <em>immediately</em>
/// preceding modifier and nothing else — not a history, not a count, not a per-modifier tally. A
/// wider interface here becomes the forced shape of M4-01's service.
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
    /// <paramref name="modifier"/> is not one of `05` §6.2's eight.
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
    /// <paramref name="previous"/> has a value that is not one of `05` §6.2's eight.
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
