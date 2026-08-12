using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// One stat's change, produced by a `18` §8 step 6 <c>STAT_CONVERT</c>.
/// </summary>
/// <param name="Stat">The stat the conversion writes to.</param>
/// <param name="Amount">
/// The amount added to it. Signed: <c>PK_TURTLE</c>'s "convert X% of DEF into ATK" is two deltas,
/// one negative and one positive, and expressing it as a pair rather than as a mutation is what lets
/// `18` §8's <em>"reads post-step-5 values"</em> be enforced by the pipeline rather than trusted.
/// </param>
internal readonly record struct StatDelta(StatId Stat, double Amount);

/// <summary>
/// 🔒 `18` §8 <b>step 2</b> — <em>"filter by condition, evaluated against current state."</em> The
/// seam <b>M2-05</b> implements.
/// </summary>
/// <remarks>
/// Condition evaluation needs the live fight: `18` §4's functions read HP fractions, enemy counts,
/// battle time, status stacks, gold held and the <c>IS_PVP</c> flag. None of that is the stat
/// pipeline's, so the pipeline asks rather than decides. The default,
/// <see cref="UnconditionalEffectsOnly"/>, is strict on purpose — see its remarks.
/// </remarks>
internal interface IEffectConditionGate
{
    /// <summary>True when the effect's `18` §4 condition holds against current state.</summary>
    bool IsActive(EffectDefinition effect);
}

/// <summary>
/// 🔒 `18` §1.1 / §2.2 — an effect's <em>effective</em> value: its authored <c>value</c> after
/// <c>valueScale</c> and <c>valueMode</c>. The seam <b>M2-06</b> (and, for the stat-op subset,
/// <b>M2-03</b>) implements.
/// </summary>
/// <remarks>
/// <c>valueScale</c> multiplies by a step count read from live state (`18` §1.1), so it is the same
/// dependency <see cref="IEffectConditionGate"/> has. <see cref="ValueScale.EffectiveValue"/> in
/// <c>Content</c> already owns the arithmetic and its rounding order; what is missing here is only
/// the state reading, which is why this is a seam and not a re-implementation.
/// </remarks>
internal interface IEffectValueReader
{
    /// <summary>The effect's value after `18` §1.1's scaling and `18` §2.2's value mode.</summary>
    double EffectiveValue(EffectDefinition effect);
}

/// <summary>
/// 🔒 `18` §8 <b>steps 6 and 9</b> — <c>STAT_CONVERT</c>'s arithmetic and
/// <c>STAT_CAP_OVERRIDE</c>'s cap table. The seam <b>M2-03</b> implements.
/// </summary>
/// <remarks>
/// <para>
/// Both are op <em>behaviour</em>, which `18` §10 and the milestone plan put with the other 43 ops,
/// not with the aggregation order. What the pipeline keeps is the part that is order and rounding:
/// it hands the conversions the frozen post-step-5 block (so <em>"reads post-step-5 values"</em>
/// cannot be got wrong by an implementation that mutates as it goes), applies the returned deltas
/// itself, and rounds once at the end of the step.
/// </para>
/// <para>
/// ⚠️ <b>Neither op is fully authored today, and that is why the default throws rather than
/// no-ops.</b> `18` §2.1 gives <c>STAT_CONVERT</c> as <em>"convert a percentage of stat A into stat
/// B (<c>PK_TURTLE</c>, <c>PK_JUGGERNAUT</c>)"</em> but the effect shape carries one <c>stat</c>
/// key, so which stat is the source and which the destination is unwritten; `18` §2.1 gives
/// <c>STAT_CAP_OVERRIDE</c> as <em>"raise or redirect a stat cap"</em> with exactly one authored
/// <c>capKind</c> (<c>HEAL_CEILING</c>, `18` §7.6) that is not one of `05` §1's six caps, and the
/// only stated redirect — `09`'s <em>Perfect Strike</em>, "crit chance above the 75% cap converts to
/// crit damage at 1:4" — appears in no DSL example at all. Filling either in would be inventing a
/// rule, so the pipeline refuses effects it cannot resolve instead.
/// </para>
/// </remarks>
internal interface IStatOpBehaviour
{
    /// <summary>
    /// `18` §8 step 6 — the deltas the <c>STAT_CONVERT</c> effects produce, applied by the caller in
    /// the order returned.
    /// </summary>
    /// <param name="conversions">The <c>STAT_CONVERT</c> effects, already in effect-id order.</param>
    /// <param name="postAdditive">
    /// The stat block as it stood after step 5. 🔒 Frozen: `18` §8 step 6 reads post-step-5 values,
    /// so a conversion never sees an earlier conversion's output.
    /// </param>
    /// <param name="values">
    /// 🔒 The <em>same</em> value reader steps 4, 5, 7 and 8 use. Handed in rather than left for the
    /// implementation to obtain: a conversion's percentage is an effect <c>value</c> like any other,
    /// and `18` §1.1's <c>valueScale</c> applies to it, so an implementation that built its own
    /// reader would give the aggregation two ways to value an effect with nothing making them agree.
    /// </param>
    IReadOnlyList<StatDelta> Convert(
        IReadOnlyList<EffectDefinition> conversions, ActorStats postAdditive, IEffectValueReader values);

