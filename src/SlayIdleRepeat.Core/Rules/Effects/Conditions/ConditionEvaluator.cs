using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Effects.Conditions;

/// <summary>
/// The condition functions, comparators and combinators, evaluated against current state.
/// </summary>
/// <remarks>
/// All functions are pure over current state — nothing here draws, reads a clock, mutates or
/// caches; <c>ConditionPurityRuleTests</c> proves it mechanically. <see cref="Read"/> rounds to four
/// decimal places once, so a comparison and a <c>valueScale</c> division see the same number and
/// round consistently across platforms. Every function reads as a number, with booleans as
/// <c>1</c>/<c>0</c>, so a boolean and a numeric comparison against the same reading answer alike.
/// </remarks>
internal static class ConditionEvaluator
{
    /// <summary>
    /// Whether a condition tree holds against the given state. A <c>null</c> condition is an
    /// ungated effect and holds.
    /// </summary>
    /// <remarks>
    /// <c>all</c> and <c>any</c> short-circuit — not as an optimisation, but because some operands
    /// (e.g. run-state reads gated behind an <c>IS_PVP</c> check) are only valid to read once an
    /// earlier operand has ruled out contexts where they'd throw.
    /// </remarks>
    /// <exception cref="EffectContextException">
    /// The context does not carry a subject the tree reads, or a term is malformed. See that type for
    /// the uniform rule.
    /// </exception>
    internal static bool IsSatisfied(EffectCondition? condition, EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (condition is null)
        {
            return true;
        }

        switch (condition.Kind)
        {
            case ConditionKind.TERM:
                return Compare(
                    condition.Term ?? throw Malformed(
                        nameof(ConditionKind.TERM),
                        "the node is a comparison but carries none",
                        "18 §4 writes a term as {\"fn\": …, \"op\": …, \"value\": …}."),
                    context);

            case ConditionKind.ALL:
            {
                var all = Combinator(condition);

                for (var i = 0; i < all.Count; i++)
                {
                    // Short-circuits: a guard operand placed first (e.g. an IS_PVP check) can rule
                    // out a context before later operands that would throw on it are ever read.
                    if (!IsSatisfied(Operand(all, i), context))
                    {
                        return false;
                    }
                }

                return true;
            }

            case ConditionKind.ANY:
            {
                var any = Combinator(condition);

                for (var i = 0; i < any.Count; i++)
                {
                    if (IsSatisfied(Operand(any, i), context))
                    {
                        return true;
                    }
                }

                return false;
            }

            case ConditionKind.NOT:
                var operands = Combinator(condition);
                return operands.Count == 1
                    ? !IsSatisfied(Operand(operands, 0), context)
                    : throw Malformed(
                        nameof(ConditionKind.NOT),
                        $"it carries {operands.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} operands rather than one",
                        "18 §4 writes it as {\"not\": { … }} — a single operand, not a list.");

            default:
                throw Malformed(
                    condition.Kind.ToString(),
                    "it is not a node kind 18 §4 authorises",
                    "18 §4's tree is a comparison or one of all · any · not.");
        }
    }

    /// <summary>
    /// One function's reading of current state, rounded to four decimal places. Booleans read
    /// <c>1</c> and <c>0</c>.
    /// </summary>
    /// <exception cref="EffectContextException">
    /// The context does not carry the function's subject, or a required argument is absent.
    /// </exception>
    internal static double Read(
        ConditionFunction function,
        ConditionArguments arguments,
        EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DeterminismRounding.Round(Reading(function, arguments, context));
    }

    /// <summary>
    /// A combinator's operands, rejected when there are none.
    /// </summary>
    /// <remarks>
    /// Duplicates <see cref="EffectCondition.All"/>'s guard on purpose: that guard lives in a
    /// factory, but <see cref="EffectCondition.Operands"/> is an <c>init</c> property a deserializer
    /// can bind directly, bypassing the factory. An empty <c>all</c>/<c>any</c> would otherwise fire
    /// (or never fire) silently, so the evaluator re-checks here.
    /// </remarks>
    private static IReadOnlyList<EffectCondition> Combinator(EffectCondition condition) =>
        condition.Operands.Count > 0
            ? condition.Operands
            : throw Malformed(
                condition.Kind.ToString(),
                "it carries no operands",
                "18 §4's combinators gate on their operands: an empty 'all' is vacuously true and an " +
                "empty 'any' vacuously false, so the effect would fire — or never fire — with its " +
                "condition still present in the data.");

