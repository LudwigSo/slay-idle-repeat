using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// The actors one op resolves against — target tokens, read through the resolver, plus the one thing
/// an op has to decide that a token can't: what an absent <c>target</c> means.
/// </summary>
/// <remarks>
/// <para>An absent target resolves to <c>SELF</c> (see <see cref="EffectDefaults"/>), but that's only the last of three arms:</para>
/// <list type="bullet">
///   <item>Holder-scoped ops never read <c>target</c> at all — <c>SURVIVE_LETHAL</c>,
///   <c>REVIVE</c>, <c>SUMMON</c>, <c>ATTACK_MULT_NEXT</c> and <c>FORCE_CRIT_NEXT</c> act on
///   whoever holds them by their own wording, and call <see cref="Holder"/> directly; an authored
///   target on one changes nothing.</item>
///   <item><c>CLEAR_SUMMONS</c>' authored default is an instance of the general rule rather than a
///   special case, but <see cref="OrSelf"/> is kept as its own member so a reader following that
///   op's documented default finds it named.</item>
///   <item>Everything else resolves through <see cref="EffectDefaults.TargetOf"/> — a <c>DAMAGE</c>
///   with no target hits the holder.</item>
/// </list>
/// </remarks>
internal static class OpTargets
{
    /// <summary>The actors the effect's target token names — or, where it authors none, <c>SELF</c>.</summary>
    /// <exception cref="EffectContextException">The token's subject is absent from the context.</exception>
    internal static IReadOnlyList<IEffectActorView> Resolve(EffectDefinition effect, EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);

        return TargetResolver.Resolve(EffectDefaults.TargetOf(effect), context.Evaluation);
    }

    /// <summary>The authored default for <c>CLEAR_SUMMONS</c>: "(default SELF)".</summary>
    /// <remarks>The same answer as <see cref="Resolve"/>; kept as its own member so a reader following that op's row finds the default named.</remarks>
    internal static IReadOnlyList<IEffectActorView> OrSelf(EffectDefinition effect, EffectOpContext context) =>
        Resolve(effect, context);

    /// <summary>The effect's holder, for ops whose own wording scopes them to it. Ignores an authored <c>target</c>.</summary>
    internal static IEffectActorView Holder(EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Holder;
    }
}
