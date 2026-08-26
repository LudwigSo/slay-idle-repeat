using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// One stat's change, produced by a <c>STAT_CONVERT</c>.
/// </summary>
/// <param name="Stat">The stat the conversion writes to.</param>
/// <param name="Amount">
/// The amount added to it. Signed: "convert X% of DEF into ATK" is two deltas, one negative and one
/// positive, and expressing it as a pair rather than a mutation lets the pipeline enforce that a
/// conversion reads post-step-5 values rather than trusting an implementation to.
/// </param>
internal readonly record struct StatDelta(StatId Stat, double Amount);

/// <summary>
/// <c>STAT_CONVERT</c>'s arithmetic and <c>STAT_CAP_OVERRIDE</c>'s cap table — the one seam of the
/// aggregation pipeline that could not move down to <c>Rules/Effects/</c> alongside its two siblings.
/// </summary>
/// <remarks>
/// The pipeline keeps the order-and-rounding part: it hands conversions the frozen post-step-5
/// block, applies the returned deltas itself, and rounds once at the end of the step. Neither op is
/// fully authored today (which stat converts to which, and what a cap redirect looks like beyond one
/// worked example, are both unwritten), so the default throws rather than guessing. This interface
/// stays in <c>Rules/Stats/</c> rather than moving to <c>Rules/Effects/</c> like its siblings,
/// because its members reference <see cref="ActorStats"/> and <see cref="StatCaps"/> — types that
/// live in this layer — and <c>Rules.Effects</c> sits below <c>Rules.Stats</c> in the intra-<c>Rules</c>
/// layering, so it can't name types from above it. A read-only view seam (as used one directory over
/// for the same problem) or relocating the stat vocabulary itself would both close the gap, but
/// neither is free, so <see cref="StatOpBehaviour"/>'s plumbing stays here while its arithmetic lives
/// with the other stat ops in <c>Rules/Effects/Ops/</c>.
/// </remarks>
internal interface IStatOpBehaviour
{
    /// <summary>
    /// The deltas the <c>STAT_CONVERT</c> effects produce, applied by the caller in the order
    /// returned.
    /// </summary>
    /// <param name="conversions">The <c>STAT_CONVERT</c> effects, already in effect-id order.</param>
    /// <param name="postAdditive">
    /// The stat block as it stood after step 5. Frozen, so a conversion never sees an earlier
    /// conversion's output.
    /// </param>
    /// <param name="values">
    /// The same value reader steps 4, 5, 7 and 8 use — handed in rather than left for the
    /// implementation to obtain, so a conversion's percentage is valued the same way every other
    /// effect value is.
    /// </param>
    IReadOnlyList<StatDelta> Convert(
        IReadOnlyList<EffectDefinition> conversions, ActorStats postAdditive, IEffectValueReader values);

    /// <summary>
    /// The cap table after the <c>STAT_CAP_OVERRIDE</c> effects have raised or redirected it.
    /// </summary>
    /// <param name="overrides">The <c>STAT_CAP_OVERRIDE</c> effects, already in effect-id order.</param>
    /// <param name="declared">The caps as declared before any override.</param>
    /// <param name="values">The same value reader, for the same reason as <see cref="Convert"/>.</param>
    StatCaps OverrideCaps(
        IReadOnlyList<EffectDefinition> overrides, StatCaps declared, IEffectValueReader values);

    /// <summary>
    /// The deltas a redirecting <c>STAT_CAP_OVERRIDE</c> produces once the caps have landed.
    /// </summary>
    /// <remarks>
    /// A cap override can raise a cap (a table change) or redirect it — moving value from one stat
    /// to another (e.g. crit above its cap converting to crit damage), which a cap table alone can't
    /// express. <paramref name="preCap"/> is the block as it stood before caps were applied — the
    /// only place the overshoot still exists — handed in frozen so a redirect reads how far its own
    /// stat overshot and never another redirect's output.
    /// </remarks>
    /// <param name="overrides">The <c>STAT_CAP_OVERRIDE</c> effects, already in effect-id order.</param>
    /// <param name="preCap">The stat block as it stood after step 8, before any cap was applied.</param>
    /// <param name="effective">The cap table <see cref="OverrideCaps"/> returned.</param>
    /// <param name="values">The same value reader, for the same reason as <see cref="Convert"/>.</param>
    IReadOnlyList<StatDelta> RedirectCappedExcess(
        IReadOnlyList<EffectDefinition> overrides,
        ActorStats preCap,
        StatCaps effective,
        IEffectValueReader values);

    /// <summary>
    /// The fraction of Max HP above which the actor cannot be healed, or <c>null</c> where the build
    /// authors none.
    /// </summary>
    /// <remarks>
    /// Not an aggregation step — it bounds healing elsewhere — but lives on this interface anyway as
    /// the third <c>STAT_CAP_OVERRIDE</c> kind, so it shares the same swap point as the other two.
    /// The lowest ceiling wins when several are active, since a second restriction should never be
    /// able to loosen the first.
    /// </remarks>
    /// <param name="overrides">The <c>STAT_CAP_OVERRIDE</c> effects, already in effect-id order.</param>
    /// <param name="values">The same value reader, for the same reason as <see cref="Convert"/>.</param>
    double? HealCeilingFraction(IReadOnlyList<EffectDefinition> overrides, IEffectValueReader values);
}

