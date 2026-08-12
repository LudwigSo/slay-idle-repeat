using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// Everything one `18` §2 op resolution needs beyond the effect itself: the `18` §4/§5 evaluation
/// context, the seams it writes through, and the three readings that exist only inside the moment
/// that fired it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A wrapper rather than three more properties on <see cref="EffectEvaluationContext"/>.</b>
/// That record is the <em>state a condition or a target reads</em> and is held to `18` §4's
/// <em>"pure functions of current state"</em> by <c>ConditionPurityRuleTests</c>. The three fields
/// below are not state at all — they are the arguments of the event being reacted to, and only
/// three of `18` §2.2's eight value modes ever read them. Widening the shared context would put a
/// heal's overheal in front of every condition evaluation in the game.
/// </para>
/// <para>
/// ⚠️ <b>M2-04 owns the triggers that populate these.</b> An <c>ON_HEAL</c> effect (`18` §3) is
/// what makes <see cref="HealAmount"/> and <see cref="OverhealAmount"/> readable — `05` §4.3 fires
/// those triggers with both in hand — and an on-hit-family trigger is what makes
/// <see cref="DamageDealt"/> readable. Where the trigger did not supply one, it stays
/// <c>null</c> and the value mode that needs it throws rather than reading zero (steering S6).
/// </para>
/// </remarks>
internal sealed record EffectOpContext
{
    /// <summary>The `18` §4 / §5 state — the holder, the roster, the target, the draw stream.</summary>
    public required EffectEvaluationContext Evaluation { get; init; }

    /// <summary>Where the op's number goes. <see cref="EffectOpSeams.Strict"/> refuses everything.</summary>
    public required EffectOpSeams Seams { get; init; }

    /// <summary>
    /// `18` §2.2's <c>DAMAGE_DEALT_PCT</c> basis — the damage the firing event just dealt.
    /// <c>null</c> outside a damage context.
    /// </summary>
    /// <remarks>
    /// 🔒 `05` §4 step 8's <em>on-damage basis</em>: the post-mitigation, post-floor hit
    /// <b>before</b> ward absorption. `05` §4.1 is explicit that <em>"a lifesteal attacker still
    /// heals off a fully-warded hit"</em>, so a leech reading the post-absorption number would heal
    /// nothing off a shielded target.
    /// </remarks>
    public double? DamageDealt { get; init; }

    /// <summary>
    /// `18` §2.2's <c>HEAL_AMOUNT</c> basis — <em>"the full amount actually healed"</em>.
    /// <c>null</c> outside an <c>ON_HEAL</c> context (`05` §4.3).
    /// </summary>
    public double? HealAmount { get; init; }

    /// <summary>
    /// `18` §2.2's <c>OVERHEAL_AMOUNT</c> basis — <em>"the clipped excess"</em>. <c>null</c> outside
    /// an <c>ON_HEAL</c> context. <c>PK_TRANSFUSION</c>'s whole input.
    /// </summary>
    public double? OverhealAmount { get; init; }

    /// <summary>The actor holding the effect — the source of every op's number.</summary>
    public IEffectActorView Holder => Evaluation.Holder;
}

/// <summary>
/// 🔒 `05` §1.1's 4-decimal-place rounding, stated for the layer that cannot reach
/// <c>Rules.Stats.StatRounding</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>This is a second statement of one rule, and it is deliberate.</b> R17 makes
/// <c>Rules.Effects</c> the bottom of the intra-<c>Rules</c> layering, so nothing here may name
/// <c>Rules.Stats</c>, where M2-07 put <c>StatRounding</c>. The alternatives were worse: reversing
/// the layering for one <c>Math.Round</c>, or leaving every op's number unrounded and breaking
/// `05` §1.1's <em>"at every accumulation point — after each damage calculation, each heal"</em> for
/// the whole DSL.
/// </para>
/// <para>
/// 🔒 The two are pinned together by
/// <c>OpRoundingTests.The_op_rounding_and_the_stat_rounding_are_one_rule</c>, which runs both over
/// the same values from the test assembly, which can see both namespaces. A shared primitive under
/// <c>Core.Primitives</c> is the real fix and belongs with M2-02's relocation of the `18` seams out
/// of <c>Rules/Stats/</c>.
/// </para>
/// <para>
/// The trailing <c>+ 0.0</c> is not redundant — it normalises <c>-0.0</c>, which
/// <c>CanonicalStateWriter</c> refuses and <c>CombatLog</c> refuses after it.
/// </para>
/// </remarks>
internal static class OpRounding
{
    /// <summary>🔒 The number of decimal places `05` §1.1 locks.</summary>
    internal const int Decimals = 4;

    /// <summary>Rounds one accumulated combat number and normalises <c>-0.0</c> to <c>+0.0</c>.</summary>
    /// <param name="value">The accumulated value.</param>
    /// <param name="effectId">The effect that produced it — named in the failure message.</param>
    /// <param name="what">What the number is, in the reader's terms.</param>
    /// <exception cref="EffectContextException"><paramref name="value"/> is NaN or infinite.</exception>
    internal static double Round(double value, string effectId, string what)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new EffectContextException(
                effectId,
                $"its {what} came out as {value.ToString("R", CultureInfo.InvariantCulture)}",
                "05 §1.1's rounding rule has nothing to say about a NaN or an infinity — it is an " +
                "overflow or a 0/0 in the value the effect authored, and CombatLog would refuse it " +
                "three layers later naming the serialiser instead of the effect.");
        }

        return Math.Round(value, Decimals) + 0.0;
    }
}
