using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;

namespace SlayIdleRepeat.Core.Rules.Effects.Values;

/// <summary>
/// 🔒 `18` §1.1 — an effect's <c>value</c> after <c>valueScale</c>:
/// <c>effectiveValue = value × steps</c>, <c>steps = min( floor( fn / per ), cap )</c>.
/// </summary>
internal static class ValueScaleEvaluator
{
    /// <summary>`18` §1.1's step count for a scale, read against the given state.</summary>
    internal static int Steps(ValueScale scale, EffectEvaluationContext context) =>
        throw new NotImplementedException();

    /// <summary>The effect's value after `18` §1.1's scaling.</summary>
    internal static double EffectiveValue(EffectDefinition effect, EffectEvaluationContext context) =>
        throw new NotImplementedException();
}
