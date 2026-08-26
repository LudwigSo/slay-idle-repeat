namespace SlayIdleRepeat.Core.Content.Effects;

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
/// <b>subject-presence</b> — whether one evaluation context supplies the subjects a tree reads is
/// <c>EffectEvaluationContext.Carries</c>, the other half of the rule.
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

        // Indexed rather than foreach: the walk sits under the battle gate, consulted per
        // conditional effect per aggregation pass, and an interface enumerator per combinator
        // node is an allocation the tick loop would pay 1800 times over.
        var operands = condition.Operands;
        for (var i = 0; i < operands.Count; i++)
        {
            // A null operand is a hole in the tree; the evaluator refuses it, so the walk has
            // nothing to classify there and leaves the refusal where it is.
            if (operands[i] is null)
            {
                continue;
            }

            var nested = Of(operands[i]);

            subjects = new ConditionSubjects(
                subjects.ReadsTarget || nested.ReadsTarget,
                subjects.ReadsAttacker || nested.ReadsAttacker);
        }

        return subjects;
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
