using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Conditions;

/// <summary>
/// Which contextual subjects — the current target, the attacker — a condition tree reads, anywhere
/// in its nesting. The classification behind the conditional standing-effect bucket.
/// </summary>
/// <param name="ReadsTarget">The tree reads the current target somewhere.</param>
/// <param name="ReadsAttacker">The tree reads the attacker somewhere.</param>
/// <remarks>
/// <para>
/// The classification is over the tree's <b>vocabulary</b>, never its truth value: a negated or
/// disjoined target read still makes the tree target-reading, because the bucket's rule is
/// <b>subject-presence</b> — a standing effect whose gate reads a contextual subject is active only
/// in an evaluation context that carries that subject, and only there is the tree evaluated at all.
/// Ambient re-aggregation carries neither subject; one attack resolution carries both, one per side.
/// </para>
/// <para>
/// Pure over the tree and total over the vocabulary: every <see cref="ConditionFunction"/> outside
/// the two trios below is ambient, so a new function is ambient until this partition says otherwise
/// — which the classification test forces as an explicit decision.
/// </para>
/// </remarks>
internal readonly record struct ConditionSubjects(bool ReadsTarget, bool ReadsAttacker)
{
    /// <summary>The subjects the tree reads, walked through every combinator.</summary>
    /// <param name="condition">The tree, or <see langword="null"/> for an ungated effect.</param>
    internal static ConditionSubjects Of(EffectCondition? condition)
    {
        if (condition is null)
        {
            return default;
        }

        var subjects = condition.Term is { } term ? OfFunction(term.Fn) : default;

        foreach (var operand in condition.Operands)
        {
            // A null operand is a hole in the tree; the evaluator refuses it, so the walk has
            // nothing to classify there and leaves the refusal where it is.
            if (operand is null)
            {
                continue;
            }

            var nested = Of(operand);

            subjects = new ConditionSubjects(
                subjects.ReadsTarget || nested.ReadsTarget,
                subjects.ReadsAttacker || nested.ReadsAttacker);
        }

        return subjects;
    }

    /// <summary>
    /// Whether a context carries every subject the tree reads — the bucket's activation rule.
    /// </summary>
    /// <param name="condition">The tree, or <see langword="null"/> for an ungated effect.</param>
    /// <param name="context">The evaluation context asking.</param>
    internal static bool CarriedBy(EffectCondition? condition, EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var subjects = Of(condition);

        return (!subjects.ReadsTarget || context.CurrentTarget is not null) &&
               (!subjects.ReadsAttacker || context.Attacker is not null);
    }

    /// <summary>One function's subject: the target trio, the attacker trio, or neither.</summary>
    private static ConditionSubjects OfFunction(ConditionFunction fn) =>
        fn switch
        {
            ConditionFunction.TARGET_HP_PCT or
            ConditionFunction.TARGET_IS_ELITE or
            ConditionFunction.TARGET_IS_BOSS => new ConditionSubjects(ReadsTarget: true, ReadsAttacker: false),

            ConditionFunction.ATTACKER_IS_ELITE or
            ConditionFunction.ATTACKER_IS_BOSS or
            ConditionFunction.ATTACKER_IS_SUMMON => new ConditionSubjects(ReadsTarget: false, ReadsAttacker: true),

            _ => default,
        };
}
