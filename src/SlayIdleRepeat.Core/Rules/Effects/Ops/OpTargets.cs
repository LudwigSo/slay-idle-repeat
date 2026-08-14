using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// The actors one op resolves against — `18` §5's tokens, read through M2-05's resolver, with the
/// one thing an <em>op</em> has to decide that a target token cannot: what an <b>absent</b>
/// <c>target</c> means.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>M2-02 has ruled: an absent <c>target</c> is <c>SELF</c>.</b> The ruling, and the clause it
/// is read from — `18` §2.4's <c>CLEAR_SUMMONS</c> row, <em>"(default <c>SELF</c>)"</em>, the one
/// place the document states a default target at all — are in <see cref="EffectDefaults"/>. M2-03
/// wrote this class before it existed and refused instead; the refusal is now gone, and the reasoning
/// that produced it (<em>"<c>SELF</c> turns a perk into self-harm"</em>) is answered there.
/// </para>
/// <para>
/// The three-way split below survives the ruling and is still what stops an op guessing, because the
/// default is only the <b>last</b> of the three arms:
/// </para>
/// <list type="bullet">
///   <item><b>Holder-scoped ops never read <c>target</c> at all.</b> <c>SURVIVE_LETHAL</c>,
///   <c>REVIVE</c>, <c>SUMMON</c>, <c>ATTACK_MULT_NEXT</c> and <c>FORCE_CRIT_NEXT</c> act on
///   whoever holds them by their own §2.4 wording — <em>"survive"</em>, <em>"the next N
///   attacks"</em> — which is exactly why `18`'s worked examples for them carry no target. They call
///   <see cref="Holder"/>, and an authored target on one changes nothing.</item>
///   <item><b><c>CLEAR_SUMMONS</c>' authored default is now an instance of the general rule</b>
///   rather than the DSL's one special case. <see cref="OrSelf"/> is kept as its own member because
///   §2.4 states it explicitly and a reader of that row should find it, but it and
///   <see cref="Resolve"/> now agree by construction.</item>
///   <item><b>Everything else resolves through <see cref="EffectDefaults.TargetOf"/>.</b> A
///   <c>DAMAGE</c> with no target hits the holder — which is what §7.5's <c>CP_BLOOD_PRICE</c>
///   drawback is, and it is the only reading under which §7.4, §7.5, §7.6 and §9.1 run at all.</item>
/// </list>
/// </remarks>
internal static class OpTargets
{
    /// <summary>
    /// The actors the effect's `18` §5 token names — or, where it authors none, `18` §5's
    /// <c>SELF</c> (<see cref="EffectDefaults"/> ruling 2).
    /// </summary>
    /// <exception cref="EffectContextException">
    /// The token's subject is absent from the context (M2-05's uniform rule).
    /// </exception>
    internal static IReadOnlyList<IEffectActorView> Resolve(EffectDefinition effect, EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);

        return TargetResolver.Resolve(EffectDefaults.TargetOf(effect), context.Evaluation);
    }

    /// <summary>
    /// 🔒 §2.4's authored default: <c>CLEAR_SUMMONS</c>'s <em>"(default <c>SELF</c>)"</em>.
    /// </summary>
    /// <remarks>
    /// Since M2-02's ruling this is the same answer <see cref="Resolve"/> gives, and it is kept as its
    /// own member because §2.4 writes the default into that op's row: a reader following the document
    /// to <c>CLEAR_SUMMONS</c> should find the clause named, not have to infer that the general rule
    /// happens to cover it. Delegating rather than restating is what keeps the two from drifting.
    /// </remarks>
    internal static IReadOnlyList<IEffectActorView> OrSelf(EffectDefinition effect, EffectOpContext context) =>
        Resolve(effect, context);

    /// <summary>
    /// The effect's holder, for the §2.4 ops whose own wording scopes them to it. Reads
    /// <c>target</c> for nothing, so an authored one on such an effect changes no behaviour.
    /// </summary>
    internal static IEffectActorView Holder(EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Holder;
    }
}
