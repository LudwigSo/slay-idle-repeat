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
/// 🔒 <b>`18` states no default target, and none is invented here.</b> M2-01 recorded it as errata —
/// §7.4, §7.5, §7.6 and §7.8 all author effects with no <c>target</c> — and M2-02 owns the ruling.
/// What this class does instead is split the ops into the two groups the document itself
/// distinguishes, so that no op has to guess:
/// </para>
/// <list type="bullet">
///   <item><b>Holder-scoped ops never read <c>target</c> at all.</b> <c>SURVIVE_LETHAL</c>,
///   <c>REVIVE</c>, <c>SUMMON</c>, <c>ATTACK_MULT_NEXT</c> and <c>FORCE_CRIT_NEXT</c> act on
///   whoever holds them by their own §2.4 wording — <em>"survive"</em>, <em>"the next N
///   attacks"</em> — which is exactly why `18`'s worked examples for them carry no target. They call
///   <see cref="Holder"/>.</item>
///   <item><b><c>CLEAR_SUMMONS</c> has an authored default.</b> §2.4 writes it out:
///   <em>"despawn all living summons owned by the target (default <c>SELF</c>)"</em>. It is the one
///   op in the DSL that does, and <see cref="OrSelf"/> is that clause and nothing wider.</item>
///   <item><b>Everything else refuses.</b> A <c>DAMAGE</c> or an <c>APPLY_STATUS</c> with no target
///   is an authoring hole, and both available guesses are wrong in a way nothing goes red for:
///   <c>SELF</c> turns a perk into self-harm, <c>CURRENT_TARGET</c> silently narrows an intended
///   AoE.</item>
/// </list>
/// </remarks>
internal static class OpTargets
{
    /// <summary>The actors the effect's `18` §5 token names.</summary>
    /// <exception cref="EffectContextException">
    /// The effect authors no target, or the token's subject is absent from the context (M2-05's
    /// uniform rule).
    /// </exception>
    internal static IReadOnlyList<IEffectActorView> Resolve(EffectDefinition effect, EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);

        var token = effect.Target ?? throw new EffectContextException(
            effect.Id,
            $"{effect.Op} needs a target and the effect authors none",
            "18 §1's eight-part shape names 'target', and 18 states no default for an effect that " +
            "omits it (§7.4, §7.5, §7.6 and §7.8 all do) — M2-01 recorded that as errata and M2-02 " +
            "owns the ruling. Both guesses available here are silent balance bugs: SELF turns an " +
            "offensive clause into self-harm, CURRENT_TARGET narrows an intended AoE to one actor " +
            "(steering S6).");

        return TargetResolver.Resolve(token, context.Evaluation);
    }

    /// <summary>
    /// 🔒 §2.4's one authored default: <c>CLEAR_SUMMONS</c>'s <em>"(default <c>SELF</c>)"</em>.
    /// </summary>
    internal static IReadOnlyList<IEffectActorView> OrSelf(EffectDefinition effect, EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);

        // ⚠️ `new[] { … }`, not the collection expression `[…]`. For a single element targeting
        // IReadOnlyList<T>, Roslyn synthesises <>z__ReadOnlySingleElementList in the GLOBAL
        // namespace with no CompilerGeneratedAttribute, and
        // AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace — which
        // filters on that attribute — reports it as an undocumented 30 §11.4 namespace. M2-01 hit it
        // in EffectCondition and M2-05 in TargetResolver.Only; this is the third.
        return effect.Target is null
            ? new[] { context.Holder }
            : TargetResolver.Resolve(effect.Target.Value, context.Evaluation);
    }

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
