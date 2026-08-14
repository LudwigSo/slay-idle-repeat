using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Values;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// 🔒 `18` §1.1's <c>valueScale</c>, wired into `18` §8's aggregation — the seam M2-07 left open with
/// <see cref="AuthoredEffectValue"/>, whose own remarks name this task: <em>"reading <c>fn</c> is
/// M2-05's and the evaluator wiring is M2-06's."</em>
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this sits in <c>Rules/Stats/</c> rather than beside the evaluator.</b> R17 fixes the
/// intra-<c>Rules</c> layering as <c>Rules.Combat → Rules.Stats → Rules.Effects</c>, with
/// <c>Rules.Effects</c> at the bottom — so <see cref="ValueScaleEvaluator"/> cannot name
/// <see cref="IEffectValueReader"/>, which is declared here. The adapter therefore lives with the
/// interface and names downward, which is the permitted direction. Moving the three seam interfaces
/// into <c>Rules/Effects/</c> — where they belong — is <b>M2-02's</b>, and doing it here would
/// three-way conflict with two siblings editing that directory right now.
/// </para>
/// <para>
/// 🔒 <b>A superset of <see cref="AuthoredEffectValue"/>, not a different reader.</b> It adds
/// <c>valueScale</c> and keeps that type's refusal of a non-<c>FLAT</c> <c>valueMode</c> on a stat
/// op — for the reason M2-07 gave, which owning the value-mode evaluator does not change: `18` §2.2's
/// modes are damage- and heal-relative, the only stat op in the whole document that carries one is
/// §9.1's <c>STAT_SET MAX_HP</c> with <c>FLAT</c>, and what the other seven would mean against a stat
/// is written nowhere (steering S6).
/// </para>
/// <para>
/// One context per reader, bound at construction: `18` §1.1 re-evaluates a scale <em>"at every
/// resolution pass"</em>, and `05` §3.1 re-aggregates a boss's stats every second of the enrage — so
/// a reader is built for one pass and discarded, never cached across ticks.
/// </para>
/// </remarks>
internal sealed class ScaledEffectValue : IEffectValueReader
{
    private readonly EffectEvaluationContext _context;

    /// <summary>Binds the reader to one evaluation context.</summary>
    /// <param name="context">The state `18` §1.1's <c>fn</c> is read against.</param>
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
