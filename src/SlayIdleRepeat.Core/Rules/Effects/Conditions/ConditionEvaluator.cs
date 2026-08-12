using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Conditions;

/// <summary>
/// 🔒 `18` §4 — the twenty-three condition functions, the seven comparators and the three
/// combinators, evaluated against current state.
/// </summary>
/// <remarks>
/// <para>
/// `18` §4: <em>"Conditions gate an effect without changing when it is evaluated. All are pure
/// functions of current state."</em> Nothing here draws, reads a clock, mutates or caches;
/// <c>ConditionPurityRuleTests</c> proves it mechanically rather than trusting the sentence.
/// </para>
/// <para>
/// 🔒 <b>4 dp, once, here.</b> <see cref="Read"/> rounds its reading to four decimal places before
/// returning it (`05` §1.1, `14` §8.2, `18` §1.1). That is the accumulation point: a comparison and a
/// <c>valueScale</c> division then see the same number, and the boundary between 44 and 45 steps of
/// <c>PK_BERSERK</c> falls in the same place on ARM64 and x64.
/// <see cref="ValueScale.StepsFor"/> rounds again, which is idempotent, and owns the division —
/// M2-06 must hand it <see cref="Read"/>'s output unmodified rather than rounding or dividing first.
/// </para>
/// <para>
/// 🔒 <b>Every function reads as a number</b>, booleans as <c>1</c> and <c>0</c>. `18` §1.1 puts all
/// twenty-three behind <c>valueScale</c>'s <c>fn</c>, which divides — so a function with no numeric
/// reading would be one the DSL offers for something it cannot do. It also means `18` §7.10's
/// <c>{"op":"eq","value":true}</c> and a numeric <c>{"op":"eq","value":1}</c> answer alike without
/// either being a special case.
/// </para>
/// </remarks>
internal static class ConditionEvaluator
{
    /// <summary>
    /// Whether a `18` §4 condition tree holds against the given state. A <c>null</c> condition is an
    /// ungated effect and holds.
    /// </summary>
    /// <remarks>
    /// 🔒 <b><c>all</c> and <c>any</c> short-circuit.</b> Not as an optimisation: `18` §9.3's skip
    /// idiom is <c>{"all":[{"not":{"fn":"IS_PVP","op":"eq","value":true}}, …]}</c> on affixes whose
    /// remaining operands read run state, and `05` §3.3 gives a duel no run. An evaluator that read
    /// every operand before combining them would throw on precisely the effects the ruling exists to
    /// neutralise.
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

            // Enumerable.All and .Any stop at the first operand that decides the answer.
            case ConditionKind.ALL:
                return condition.Operands.All(operand => IsSatisfied(operand, context));

            case ConditionKind.ANY:
                return condition.Operands.Any(operand => IsSatisfied(operand, context));

            case ConditionKind.NOT:
                var operands = condition.Operands;
                return operands.Count == 1
                    ? !IsSatisfied(operands[0], context)
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
    /// One `18` §4 function's reading of current state, rounded to four decimal places. Booleans read
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