/// <summary>
/// The three seams of <see cref="StatAggregation"/>, together.
/// </summary>
/// <param name="Conditions">The condition filter.</param>
/// <param name="Values">The effect value reader.</param>
/// <param name="Ops">The <c>STAT_CONVERT</c> / <c>STAT_CAP_OVERRIDE</c> behaviour.</param>
internal sealed record StatAggregationSeams(
    IEffectConditionGate Conditions,
    IEffectValueReader Values,
    IStatOpBehaviour Ops)
{
    /// <summary>
    /// A complete, usable implementation for everything authored content can reach today, which
    /// refuses rather than guesses at what isn't (a conditional effect, or a <c>valueScale</c>).
    /// </summary>
    internal static StatAggregationSeams Strict { get; } = new(
        UnconditionalEffectsOnly.Instance,
        AuthoredEffectValue.Instance,
        StatOpBehaviour.Instance);
}

/// <summary>
/// The default step-2 filter: an ungated effect is active, a context-gated one is inactive, and an
/// ambient-conditional one is refused.
/// </summary>
/// <remarks>
/// <para>
/// The refusal is deliberate, not a gap: admitting every ambient-conditional effect would apply an
/// on-low-HP perk at full health, and skipping every one would delete a conditional perk from the
/// build — both silent balance bugs only the live fight can avoid.
/// </para>
/// <para>
/// A <b>context-gated</b> effect — one whose condition reads the current target or the attacker
/// (<see cref="Effects.Conditions.ConditionSubjects"/>) — is different: the conditional
/// standing-effect bucket's rule gives it an authored answer outside a fight. It contributes only
/// in a context carrying its subject, and this gate has none — so it is inactive here, which is
/// what lets a hero screen compose a build wearing a damage-vs-Elites affix instead of throwing.
/// </para>
/// </remarks>
internal sealed class UnconditionalEffectsOnly : IEffectConditionGate
{
    /// <summary>The single instance.</summary>
    internal static UnconditionalEffectsOnly Instance { get; } = new();

    private UnconditionalEffectsOnly()
    {
    }

    /// <inheritdoc />
    public bool IsActive(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.Condition is null)
        {
            return true;
        }

        var subjects = ConditionSubjects.Of(effect.Condition);
        if (subjects.ReadsTarget || subjects.ReadsAttacker)
        {
            // The bucket's rule, not a guess: with neither subject in hand the gate cannot hold,
            // so the effect contributes nothing here and waits for an attack resolution.
            return false;
        }

        throw new EffectContextException(
            effect.Id,
            "18 §8 step 2 filters by condition, evaluated against current state, and it carries one",
            "Evaluating 18 §4's condition functions needs the live fight and is M2-05's; " +
            "M2-07 ships the aggregation order with this seam open. Admitting the effect anyway would " +
            "apply PK_EXECUTIONER against a full-health target, and skipping it would delete " +
            "PK_BERSERK — both are silent balance bugs, so the aggregation refuses instead. Pass a " +
            "StatAggregationSeams with a real IEffectConditionGate.");
    }
}

/// <summary>
/// The default value reader: the authored <c>value</c>, unscaled.
/// </summary>
/// <remarks>
/// <c>valueScale</c> is refused because it evaluates against live state this default doesn't have —
/// <see cref="ValueScale.EffectiveValue"/> already owns the arithmetic, only the state reading is
/// missing here. A <c>valueMode</c> other than <c>FLAT</c> is refused for the same class of reason:
/// the other modes are damage- and heal-relative, and what they'd mean applied to a stat op is
/// written nowhere.
/// </remarks>
internal sealed class AuthoredEffectValue : IEffectValueReader
{
    /// <summary>The single instance.</summary>
    internal static AuthoredEffectValue Instance { get; } = new();

    private AuthoredEffectValue()
    {
    }

    /// <inheritdoc />
    public double EffectiveValue(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.ValueScale is not null)
        {
            throw new EffectContextException(
                effect.Id,
                "it carries a valueScale, which 18 §1.1 evaluates against live state " +
                "(effectiveValue = value x steps, steps = min(floor(fn / per), cap))",
                "Reading fn is " +
                "M2-05's and the evaluator wiring is M2-06's; ValueScale.EffectiveValue already owns the " +
                "arithmetic. Pass a StatAggregationSeams with a real IEffectValueReader.");
        }

        if (effect.ValueMode is { } mode && mode != ValueMode.FLAT)
        {
            throw new EffectContextException(
                effect.Id,
                $"it is a stat op with valueMode {mode}",
                "18 §2.2's value modes are damage- " +
                "and heal-relative, and the only stat op in 18 that carries one is 9.1's " +
                "STAT_SET MAX_HP with FLAT. What the other seven would mean against a stat is written " +
                "nowhere, so M2-07 refuses rather than picking one. Op behaviour is M2-03's.");
        }

        return effect.Value ?? throw new EffectContextException(
            effect.Id,
            $"it is a {effect.Op} with no value",
            "Every stat op in 18 §2.1 is a magnitude; " +
            "an absent one is an authoring hole, and treating it as 0 would make STAT_ADD_* a no-op and " +
            "STAT_MULT a wipe.");
    }
}
