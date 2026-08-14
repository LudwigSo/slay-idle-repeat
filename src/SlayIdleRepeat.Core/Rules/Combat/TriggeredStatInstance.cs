using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Duration;
using SlayIdleRepeat.Core.Rules.Effects.Stacking;

namespace SlayIdleRepeat.Core.Rules.Combat;

/// <summary>
/// 🔒 M2-R1 — one <b>fired</b> `18` §2.1 stat op's live applications on one target, and the `18` §6
/// bookkeeping that ends it.
/// </summary>
/// <remarks>
/// <para>
/// This is the other half of `18` §8 step 1 that <c>BattleSimulation.RefreshStats</c>' own remarks
/// name as absent: <em>"a triggered effect that has fired and whose duration has not ended … that set
/// is <c>EffectResolver</c>'s (M2-02) over M2-06's <c>EffectStackSet</c>."</em> A firing
/// <c>SYS_ENRAGE</c> or a boss's <c>ON_PHASE_ENTER</c> stat buff does not go through
/// <c>StatAggregation</c> directly — it lands here, on the <b>target</b> it was authored to modify,
/// and <c>RefreshStats</c> reads it back as a synthetic entry in the same aggregation pass that reads
/// <see cref="BattleActor.StandingEffects"/>.
/// </para>
/// <para>
/// 🔒 <b>Modelled on <c>StatusInstance</c>, deliberately.</b> `18` §6 governs a fired stat op's
/// duration and stacking exactly as it governs a status's, and <c>StatusInstance</c> is the repo's one
/// working implementation of that — same immutable <see cref="EffectStackSet"/>, same mutable
/// <see cref="EffectApplication"/> bookkeeping, same reapplication contract: reapplying <b>never
/// re-anchors</b> anything (there is no cadence here to re-anchor, only the duration timer, which
/// <see cref="EffectStackSet.Apply"/>'s <c>RefreshDuration</c> flag already decides).
/// </para>
/// <para>
/// ⚠️ <b>Keyed by the authored effect id, and the id is never synthesised.</b> Unlike the
/// <c>(stat-copy:…)</c> and <c>(status:…)</c> buckets <c>RefreshStats</c> and <c>StatusTimeline</c>
/// build, a fired stat op already carries its own repository-unique `18` §8 id (<c>SYS_ENRAGE</c>,
/// <c>BOSS_THORNMAW_P2_ROOT</c>) — reusing it is what lets a failure message name the real effect
/// (steering S2) instead of a bucket standing in for it, and it cannot collide with an
/// <c>ALWAYS</c>/untriggered effect id on the same actor: every authored id is repository-unique and a
/// triggered effect is never also in <see cref="BattleActor.StandingEffects"/>.
/// </para>
/// </remarks>
internal sealed class TriggeredStatInstance
{
    /// <summary>The firing effect's `18` §8 id — named in every failure (steering S2).</summary>
    internal required string EffectId { get; init; }

    /// <summary>Which `18` §2.1 stat op fired — one of the four <c>RefreshStats</c> reads back.</summary>
    internal required EffectOp Op { get; init; }

    /// <summary>The stat the op names.</summary>
    internal required StatId Stat { get; init; }

    /// <summary>
    /// `18` §6's applications, combined by the effect's stacking mode. Replaced on every
    /// reapplication, because <see cref="EffectStackSet"/> is immutable by design.
    /// </summary>
    internal required EffectStackSet Stacks { get; set; }

    /// <summary>
    /// `18` §6's duration bookkeeping — what <c>DurationEvaluator</c> ends the instance on. Rewritten
    /// by a reapplication that refreshes, which moves the <b>timer</b> only.
    /// </summary>
    internal required EffectApplication Application { get; set; }

    /// <summary>
    /// 🔒 One reapplication: `18` §6's stacking and its duration refresh, and nothing else — the same
    /// split <c>StatusInstance.Reapply</c> keeps.
    /// </summary>
    /// <param name="value">The fire-time, `18` §1.1-scaled value of this application.</param>
    /// <param name="application">The duration bookkeeping this application would install.</param>
    internal StackApplication Reapply(double value, EffectApplication application)
    {
        var applied = Stacks.Apply(value);

        Stacks = applied.Stacks;

        if (applied.RefreshDuration)
        {
            Application = application;
        }

        return applied;
    }
}
