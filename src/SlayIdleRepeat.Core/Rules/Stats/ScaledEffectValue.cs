using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// 🔒 `18` §1.1's <c>valueScale</c>, wired into `18` §8's aggregation — the seam M2-07 left open
/// with <see cref="AuthoredEffectValue"/>.
/// </summary>
internal sealed class ScaledEffectValue : IEffectValueReader
{
    /// <summary>Binds the reader to one evaluation context.</summary>
    internal ScaledEffectValue(EffectEvaluationContext context) => throw new NotImplementedException();

    /// <inheritdoc />
    public double EffectiveValue(EffectDefinition effect) => throw new NotImplementedException();
}
