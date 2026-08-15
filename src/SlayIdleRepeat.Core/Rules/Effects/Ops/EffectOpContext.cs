using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// Everything one op resolution needs beyond the effect itself: the evaluation context, the seams it
/// writes through, and the three readings that exist only inside the moment that fired it.
/// </summary>
/// <remarks>
/// A wrapper rather than three more properties on <see cref="EffectEvaluationContext"/>: that record
/// is state a condition or target reads, held to being a pure function of current state. The three
/// fields below are event arguments, not state — only a few value modes ever read them, and widening
/// the shared context would put a heal's overheal in front of every condition evaluation in the game.
/// Where a trigger did not supply one, it stays null and the value mode that needs it throws.
/// </remarks>
internal sealed record EffectOpContext
{
    /// <summary>The evaluation state — the holder, the roster, the target, the draw stream.</summary>
    public required EffectEvaluationContext Evaluation { get; init; }

    /// <summary>Where the op's number goes. <see cref="EffectOpSeams.Strict"/> refuses everything.</summary>
    public required EffectOpSeams Seams { get; init; }

    /// <summary><c>DAMAGE_DEALT_PCT</c>'s basis — the damage the firing event just dealt. <c>null</c> outside a damage context.</summary>
    /// <remarks>
    /// The post-mitigation, post-floor hit before ward absorption — a lifesteal attacker still heals
    /// off a fully-warded hit, so a leech reading the post-absorption number would heal nothing off a
    /// shielded target.
    /// </remarks>
    public double? DamageDealt { get; init; }

    /// <summary><c>HEAL_AMOUNT</c>'s basis — the full amount actually healed. <c>null</c> outside an <c>ON_HEAL</c> context.</summary>
    public double? HealAmount { get; init; }

    /// <summary><c>OVERHEAL_AMOUNT</c>'s basis — the clipped excess. <c>null</c> outside an <c>ON_HEAL</c> context.</summary>
    public double? OverhealAmount { get; init; }

    /// <summary>The actor holding the effect — the source of every op's number.</summary>
    public IEffectActorView Holder => Evaluation.Holder;
}

/// <summary>4-decimal-place combat rounding, as the op layer names it — the failure message, not a second statement of the rule.</summary>
/// <remarks>
/// The actual rounding lives in <see cref="DeterminismRounding"/>, shared with the stat layer, so
/// this layer can round without naming <c>Rules.Stats</c> directly (which the intra-<c>Rules</c>
/// layering forbids). What's left here is the refusal — see <see cref="RequireFinite"/> — since a NaN
/// or infinity is not a rounding question, and the useful thing to say about one is which effect
/// produced it.
/// </remarks>
internal static class OpRounding
{
    /// <summary>The number of decimal places locked — an alias of <see cref="DeterminismRounding.Decimals"/>, not a copy.</summary>
    internal const int Decimals = DeterminismRounding.Decimals;

    /// <summary>Rounds one accumulated combat number and normalises <c>-0.0</c> to <c>+0.0</c>.</summary>
    /// <param name="value">The accumulated value.</param>
    /// <param name="effectId">The effect that produced it — named in the failure message.</param>
    /// <param name="what">What the number is, in the reader's terms.</param>
    /// <exception cref="EffectContextException"><paramref name="value"/> is NaN or infinite.</exception>
    internal static double Round(double value, string effectId, string what)
    {
        RequireFinite(value, effectId, what);

        return DeterminismRounding.Round(value);
    }

    /// <summary>The refusal itself, stated once — every combat number that reaches a log, a stat or an HP bar passes this.</summary>
    /// <remarks>
    /// The damage pipeline needs its own copy of this guard too, since it's reachable without passing
    /// through this layer at all — but it forwards to this one rather than restating the rule.
    /// </remarks>
    /// <param name="value">The number.</param>
    /// <param name="effectId">The effect that produced it — named in the failure message.</param>
    /// <param name="what">What the number is, in the reader's terms.</param>
    /// <exception cref="EffectContextException"><paramref name="value"/> is NaN or infinite.</exception>
    internal static void RequireFinite(double value, string effectId, string what)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new EffectContextException(
                effectId,
                $"its {what} came out as {value.ToString("R", CultureInfo.InvariantCulture)}",
                "05 §1.1's rounding rule has nothing to say about a NaN or an infinity, and 05 §4's " +
                "pipeline produces real quantities — it is an overflow or a 0/0 in the value the " +
                "effect authored. A NaN compares false against every bound it meets (the dodge test, " +
                "the floor, the ward cap), so it passes through all of them and CombatLog refuses it " +
                "three layers later naming the serialiser instead of the effect.");
        }
    }
}
