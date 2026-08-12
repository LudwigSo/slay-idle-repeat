using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Targeting;

/// <summary>
/// 🔒 `18` §5 — the eleven target tokens, resolved to the actors they name.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Every token is relative to the effect's holder.</b> See
/// <see cref="EffectEvaluationContext.Holder"/> for `18` §7.10's Volatile worked example, which is
/// what settles it.
/// </para>
/// <para>
/// 🔒 <b>Living actors only.</b> `05` §3.1 step 6: <em>"An actor whose HP reaches 0 stops acting and
/// being targetable at that moment — only its death resolution (<c>ON_DEATH</c>, removal) waits for
/// this slot."</em> <c>SELF</c>, <c>ATTACKER</c> and <c>OWNER</c> are excepted: each names one
/// specific actor rather than selecting from a set, and a dying actor's own <c>ON_DEATH</c> effect
/// targeting <c>SELF</c> is `18` §3's whole point.
/// </para>
/// <para>
/// 🔒 <b>Pets are never in an enemy set.</b> `05` §3.2: <em>"Pets cannot be targeted or killed."</em>
/// </para>
/// </remarks>
internal static class TargetResolver
{
    /// <summary>
    /// The actors a `18` §5 token names, in `05` §3.1's fixed actor-index order.
    /// </summary>
    /// <returns>
    /// The selected actors, possibly empty. An empty result means the set is genuinely empty — the
    /// last enemy died, the hero has no pets equipped, or `18` §5's authored <c>OWNER</c> skip
    /// applied. It never means "the token could not be answered": that throws.
    /// </returns>
    /// <exception cref="EffectContextException">
    /// The context does not carry the token's subject. See that type for the uniform rule.
    /// </exception>
    internal static IReadOnlyList<IEffectActorView> Resolve(
        EffectTarget target,
        EffectEvaluationContext context) =>
        throw new NotImplementedException();
}