        // 🔒 The `+ 0.0` normalises a negative zero, for the reason ValueScale.EffectiveValue records:
        // CanonicalStateWriter THROWS on -0.0 rather than encoding one, because -0.0 and 0.0 have
        // different bit patterns and would produce two stateHashes for one state.
        return Math.Round(Reading(function, arguments, context), 4) + 0.0;
    }

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

            // 🔒 05 §3.3 states all three of these as rules of the duel rather than as consequences
            // of its roster — "TARGET_IS_ELITE / TARGET_IS_BOSS are always false. ENEMY_COUNT is
            // always 1." Implemented as stated: a ghost snapshot that arrived mislabelled, or a
            // roster M2-14 builds with a stray actor in it, must not change what a perk does.
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
            ConditionFunction.DIE_FACE_COUNT => DieFaceCount(arguments, context, function),
            ConditionFunction.GOLD_HELD => Run(context, function).GoldHeld,
            ConditionFunction.BATTLES_WON_THIS_RUN => Run(context, function).BattlesWonThisRun,
            ConditionFunction.STAGE_INDEX => Run(context, function).StageIndex,
            ConditionFunction.CHAPTER => Run(context, function).Chapter,
            ConditionFunction.TIER => Run(context, function).Tier,

            ConditionFunction.IS_PVP => Flag(context.IsPvp),

            // 🔒 18 §4's one authored default: "valid only in contexts with an attacker
            // (ON_HIT_TAKEN, ON_DODGE/ON_BLOCK, and DAMAGE_TAKEN_MULT evaluation inside 05 §4 step
            // 6); false elsewhere". PK_STALWART is an ALWAYS effect, so it is evaluated at every
            // resolution pass including the stat aggregation, where no attacker exists — a throw
            // there would make the perk unusable. 05 §3.3 rules the TARGET_IS_* pair for duels and
            // says nothing about these three, so nothing here switches them off in one.
            ConditionFunction.ATTACKER_IS_ELITE => Flag(context.Attacker?.IsElite ?? false),
            ConditionFunction.ATTACKER_IS_BOSS => Flag(context.Attacker?.IsBoss ?? false),
            ConditionFunction.ATTACKER_IS_SUMMON => Flag(context.Attacker?.IsSummon ?? false),

            _ => throw Malformed(
                function.ToString(),
                "it is not one of 18 §4's twenty-three functions",
                "18 §11: '23 conditions = 20 + the three ATTACKER_IS_*'."),
        };

    private static double Flag(bool value) => value ? 1 : 0;

    private static double HpFraction(IEffectActorView actor, ConditionFunction function) =>
        actor.MaxHp > 0
            ? actor.CurrentHp / actor.MaxHp
            : throw new EffectContextException(
                function.ToString(),
                $"'{actor.Id}' has a Max HP of {actor.MaxHp.ToString(System.Globalization.CultureInfo.InvariantCulture)}, so it has no HP fraction",
                "18 §4 types the HP functions as 0..1 and authorises no answer for a zero denominator. " +
                "Steering S6: a guard returning 0 or 1 here would be a value the design has not " +
                "authorised, and it would silently fire or silently suppress every perk gated on HP.");

    /// <summary>
    /// 🔒 `18` §4 says <em>"seconds to the 70 s enrage"</em>; `05` §3.1 says the enrage is
    /// <em>"Bosses only; ordinary fights rely on the 90 s timeout"</em>, and `05` §3.3 caps a duel at
    /// 60 s. `18` writes no answer for a fight with no enrage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The ruling:</b> <c>max(0, horizon − elapsed)</c>, where the horizon is the enrage when
    /// the fight has one and the fight's forced end otherwise. That is the one reading which keeps the
    /// function's own name — <em>time remaining</em> — true in every context, and it invents no
    /// number: both horizons are readings on the context, because <c>70</c>, <c>90</c> and <c>60</c>
    /// are tunables (`17` §1, `05` §3, `11` §4.3) and `21` §3.1 keeps tunables out of code.
    /// </para>
    /// <para>
    /// 🔒 The clamp at zero is the only thing added. Past the enrage there is no time *remaining* —
    /// `05` §3.1's <c>SYS_ENRAGE</c> is already firing, once a second, for the rest of the fight — and
    /// a negative reading would invert the sign of every <c>valueScale</c> step driven by it.
    /// </para>
    /// </remarks>
    private static double TimeRemaining(EffectEvaluationContext context) =>
        Math.Max(0.0, (context.EnrageAtSeconds ?? context.FightHorizonSeconds) - context.BattleTimeSeconds);

    /// <summary>
    /// 🔒 Holder-relative, exactly as `18` §5's target tokens are: the living non-pets on the side
    /// opposite the holder's. On a boss that is the hero side (`18` §7.10's Volatile reading).
    /// </summary>
    private static int LivingEnemyCount(EffectEvaluationContext context)
    {
        var count = 0;

        for (var i = 0; i < context.Actors.Count; i++)
        {
            var actor = context.Actors[i];

            // 05 §3.1 step 6 puts an actor out of play at 0 HP; 05 §3.2 keeps pets out of every
            // enemy set — "Pets cannot be targeted or killed."
            if (actor.IsAlive && actor.Kind != EffectActorKind.PET && actor.Side != context.Holder.Side)
            {
                count++;
            }
        }

        return count;
    }

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

    private static double DieFaceCount(
        ConditionArguments arguments,
        EffectEvaluationContext context,
        ConditionFunction function)
    {
        var faceKind = arguments.FaceKind ?? throw Malformed(
            function.ToString(),
            "it counts one face kind and the term names none",
            "18 §4 types it 'by face kind' — one of 04 §1's six.");

        return Run(context, function).DieFaceCount(faceKind);
    }

    // ------------------------------------------------------------------ the seven comparators

    private static bool Compare(ConditionTerm term, EffectEvaluationContext context)
    {
        // 🔒 The term's SHAPE is validated before its function is read. A malformed term is malformed
        // whatever the state is, and validating first means the failure names the term rather than
        // whichever subject the context happened to be missing as well.
        if (term.Comparator == ConditionComparator.BETWEEN)
        {
            RejectFlag(term);

            var low = term.RangeLow ?? throw MissingBound(term);
            var high = term.RangeHigh ?? throw MissingBound(term);
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
            "the term carries nothing to compare against — neither a value nor a flag",
            "18 §4 writes every comparison as {\"fn\": …, \"op\": …, \"value\": …}.");

        var actual = Read(term.Fn, ConditionArguments.Of(term), context);

        return term.Comparator switch
        {
            // 🔒 Exact equality on doubles, deliberately: `actual` has already been rounded to 4 dp by
            // Read, which is the rule that makes the two sides comparable at all (05 §1.1, 14 §8.2).
            // An epsilon here would be a tolerance 18 §4 does not authorise, and it would make
            // {"op":"eq","value":0.30} fire at 0.3001 on one device and not the other.
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

    private static EffectContextException MissingBound(ConditionTerm term) =>
        Malformed(
            term.Comparator.ToString(),
            "it is a range and one of its two bounds is absent",
            "18 §4 lists 'between' and writes no example, so the encoding is ConditionTerm's " +
            "two-element one — and half a range is not a range.");

    private static EffectContextException Malformed(string token, string missing, string reference) =>
        new(token, missing, reference);
}