    /// <summary>
    /// `18` §8 step 9 — the cap table after the <c>STAT_CAP_OVERRIDE</c> effects have raised or
    /// redirected it.
    /// </summary>
    /// <param name="overrides">The <c>STAT_CAP_OVERRIDE</c> effects, already in effect-id order.</param>
    /// <param name="declared">The caps as `05` §1 declares them.</param>
    /// <param name="values">The same value reader, for the same reason as <see cref="Convert"/>.</param>
    StatCaps OverrideCaps(
        IReadOnlyList<EffectDefinition> overrides, StatCaps declared, IEffectValueReader values);
}

/// <summary>
/// The three seams of <see cref="StatAggregation"/>, together.
/// </summary>
/// <param name="Conditions">`18` §8 step 2 — M2-05.</param>
/// <param name="Values">`18` §1.1 / §2.2 — M2-06 and M2-03.</param>
/// <param name="Ops">`18` §8 steps 6 and 9 — M2-03.</param>
internal sealed record StatAggregationSeams(
    IEffectConditionGate Conditions,
    IEffectValueReader Values,
    IStatOpBehaviour Ops)
{
    /// <summary>
    /// 🔒 The seam set M2-07 ships: every part of `18` §8 that is not yet authored refuses the
    /// input rather than guessing at it.
    /// </summary>
    /// <remarks>
    /// It is a complete, usable implementation for the whole of `18` §8 that authored content can
    /// reach <em>today</em> — unconditional <c>STAT_ADD_FLAT</c>, <c>STAT_ADD_PCT</c>,
    /// <c>STAT_MULT</c> and <c>STAT_SET</c>, which is `18` §9.1's <c>CP_GLASS_HEART</c>, `18` §7.1's
    /// <c>PK_SHARP_EDGE</c>, `18` §7.6's <c>Avatar of War</c> multiplier and `05` §3.1's
    /// <c>SYS_ENRAGE</c> — and it throws, with the owning task named, on everything else. That is
    /// the shape S6 asks for: a hole that fails loudly rather than a default that looks like an
    /// answer.
    /// </remarks>
    internal static StatAggregationSeams Strict { get; } = new(
        UnconditionalEffectsOnly.Instance,
        AuthoredEffectValue.Instance,
        UnimplementedStatOps.Instance);
}

/// <summary>
/// The `18` §8 step 2 filter M2-07 ships: an effect with no condition is active, and an effect with
/// one is refused.
/// </summary>
/// <remarks>
/// 🔒 <b>Refused, not skipped and not admitted.</b> Both of the quiet answers are wrong in a way
/// that shows up as a balance bug rather than an error: admitting every conditional effect gives
/// <c>PK_EXECUTIONER</c>'s +25% DMG% against a full-health target, and skipping every one deletes
/// <c>PK_BERSERK</c> from the build. M2-05 replaces this with the real evaluator.
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

        return effect.Condition is null
            ? true
            : throw new NotSupportedException(
                $"18 §8 step 2 filters by condition, evaluated against current state, and '{effect.Id}' " +
                "carries one. Evaluating 18 §4's condition functions needs the live fight and is M2-05's; " +
                "M2-07 ships the aggregation order with this seam open. Admitting the effect anyway would " +
                "apply PK_EXECUTIONER against a full-health target, and skipping it would delete " +
                "PK_BERSERK — both are silent balance bugs, so the aggregation refuses instead. Pass a " +
                "StatAggregationSeams with a real IEffectConditionGate.");
    }
}

