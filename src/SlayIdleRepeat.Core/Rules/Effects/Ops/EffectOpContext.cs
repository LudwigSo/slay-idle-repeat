using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

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
/// 🔒 `05` §1.1's 4-decimal-place rounding, as the op layer names it — <b>the failure message, not a
/// second statement of the rule</b>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>Closed by M2-02.</b> This WAS a second statement of `05` §1.1's rule. M2-03 wrote it that
/// way with the reason recorded — R17 makes <c>Rules.Effects</c> the bottom of the intra-<c>Rules</c>
/// layering, so nothing here may name <c>Rules.Stats.StatRounding</c> — and named the fix:
/// <em>"a shared primitive under <c>Core.Primitives</c> is the real fix and belongs with M2-02's
/// relocation of the `18` seams out of <c>Rules/Stats/</c>."</em> That primitive is
/// <see cref="DeterminismRounding"/>, and it works where <c>StatRounding</c> could not because `30`
/// §11.4 puts <c>Primitives</c> beneath <b>every</b> layer that rounds — under
/// <c>Rules.Effects</c> and <c>Rules.Stats</c> alike, and under <c>Content</c>, which rounds too and
/// may name neither.
/// </para>
/// <para>
/// 🔒 <b>What is left here is the refusal</b> — <see cref="OpRounding.RequireFinite"/>. A NaN or an
/// infinity is not a rounding question, and the useful thing to say about one is <em>which effect
/// produced it and what the number was meant to be</em> — steering S2. <c>StatRounding</c> keeps its
/// own, naming the `18` §8 step and the stat instead. Two messages, one rule; the drift hazard was
/// the <c>4</c>, the midpoint mode and the trailing <c>+ 0.0</c>, and those are now stated once.
/// </para>
/// <para>
/// 🔴 <b>Errata: the refusal WAS duplicated too.</b> This paragraph used to claim the refusal was
/// <em>"the part that was never duplicated"</em>, and by the end of M2 that was false —
/// <c>AttackPipeline.RequireFinite</c> was a near-verbatim second copy of it, on the same values
/// under the same labels ("true damage", "healing", "max-HP-percent damage"). M2 review lifted the
/// guard into <see cref="OpRounding.RequireFinite"/> and made the pipeline's method a forwarder, so
/// the wording exists once. The pipeline still needs a guard of its own because
/// <c>StatusTimeline</c> and the boss scripts reach it without passing through this layer.
/// </para>
/// <para>
/// <c>OpRoundingTests.The_op_rounding_and_the_stat_rounding_are_one_rule</c> still runs both over the
/// same values and is now a tautology by construction — kept because a test that becomes trivially
/// true when a duplication is removed is the cheapest possible regression guard against its return,
/// and <c>DeterminismRoundingRuleTests</c> is what makes the return a build failure.
/// </para>
/// </remarks>
internal static class OpRounding
{
    /// <summary>
    /// 🔒 The number of decimal places `05` §1.1 locks — an alias of
    /// <see cref="DeterminismRounding.Decimals"/>, not a copy of it.
    /// </summary>
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

    /// <summary>
    /// 🔒 The refusal itself, <b>stated once</b> — every combat number that reaches a log, a stat or
    /// an HP bar passes this, whether or not it is being rounded on the way.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Lifted out of <see cref="Round"/> by M2 review.</b> `05` §4's damage pipeline
    /// (<c>AttackPipeline</c>) had a near-verbatim second copy — same type, same <c>"R"</c> invariant
    /// formatting, same rationale, on the same values under the same labels. It is reachable without
    /// passing through this layer at all (<c>StatusTimeline</c> and the boss scripts call the pipeline
    /// directly), so the guard has to exist there; what it must not be is a second <em>statement</em>
    /// of the rule.
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
