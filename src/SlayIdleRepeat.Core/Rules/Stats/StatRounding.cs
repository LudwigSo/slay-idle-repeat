using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// The 4-decimal-place rounding rule, in one place.
/// </summary>
/// <remarks>
/// Applied at every accumulation point, not only the final one: rounding only at the end would make
/// the pipeline's arithmetic depend on how many intermediate digits the platform happened to keep in
/// an extended-precision register, which is exactly the class of client/server divergence this rule
/// exists to remove. The trailing normalisation of <c>-0.0</c> to <c>+0.0</c> matters because a stat
/// that drifts a hair below zero would otherwise reach the canonical state writer as a negative
/// zero — which compares equal to <c>0.0</c> in C# but has a different bit pattern, so two states the
/// language calls identical would hash differently. NaN and infinity throw here rather than later:
/// both are far easier to trace from the step and stat that produced them than from a serialization
/// failure layers downstream.
/// </remarks>
internal static class StatRounding
{
    /// <summary>The number of decimal places this rule rounds to.</summary>
    internal const int Decimals = DeterminismRounding.Decimals;

    /// <summary>
    /// Rounds one accumulated stat value to <see cref="Decimals"/> places and normalises
    /// <c>-0.0</c> to <c>+0.0</c>.
    /// </summary>
    /// <param name="value">The accumulated value.</param>
    /// <param name="stat">The stat being accumulated — named in the failure message.</param>
    /// <param name="step">The aggregation step this accumulation point belongs to.</param>
    /// <exception cref="ArithmeticException">
    /// <paramref name="value"/> is NaN or infinite.
    /// </exception>
    internal static double Round(double value, StatId stat, string step)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArithmeticException(
                $"18 §8 {step} produced {value.ToString("R", CultureInfo.InvariantCulture)} for {stat}. " +
                "A stat that is NaN or infinite is an overflow or a 0/0 in this step, not a value to " +
                "round and carry forward — 05 §1.1's determinism rule has nothing to say about it and " +
                "CanonicalStateWriter would refuse it later, naming the serialiser rather than the " +
                "aggregation step that produced it.");
        }

        return DeterminismRounding.Round(value);
    }

    /// <summary>
    /// The same rounding, for an accumulation point that is not a stat: a tick's battle time, an
    /// attack cooldown, an HP fraction, a transient multiplier.
    /// </summary>
    /// <param name="value">The accumulated value.</param>
    /// <remarks>
    /// Does not throw on NaN or infinity, unlike the overload above: a combat transient reaches the
    /// combat log instead, which names the event and tick that carried it — a better message than
    /// this method could write. Delegates to <see cref="DeterminismRounding.Round"/>, the one
    /// statement of the rule; kept as its own name since the combat loop reads better calling a
    /// stat-pipeline verb.
    /// </remarks>
    internal static double Round(double value) => DeterminismRounding.Round(value);

    /// <summary>
    /// True when a value is already in the form <see cref="Round"/> produces: rounded to
    /// <see cref="Decimals"/> places, finite, and not a negative zero.
    /// </summary>
    /// <remarks>
    /// The guard <see cref="ActorStats"/> states over every value it holds, so that "an unstated
    /// stat is a bug" is joined by "an unrounded stat is a bug". NaN answers <c>false</c>, because
    /// <c>Math.Round(NaN, 4) != NaN</c> — deliberately, since a NaN is exactly the value that must
    /// not be waved through.
    /// </remarks>
    internal static bool IsRounded(double value) => DeterminismRounding.IsRounded(value);
}
