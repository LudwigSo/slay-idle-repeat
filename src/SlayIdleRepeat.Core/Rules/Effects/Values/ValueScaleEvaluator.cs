using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;

namespace SlayIdleRepeat.Core.Rules.Effects.Values;

/// <summary>
/// An effect's <c>value</c> after <c>valueScale</c>: <c>effectiveValue = value × steps</c>,
/// <c>steps = min( floor( fn / per ), cap )</c>.
/// </summary>
/// <remarks>
/// This type owns the wiring and nothing else — the arithmetic belongs to
/// <see cref="ValueScale.StepsFor"/> and <see cref="ValueScale.EffectiveValue"/>, and the state
/// reading to <see cref="ConditionEvaluator.Read"/>. That reading is rounded once, inside
/// <c>Read</c>, and handed on here unmodified — a second rounding would be a determinism defect,
/// since it can shift which step boundary a value falls on differently across platforms. Runs at
/// the same moment conditions are evaluated: every resolution pass for <c>ALWAYS</c> effects, fire
/// time for triggered ones. Nothing here caches or holds state.
/// </remarks>
internal static class ValueScaleEvaluator
{
    /// <summary>
    /// <c>steps = min( floor( fn / per ), cap )</c>, read against the given state.
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
    /// The effect's value after scaling. <c>valueScale: null</c> — the default — means
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

        // The unscaled path returns the authored value untouched — not rounded, not normalised.
        return effect.ValueScale is { } scale
            ? scale.EffectiveValue(Value(effect), Reading(scale, context))
            : Value(effect);
    }

    /// <summary>The single reading, taken through the condition evaluator and handed on unmodified.</summary>
    /// <remarks>
    /// Uses <see cref="ConditionArguments.Of(ValueScale)"/> rather than
    /// <see cref="ConditionArguments.None"/>, since <c>fn</c> can be any condition function including
    /// ones that take an argument (<c>STATUS_STACKS</c>, <c>DIE_FACE_COUNT</c>).
    /// </remarks>
    private static double Reading(ValueScale scale, EffectEvaluationContext context) =>
        ConditionEvaluator.Read(scale.Fn, ConditionArguments.Of(scale), context);

    /// <summary>
    /// The effect's authored magnitude, refused when there is none.
    /// </summary>
    /// <remarks>
    /// A hole, not a zero: an absent value would make every scaled effect a silent no-op however the
    /// state reads — the perk is in the build, its condition passes, its scale counts steps, and it
    /// does nothing.
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
