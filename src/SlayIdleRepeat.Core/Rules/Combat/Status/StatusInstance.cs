using System.Globalization;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;

namespace SlayIdleRepeat.Core.Rules.Combat.Status;

/// <summary>
/// 🔒 `05` §3.1 — <em>"one instance per <c>statusId</c> per target"</em>, live.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b><see cref="AnchorTick"/> is <c>init</c>-only, and that is the cadence rule made structural.</b>
/// `05` §3.1: reapplication <em>"adds stacks / refreshes duration per the status's stacking rule
/// (`18` §6) but <b>never re-anchors the cadence</b>."</em> A settable anchor would put that rule in
/// every caller's discipline; an <c>init</c>-only one makes re-anchoring a compile error, which is
/// the only form of the rule that cannot be forgotten in a later edit. <see cref="Reapply"/> is the
/// one place a reapplication is expressed and it writes <see cref="Stacks"/> and
/// <see cref="Application"/> and nothing else.
/// </para>
/// <para>
/// 🔒 <b>The stack set holds each application's per-second <em>contribution</em>, not the raw
/// authored <c>X</c>.</b> `05` §5 states two of its DoT units against the <b>applier</b>
/// (<c>BURN</c>'s <em>"X% of attacker ATK"</em>, <c>BLEED</c>'s <em>"set at application as X% of the
/// applier's ATK"</em>), and the applier can be dead by the time a tick lands — so the applier-side
/// half of the basis is folded in at application, where it is knowable, and `05` §3.1's
/// <em>"potency was fixed at application"</em> is that fact. The target-side units
/// (<c>POISON</c>'s and <c>REGEN</c>'s <em>"% of target Max HP"</em>) keep the fraction, because
/// their subject is the actor carrying the status and is present at every tick.
/// </para>
/// </remarks>
internal sealed class StatusInstance
{
    /// <summary>The `05` §5 row this instance is of.</summary>
    internal required StatusDefinition Definition { get; init; }

    /// <summary>
    /// 🔒 `05` §3.1 — the tick of <b>first</b> application, and the cadence's only anchor. Never
    /// written again; see the type remarks.
    /// </summary>
    internal required int AnchorTick { get; init; }

    /// <summary>
    /// `18` §6's applications, combined by the status's stacking mode. Replaced on every
    /// reapplication, because <see cref="EffectStackSet"/> is immutable by design.
    /// </summary>
    internal required EffectStackSet Stacks { get; set; }

    /// <summary>
    /// `18` §6's duration bookkeeping — what <c>DurationEvaluator</c> ends the instance on.
    /// Rewritten by a reapplication that refreshes, which moves the <b>timer</b> and not the anchor.
    /// </summary>
    internal required EffectApplication Application { get; set; }

    /// <summary>
    /// The `18` §8 effect id `05` §3.1 slot 2 orders expiries by — <em>"in ascending effect-id
    /// order"</em>. The most recent application's, so that a refreshed instance sorts where its live
    /// source puts it.
    /// </summary>
    internal required string SourceEffectId { get; set; }

    /// <summary>
    /// 🔒 One reapplication: `18` §6's stacking, and `18` §6's duration refresh, and <b>nothing
    /// else</b>.
    /// </summary>
    /// <param name="value">The application's per-second contribution (see the type remarks).</param>
    /// <param name="application">The duration bookkeeping this application would install.</param>
    /// <param name="sourceEffectId">The applying effect's `18` §8 id.</param>
    /// <returns>What `18` §6 did — <see cref="StackApplication.StackAdded"/> and the refresh flag.</returns>
    /// <remarks>
    /// 🔒 The refresh is applied only when <see cref="StackApplication.RefreshDuration"/> asks.
    /// `05` §3.1 keeps the two questions apart — <em>"reapplication adds stacks / refreshes
    /// duration"</em>, not "adds stacks and therefore refreshes" — and M2-06's
    /// <c>EffectStackSet.Apply</c> is what answers each.
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
