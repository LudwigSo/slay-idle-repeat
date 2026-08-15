using System.Globalization;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// One instance per status id per target, live.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AnchorTick"/> is <c>init</c>-only, making the "never re-anchors the cadence" rule
/// structural rather than a matter of caller discipline. <see cref="Reapply"/> is the one place a
/// reapplication is expressed, and it writes <see cref="Stacks"/> and <see cref="Application"/> and
/// nothing else.
/// </para>
/// <para>
/// The stack set holds each application's per-second contribution, not the raw authored value:
/// applier-side DoT units (e.g. BURN's "% of attacker ATK") are folded in at application, since the
/// applier can be dead by the time a tick lands. Target-side units (e.g. POISON's "% of target Max
/// HP") keep the fraction, since their subject is present at every tick.
/// </para>
/// </remarks>
internal sealed class StatusInstance
{
    /// <summary>The row this instance is of.</summary>
    internal required StatusDefinition Definition { get; init; }

    /// <summary>
    /// The tick of first application, and the cadence's only anchor. Never written again; see the
    /// type remarks.
    /// </summary>
    internal required int AnchorTick { get; init; }

    /// <summary>
    /// The applications, combined by the status's stacking mode. Replaced on every reapplication,
    /// because <see cref="EffectStackSet"/> is immutable by design.
    /// </summary>
    internal required EffectStackSet Stacks { get; set; }

    /// <summary>
    /// Duration bookkeeping — what <c>DurationEvaluator</c> ends the instance on. Rewritten by a
    /// reapplication that refreshes, which moves the timer and not the anchor.
    /// </summary>
    internal required EffectApplication Application { get; set; }

    /// <summary>
    /// The effect id expiries are ordered by, in ascending order. The most recent application's, so
    /// a refreshed instance sorts where its live source puts it.
    /// </summary>
    internal required string SourceEffectId { get; set; }

    /// <summary>
    /// One reapplication: stacking and duration refresh, and nothing else.
    /// </summary>
    /// <param name="value">The application's per-second contribution (see the type remarks).</param>
    /// <param name="application">The duration bookkeeping this application would install.</param>
    /// <param name="sourceEffectId">The applying effect's id.</param>
    /// <returns>What happened — <see cref="StackApplication.StackAdded"/> and the refresh flag.</returns>
    /// <remarks>
    /// The refresh is applied only when <see cref="StackApplication.RefreshDuration"/> asks: adding
    /// stacks and refreshing duration are kept as two separate questions.
    /// </remarks>
    internal StackApplication Reapply(double value, EffectApplication application, string sourceEffectId)
    {
        var applied = Stacks.Apply(value);

        Stacks = applied.Stacks;
        SourceEffectId = sourceEffectId;

        if (applied.RefreshDuration)
        {
            Application = application;
        }

        return applied;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Definition.Id} x{Stacks.Count.ToString(CultureInfo.InvariantCulture)} " +
        $"anchored at tick {AnchorTick.ToString(CultureInfo.InvariantCulture)} " +
        $"from '{SourceEffectId}'";
}