    /// <summary>One operand of a combinator, rejected when it is <c>null</c>.</summary>
    /// <remarks>
    /// <see cref="IsSatisfied"/> reads a <c>null</c> condition as an ungated effect, which is only
    /// meant to apply at an effect's top level — not to a hole inside a combinator, where a missing
    /// operand would silently ungate the same way an empty <c>any</c> would.
    /// </remarks>
    private static EffectCondition Operand(IReadOnlyList<EffectCondition> operands, int index) =>
        operands[index] ?? throw Malformed(
            "operand",
            $"operand {index.ToString(System.Globalization.CultureInfo.InvariantCulture)} of the combinator is null",
            "18 §1's \"condition\": null means an UNGATED EFFECT. A null inside a combinator is a " +
            "hole in the tree, and reading it as 'true' would ungate the effect silently.");

    // ------------------------------------------------------------------ the twenty-three functions

    private static double Reading(
        ConditionFunction function,
        ConditionArguments arguments,
        EffectEvaluationContext context) =>
        function switch
        {
            ConditionFunction.SELF_HP_PCT => HpFraction(context.Holder, function),
            ConditionFunction.SELF_MISSING_HP_PCT => 1.0 - HpFraction(context.Holder, function),
            ConditionFunction.TARGET_HP_PCT => HpFraction(Target(context, function), function),

            // In PvP these three are fixed by rule (no elite/boss, exactly one "enemy") rather than
            // read off the roster, so a mislabelled snapshot can't change what a perk does.
            ConditionFunction.TARGET_IS_ELITE => context.IsPvp ? 0 : Flag(Target(context, function).IsElite),
            ConditionFunction.TARGET_IS_BOSS => context.IsPvp ? 0 : Flag(Target(context, function).IsBoss),
            ConditionFunction.ENEMY_COUNT => context.IsPvp ? 1 : LivingEnemyCount(context),

            ConditionFunction.BATTLE_TIME => context.BattleTimeSeconds,
            ConditionFunction.BATTLE_TIME_REMAINING_EST => TimeRemaining(context),

            ConditionFunction.HAS_STATUS => Flag(context.Holder.StatusStacks(StatusId(arguments, function)) > 0),
            ConditionFunction.STATUS_STACKS => context.Holder.StatusStacks(StatusId(arguments, function)),

            ConditionFunction.PERK_COUNT => Run(context, function).PerkCount(arguments.Category),
            ConditionFunction.DISTINCT_PERK_CATEGORIES => Run(context, function).DistinctPerkCategories,
            ConditionFunction.PET_COUNT => Run(context, function).PetCount,
            ConditionFunction.GOLD_HELD => Run(context, function).GoldHeld,
            ConditionFunction.BATTLES_WON_THIS_RUN => Run(context, function).BattlesWonThisRun,
            ConditionFunction.STAGE_INDEX => Run(context, function).StageIndex,
            ConditionFunction.CHAPTER => Run(context, function).Chapter,
            ConditionFunction.TIER => Run(context, function).Tier,

            ConditionFunction.IS_PVP => Flag(context.IsPvp),

            // These three default to false rather than throwing when there's no attacker in
            // context, since a throw there would make an otherwise-valid effect unusable outside a
            // hit reaction. (Both aggregation gates now intercept attacker-reading STANDING trees
            // via the subject-presence rule before this evaluator is asked, so the default's
            // remaining consumers are trigger-time and value-scale contexts.)
            ConditionFunction.ATTACKER_IS_ELITE => Flag(context.Attacker?.IsElite ?? false),
            ConditionFunction.ATTACKER_IS_BOSS => Flag(context.Attacker?.IsBoss ?? false),
            ConditionFunction.ATTACKER_IS_SUMMON => Flag(context.Attacker?.IsSummon ?? false),

            _ => throw Malformed(
                function.ToString(),
                "it is not one of 18 §4's twenty-three functions",
                "18 §11: '23 conditions = 20 + the three ATTACKER_IS_*'."),
        };