/// <summary>
/// The `18` §1.1 value reader M2-07 ships: the authored <c>value</c>, unscaled.
/// </summary>
/// <remarks>
/// <para>
/// <c>valueScale</c> is refused because `18` §1.1 evaluates it against live state (<em>"at every
/// resolution pass for <c>ALWAYS</c> effects, at fire time for triggered ones"</em>), which this
/// task does not have. <see cref="ValueScale.EffectiveValue"/> already owns the arithmetic; only the
/// reading is missing.
/// </para>
/// <para>
/// A <c>valueMode</c> other than <c>FLAT</c> is refused for the same reason. `18` §9.1's
/// <c>STAT_SET MAX_HP</c> is the one stat op in the whole document that carries one, and it is
/// <c>FLAT</c>; the other seven modes of `18` §2.2 are damage- and heal-relative (<c>ATK_MULT</c>,
/// <c>TARGET_MAXHP_PCT</c>, <c>OVERHEAL_AMOUNT</c>, …) and what they would mean applied to a stat op
/// is written nowhere.
/// </para>
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
            throw new NotSupportedException(
                $"'{effect.Id}' carries a valueScale, which 18 §1.1 evaluates against live state " +
                "(effectiveValue = value x steps, steps = min(floor(fn / per), cap)). Reading fn is " +
                "M2-05's and the evaluator wiring is M2-06's; ValueScale.EffectiveValue already owns the " +
                "arithmetic. Pass a StatAggregationSeams with a real IEffectValueReader.");
        }

        if (effect.ValueMode is { } mode && mode != ValueMode.FLAT)
        {
            throw new NotSupportedException(
                $"'{effect.Id}' is a stat op with valueMode {mode}. 18 §2.2's value modes are damage- " +
                "and heal-relative, and the only stat op in 18 that carries one is 9.1's " +
                "STAT_SET MAX_HP with FLAT. What the other seven would mean against a stat is written " +
                "nowhere, so M2-07 refuses rather than picking one. Op behaviour is M2-03's.");
        }

        return effect.Value ?? throw new ArgumentException(
            $"'{effect.Id}' is a {effect.Op} with no value. Every stat op in 18 §2.1 is a magnitude; " +
            "an absent one is an authoring hole, and treating it as 0 would make STAT_ADD_* a no-op and " +
            "STAT_MULT a wipe.",
            nameof(effect));
    }
}

/// <summary>
/// The `18` §8 step 6 / step 9 behaviour M2-07 ships: none, stated as a refusal.
/// </summary>
/// <remarks>
/// Both methods are a no-op on an <b>empty</b> input — which is the real state of the content set,
/// since no <c>STAT_CONVERT</c> or <c>STAT_CAP_OVERRIDE</c> effect is authored anywhere yet — and
/// throw the moment one arrives. See <see cref="IStatOpBehaviour"/> for why neither op's semantics
/// can be written from what `18` and `09` authorise today.
/// </remarks>
internal sealed class UnimplementedStatOps : IStatOpBehaviour
{
    /// <summary>The single instance.</summary>
    internal static UnimplementedStatOps Instance { get; } = new();

    private UnimplementedStatOps()
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<StatDelta> Convert(
        IReadOnlyList<EffectDefinition> conversions, ActorStats postAdditive, IEffectValueReader values)
    {
        ArgumentNullException.ThrowIfNull(conversions);

        return conversions.Count == 0
            ? []
            : throw new NotSupportedException(
                $"18 §8 step 6 applies STAT_CONVERT, and " +
                $"{conversions.Count.ToString(CultureInfo.InvariantCulture)} such effect(s) reached the " +
                $"aggregation: {string.Join(", ", conversions.Select(e => e.Id))}. 18 §2.1 describes the " +
                "op as 'convert a percentage of stat A into stat B' while the effect shape carries one " +
                "'stat' key, so which side is the source is unauthored — M2-03 rules on it. M2-07 owns " +
                "the step's position, its post-step-5 reading and its rounding, not its arithmetic.");
    }

    /// <inheritdoc />
    public StatCaps OverrideCaps(
        IReadOnlyList<EffectDefinition> overrides, StatCaps declared, IEffectValueReader values)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(declared);

        return overrides.Count == 0
            ? declared
            : throw new NotSupportedException(
                $"18 §8 step 9 honours STAT_CAP_OVERRIDE, and " +
                $"{overrides.Count.ToString(CultureInfo.InvariantCulture)} such effect(s) reached " +
                $"the aggregation: {string.Join(", ", overrides.Select(e => e.Id))}. The one authored " +
                "capKind is HEAL_CEILING (18 §7.6's Avatar of War), which is not one of 05 §1's six " +
                "caps, and the one authored redirect — 09's Perfect Strike, crit above the 75% cap into " +
                "crit damage at 1:4 — appears in no DSL example. Both are M2-03's to rule on.");
    }
}
