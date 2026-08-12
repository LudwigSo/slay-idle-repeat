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

            case ConditionKind.ALL:
            {
                var all = Combinator(condition);

                for (var i = 0; i < all.Count; i++)
                {
                    // 🔒 Returns at the first operand that decides the answer. Not an optimisation:
                    // `18` §9.3's skip idiom puts {"not":{"fn":"IS_PVP",…}} first precisely so the
                    // run-state operands behind it are never read in a duel, which `05` §3.3 gives
                    // no run at all.
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

    /// <summary>
    /// 🔒 A combinator's operands, rejected when there are none.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>This duplicates <see cref="EffectCondition.All"/>'s guard on purpose, and the duplication
    /// is the point.</b> That guard lives in a <em>factory</em>, and
    /// <see cref="EffectCondition.Operands"/> is an <c>init</c> property defaulting to an empty list —
    /// so <c>new EffectCondition { Kind = ConditionKind.ALL }</c> bypasses it, and so will every JSON
    /// deserialiser M2-02 wires up, since those bind init properties directly. An empty <c>all</c> is
    /// vacuously true and an empty <c>any</c> vacuously false, which means an effect would fire (or
    /// never fire) with its condition still plainly visible in the data. The evaluator is the thing
    /// that actually decides, so the check has to exist here too.
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
    /// ⚠️ <see cref="IsSatisfied"/> reads a <c>null</c> condition as an ungated effect, which is `18`
    /// §1's <c>"condition": null</c> — a rule about an effect's <b>top-level</b> condition, not about
    /// a hole inside a combinator. Without this check a missing operand would make an <c>any</c>
    /// vacuously true, which is the same silent ungating <see cref="Combinator"/> exists to stop, one
    /// level down.
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

    /// <summary>
    /// An actor's HP as the <c>0..1</c> fraction `18` §4 declares.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Clamped to the range the document types, which is not the same as inventing a bound.</b>
    /// `18` §4 states the range; two live paths break it. `05` §4 step 9 applies no floor at zero and
    /// `05` §3.1 step 6 defers <em>removal</em> to the death slot, so an overkilled holder firing its
    /// <c>ON_DEATH</c> effect reads a negative fraction. And a Max HP <em>decrease</em> — a buff
    /// expiring, `18` §9.1's <c>CP_GLASS_HEART</c> re-base — leaves <c>CurrentHp &gt; MaxHp</c>.
    /// Unclamped, <c>SELF_MISSING_HP_PCT</c> then reads <c>-0.2</c> and <c>PK_BERSERK</c> applies
    /// <b>-20% ATK</b> from a perk that only ever adds; <see cref="ValueScale.StepsFor"/>'s own
    /// remarks record that it imposes no lower bound because <em>"every §4 function that drives a
    /// documented scale is non-negative by construction"</em> — this is what makes that true.
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
    /// 🔒 Holder-relative, exactly as `18` §5's target tokens are — and through the <b>same</b>
    /// predicate, so <c>ENEMY_COUNT</c> can never disagree with <c>ALL_ENEMIES</c> about one battle.
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
        RejectAmbiguousOperand(term);

        if (term.Comparator == ConditionComparator.BETWEEN)
        {
            RejectFlag(term);

            var low = term.RangeLow ?? throw MissingBound(term);
            var high = term.RangeHigh ?? throw MissingBound(term);

            if (low > high)
            {
                // An inverted range never fires and never complains — a content authoring error that
                // could never go red, which is exactly what steering S6 calls silently defaulting
                // where the code should fail loudly.
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

    /// <summary>
    /// Rejects a term carrying both a numeric value and a boolean one.
    /// </summary>
    /// <remarks>
    /// 🔒 Without this the flag branch simply wins and the number is discarded in silence:
    /// <c>{"fn":"SELF_HP_PCT","op":"eq","value":0.5,"flag":true}</c> would then hold at <b>any</b>
    /// non-zero HP, because a boolean comparison asks only whether the reading is non-zero. The data
    /// asked for exactly 50%.
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