    private static double Flag(bool value) => value ? 1 : 0;

    /// <summary>
    /// An actor's HP as a <c>0..1</c> fraction.
    /// </summary>
    /// <remarks>
    /// Clamped rather than left raw: an overkilled holder's <c>ON_DEATH</c> effect can see
    /// <c>CurrentHp &lt; 0</c>, and a Max HP decrease (e.g. a buff expiring) can leave
    /// <c>CurrentHp &gt; MaxHp</c>. Unclamped, a scale like <c>PK_BERSERK</c> could read a negative
    /// fraction and apply a penalty from a perk that's only ever meant to add.
    /// </remarks>
    private static double HpFraction(IEffectActorView actor, ConditionFunction function) =>
        actor.MaxHp > 0
            ? Math.Clamp(actor.CurrentHp / actor.MaxHp, 0.0, 1.0)
            : throw new EffectContextException(
                function.ToString(),
                $"'{actor.Id}' has a Max HP of {actor.MaxHp.ToString(System.Globalization.CultureInfo.InvariantCulture)}, so it has no HP fraction",
                "18 §4 types the HP functions as 0..1 and authorises no answer for a zero denominator. " +
                "Steering S6: a guard returning 0 or 1 here would be a value the design has not " +
                "authorised, and it would silently fire or silently suppress every perk gated on HP.");

    /// <summary>
    /// Seconds until the fight's enrage, or until its forced end if it has no enrage.
    /// </summary>
    /// <remarks>
    /// Clamped at zero: past the enrage there's no time "remaining", and a negative reading would
    /// invert the sign of every <c>valueScale</c> step driven by it.
    /// </remarks>
    private static double TimeRemaining(EffectEvaluationContext context) =>
        Math.Max(0.0, (context.EnrageAtSeconds ?? context.FightHorizonSeconds) - context.BattleTimeSeconds);

    /// <summary>
    /// Holder-relative, through the same predicate <c>ALL_ENEMIES</c> targeting uses, so the two can
    /// never disagree about one battle.
    /// </summary>
    private static int LivingEnemyCount(EffectEvaluationContext context) =>
        BattleRoster.LivingEnemies(context).Count;

    // ------------------------------------------------------------------ subjects the context may lack

    private static IEffectActorView Target(EffectEvaluationContext context, ConditionFunction function) =>
        context.CurrentTarget ?? throw new EffectContextException(
            function.ToString(),
            "the context carries no current target",
            "18 §4 authorises no reading for an absent target — its one authored default is the " +
            "ATTACKER_IS_* trio's false, and it is written out. Steering S6: a substituted 0 would " +
            "fire PK_EXECUTIONER (18 §7.2) on an empty battlefield.");

    private static IRunStateView Run(EffectEvaluationContext context, ConditionFunction function) =>
        context.Run ?? throw new EffectContextException(
            function.ToString(),
            "the context carries no run state",
            "18 §9.3 rules that a clause with no duel meaning is 'simply skipped rather than " +
            "converted', via the IS_PVP condition — so a run-state condition reaching a run-less " +
            "context is content that did not skip it, and a substituted 0 would hide that forever.");

    private static string StatusId(ConditionArguments arguments, ConditionFunction function) =>
        arguments.StatusId ?? throw Malformed(
            function.ToString(),
            "it reads a status and the term names none",
            "18 §4 types it 'by status id'. Counting every status instead would be a reading the " +
            "document does not describe. (18 §1.1 offers the same function to valueScale, which " +
            "carries no argument key at all — see ConditionArguments.)");

    // ------------------------------------------------------------------ the seven comparators

