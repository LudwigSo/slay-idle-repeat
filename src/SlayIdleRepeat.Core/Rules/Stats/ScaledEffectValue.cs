using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Values;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// <c>valueScale</c>, wired into stat aggregation.
/// </summary>
/// <remarks>
/// Lives in <c>Rules/Stats/</c> rather than beside the evaluator because the intra-<c>Rules</c>
/// layering puts <c>Rules.Effects</c> below <c>Rules.Stats</c>, so the evaluator can't name
/// <see cref="IEffectValueReader"/> — the adapter has to live with the interface instead. A superset
/// of <see cref="AuthoredEffectValue"/>, not a different reader: it adds <c>valueScale</c> and keeps
/// that type's refusal of a non-<c>FLAT</c> <c>valueMode</c> on a stat op, since what those modes
/// would mean against a stat (rather than damage or healing) is written nowhere. One context per
/// reader, bound at construction — a reader is built for one resolution pass and discarded, never
/// cached across ticks.
/// </remarks>
internal sealed class ScaledEffectValue : IEffectValueReader
{
    private readonly EffectEvaluationContext _context;

    /// <summary>Binds the reader to one evaluation context.</summary>
    /// <param name="context">The state <c>fn</c> is read against.</param>
    /// <exception cref="ArgumentNullException">The context is null.</exception>
    internal ScaledEffectValue(EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public double EffectiveValue(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        if (effect.ValueMode is { } mode && mode != ValueMode.FLAT)
        {
            throw new EffectContextException(
                effect.Id,
                $"it is a stat op with valueMode {mode}",
                "18 §2.2's value modes are damage- " +
                "and heal-relative, and the only stat op in 18 that carries one is 9.1's " +
                "STAT_SET MAX_HP with FLAT. What the other seven would mean against a stat is written " +
                "nowhere, so it is refused rather than picked. M2-06 owns the value-mode evaluator " +
                "(ValueModeEvaluator, for the damage and healing ops); which ops apply it is M2-03's.");
        }

        return ValueScaleEvaluator.EffectiveValue(effect, _context);
    }
}
