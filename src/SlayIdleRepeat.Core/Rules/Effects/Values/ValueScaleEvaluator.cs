using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;

namespace SlayIdleRepeat.Core.Rules.Effects.Values;

/// <summary>
/// 🔒 `18` §1.1 — an effect's <c>value</c> after <c>valueScale</c>:
/// <c>effectiveValue = value × steps</c>, <c>steps = min( floor( fn / per ), cap )</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>This type owns the wiring and nothing else.</b> The arithmetic is
/// <see cref="ValueScale.StepsFor"/>'s and <see cref="ValueScale.EffectiveValue"/>'s; the state
/// reading is <see cref="ConditionEvaluator.Read"/>'s. What was missing between them — and all that
/// is added here — is the two lines that join them, in the one order `18` §1.1 authorises.
/// </para>
/// <para>
/// 🔒 <b>The reading is rounded once, and not here.</b> <see cref="ConditionEvaluator.Read"/> rounds
/// to four decimal places, which is the accumulation point `18` §1.1 requires <em>before</em> the
/// division, and normalises a negative zero. Its result is handed on <b>unmodified</b>: this
/// evaluator does not round it again and does not divide first. M2-05 pinned that contract by test,
/// and its own remarks name this task as the one that has to honour it — a second rounding is a
/// determinism defect, not belt-and-braces, because it would move the boundary between 44 and 45
/// steps of <c>PK_BERSERK</c> on one platform and not the other (`14` §8.2).
/// </para>
/// <para>
/// `18` §1.1 fixes <em>when</em> this runs, and it is the same moment conditions are evaluated: at
/// every resolution pass for <c>ALWAYS</c> effects, at fire time for triggered ones. Nothing here
/// caches, and nothing here holds state.
/// </para>
/// </remarks>
internal static class ValueScaleEvaluator
{
    /// <summary>
    /// `18` §1.1's <c>steps = min( floor( fn / per ), cap )</c>, read against the given state.
    /// </summary>
    /// <param name="scale">The effect's <c>valueScale</c>.</param>
    /// <param name="context">The state <c>fn</c> is read against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scale"/> or the context is null.</exception>
    /// <exception cref="EffectContextException">
    /// The context does not carry <c>fn</c>'s subject, or the scale names a function that takes an
    /// argument and carries none.
    /// </exception>
    internal static int Steps(ValueScale scale, EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(scale);

        return scale.StepsFor(Reading(scale, context));
    }

    /// <summary>
    /// The effect's value after `18` §1.1's scaling. <c>valueScale: null</c> — the default — means
    /// <c>effectiveValue = value</c>.
    /// </summary>
    /// <param name="effect">The effect.</param>
    /// <param name="context">The state <c>fn</c> is read against.</param>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> or the context is null.</exception>
    /// <exception cref="EffectContextException">
    /// The effect carries no <c>value</c> to scale, or as <see cref="Steps"/>.
    /// </exception>
    internal static double EffectiveValue(EffectDefinition effect, EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);

        // 🔒 The unscaled path returns the authored value UNTOUCHED — not rounded, not normalised.
        // `18` §1.1 says "valueScale: null (the default) means effectiveValue = value", and an
        // evaluator that edited the number on the way past would be a rounding point `05` §1.1 does
        // not list.
        return effect.ValueScale is { } scale
            ? scale.EffectiveValue(Value(effect), Reading(scale, context))
            : Value(effect);
    }

    /// <summary>
    /// 🔒 The single reading, taken through M2-05's evaluator and handed on unmodified.
    /// </summary>
    /// <remarks>
    /// ⚠️ <see cref="ConditionArguments.Of(ValueScale)"/> rather than
    /// <see cref="ConditionArguments.None"/>: `18` §1.1 offers <c>fn</c> <em>"any condition function
    /// from §4"</em> and names <c>STATUS_STACKS</c> and <c>DIE_FACE_COUNT</c>, both of which take an
    /// argument. M2-05 recorded that as a gap in §1.1 and left this seam to close it; M2-06 closed it
    /// by `18` §10's route, and this is the line where the argument arrives.
    /// </remarks>
    private static double Reading(ValueScale scale, EffectEvaluationContext context) =>
        ConditionEvaluator.Read(scale.Fn, ConditionArguments.Of(scale), context);

    /// <summary>
    /// The effect's authored magnitude, refused when there is none.
    /// </summary>
    /// <remarks>
    /// 🔒 A hole, not a zero. `18` §1.1's formula is <c>value × steps</c>, so an absent value makes
    /// every scaled effect a silent no-op however the state reads — the perk is in the build, its
    /// condition passes, its scale counts steps, and it does nothing. Steering S6: fail loudly.
    /// </remarks>
    private static double Value(EffectDefinition effect) =>
        effect.Value ?? throw new EffectContextException(
            effect.Id,
            $"it is a {effect.Op} with no value",
            "18 §1.1's effectiveValue = value x steps " +
            "has nothing to scale, and 18 §2.1's stat ops are magnitudes — an absent value is an " +
            "authoring hole, and reading it as 0 would make the effect a no-op that is still visible " +
            "in the data.");
}