    private static bool Compare(ConditionTerm term, EffectEvaluationContext context)
    {
        // Validated before the function is read, so a malformed term is reported as such rather
        // than as whichever subject the context happens to be missing too.
        RejectAmbiguousOperand(term);

        if (term.Comparator == ConditionComparator.BETWEEN)
        {
            RejectFlag(term);

            var low = term.RangeLow ?? throw MissingBound(term);
            var high = term.RangeHigh ?? throw MissingBound(term);

            if (low > high)
            {
                // Throws rather than silently never firing: an inverted range is a content
                // authoring error that should fail loudly instead of going undetected.
                throw Malformed(
                    term.Comparator.ToString(),
                    $"the {term.Fn} term's range is inverted — its low bound is above its high bound",
                    "18 §4's 'between' is an inclusive range. An inverted one is satisfied by no " +
                    "reading at all, so the effect it gates could never fire.");
            }

            var reading = Read(term.Fn, ConditionArguments.Of(term), context);

            // Inclusive at both ends, per ConditionTerm.RangeLow/RangeHigh's declarations.
            return reading >= low && reading <= high;
        }

        if (term.Flag is { } flag)
        {
            RejectFlag(term);

            var holds = Read(term.Fn, ConditionArguments.Of(term), context) != 0.0;

            return term.Comparator == ConditionComparator.EQ ? holds == flag : holds != flag;
        }

        var value = term.Value ?? throw Malformed(
            term.Comparator.ToString(),
            $"the {term.Fn} term carries nothing to compare against — neither a value nor a flag",
            "18 §4 writes every comparison as {\"fn\": …, \"op\": …, \"value\": …}.");

        var actual = Read(term.Fn, ConditionArguments.Of(term), context);

        return term.Comparator switch
        {
            // Exact equality on doubles, deliberately: actual has already been rounded to 4 dp by
            // Read, which is what makes the two sides comparable without an epsilon.
            ConditionComparator.EQ => actual == value,
            ConditionComparator.NEQ => actual != value,
            ConditionComparator.LT => actual < value,
            ConditionComparator.LTE => actual <= value,
            ConditionComparator.GT => actual > value,
            ConditionComparator.GTE => actual >= value,
            _ => throw Malformed(
                term.Comparator.ToString(),
                "it is not one of 18 §4's seven comparators",
                "18 §4: 'eq · neq · lt · lte · gt · gte · between'."),
        };
    }

    /// <summary>
    /// Rejects a boolean compared with an ordering comparator — <c>{"op":"gt","value":true}</c> has no
    /// meaning, and answering it would mean inventing one.
    /// </summary>
    private static void RejectFlag(ConditionTerm term)
    {
        if (term.Flag is not null &&
            term.Comparator is not (ConditionComparator.EQ or ConditionComparator.NEQ))
        {
            throw Malformed(
                term.Comparator.ToString(),
                "it orders a boolean",
                "18 §4's comparators order numbers. Only eq and neq are defined over a boolean, which " +
                "is what 18 §7.10 writes as {\"fn\":\"ATTACKER_IS_ELITE\",\"op\":\"eq\",\"value\":true}.");
        }
    }

    /// <summary>
    /// Rejects a term carrying both a numeric value and a boolean one.
    /// </summary>
    /// <remarks>
    /// Without this the flag branch would silently win and the number would be discarded, turning
    /// an exact numeric comparison into "true at any non-zero reading".
    /// </remarks>
    private static void RejectAmbiguousOperand(ConditionTerm term)
    {
        if (term is { Flag: not null, Value: not null })
        {
            throw Malformed(
                term.Fn.ToString(),
                "the term carries both a numeric value and a boolean one",
                "18 §4 writes one 'value' per comparison. Answering on the flag and discarding the " +
                "number would make an exact numeric comparison true at any non-zero reading.");
        }
    }

    private static EffectContextException MissingBound(ConditionTerm term) =>
        Malformed(
            term.Comparator.ToString(),
            $"the {term.Fn} term is a range and one of its two bounds is absent",
            "18 §4 lists 'between' and writes no example, so the encoding is ConditionTerm's " +
            "two-element one — and half a range is not a range.");

    private static EffectContextException Malformed(string token, string missing, string reference) =>
        new(token, missing, reference);
}
