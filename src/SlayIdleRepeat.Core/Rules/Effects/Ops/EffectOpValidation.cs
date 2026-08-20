using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>Whether an effect is well-formed for its op — the in-code counterpart of the content schema's key-shape branches, answerable without a battle.</summary>
/// <remarks>
/// <para>
/// The schema is the authority; this is not a second one. Anything that builds an effect in code
/// rather than loading authored JSON (the balance harness, a test, <c>InMemoryGame</c>) is outside
/// that enforcement and needs its own check — this covers only the op-to-key partition, not id
/// shape, enum membership or numeric ranges, all of which the C# types already enforce by being enums.
/// </para>
/// <para>
/// The resolver does not trust this: <see cref="EffectOpResolver"/> re-checks every key it uses and
/// throws its own <see cref="EffectContextException"/>, rather than assuming a prior validation ran.
/// </para>
/// </remarks>
internal static class EffectOpValidation
{
    /// <summary>
    /// Every way the effect's keys disagree with its op, in a stable order. Empty means well-formed.
    /// </summary>
    internal static IReadOnlyList<string> Problems(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        var problems = new List<string>();

        switch (effect.Op)
        {
            case EffectOp.STAT_ADD_FLAT:
            case EffectOp.STAT_ADD_PCT:
            case EffectOp.STAT_MULT:
                RequireStat(effect, problems, groupAllowed: true);
                RequireValue(effect, problems);
                break;

            case EffectOp.STAT_SET:
                RequireStat(effect, problems, groupAllowed: false);
                RequireValue(effect, problems);
                break;

            case EffectOp.STAT_CONVERT:
                RequireStat(effect, problems, groupAllowed: false);
                RequireValue(effect, problems);
                RequireToStat(effect, problems, "18 §2.1 converts stat A into stat B; toStat is stat B");
                break;

            case EffectOp.STAT_CAP_OVERRIDE:
                RequireStat(effect, problems, groupAllowed: false);
                RequireValue(effect, problems);
                if (effect.CapKind is null)
                {
                    problems.Add("STAT_CAP_OVERRIDE names no capKind, and 18 §2.1's three — " +
                                 "HEAL_CEILING, STAT_MAX, REDIRECT_EXCESS — are different operations");
                }
                else if (effect.CapKind == StatCapKind.REDIRECT_EXCESS)
                {
                    RequireToStat(effect, problems, "a REDIRECT_EXCESS override sends the overshoot to toStat");
                }
                else if (effect.ToStat is not null)
                {
                    // Only a redirect has a destination; the JSON schema can't express this
                    // conditional-required rule, so it's checked here instead.
                    problems.Add($"a {effect.CapKind} override carries toStat, which belongs to " +
                                 "REDIRECT_EXCESS alone — a raise and a heal ceiling send nothing anywhere");
                }

                break;

            case EffectOp.STAT_COPY:
                RequireStat(effect, problems, groupAllowed: false, copySelectorAllowed: true);
                RequireValue(effect, problems);
                break;

            case EffectOp.DAMAGE:
            case EffectOp.DAMAGE_TRUE:
            case EffectOp.DAMAGE_MAXHP_PCT:
            case EffectOp.HEAL:
            case EffectOp.HEAL_LEECH:
            case EffectOp.SHIELD:
            case EffectOp.REFLECT:
                RequireValue(effect, problems);
                break;

            case EffectOp.APPLY_STATUS:
            case EffectOp.EXTEND_STATUS:
            case EffectOp.IMMUNE_STATUS:
                if (effect.StatusId is null)
                {
                    problems.Add($"{effect.Op} names no statusId, and 18 §2.3 acts on one of 05 §5's twelve");
                }

                if (effect.StatusTag is not null)
                {
                    problems.Add($"{effect.Op} carries a statusTag; 18 §2.3 gives the tag-group form to " +
                                 "REMOVE_STATUS alone");
                }

                break;

            case EffectOp.REMOVE_STATUS:
                if ((effect.StatusId is null) == (effect.StatusTag is null))
                {
                    problems.Add(effect.StatusId is null
                        ? "REMOVE_STATUS names neither a statusId nor a statusTag, and 18 §2.3 offers one " +
                          "or the other"
                        : "REMOVE_STATUS names both a statusId and a statusTag, and 18 authors no " +
                          "precedence between them");
                }

                break;

            case EffectOp.STATUS_POWER_PCT:
            case EffectOp.STATUS_DURATION_PCT:
                RequireValue(effect, problems);
                break;

            case EffectOp.ATTACK_MULT_NEXT:
                RequireValue(effect, problems);
                RequireCharges(effect, problems);
                break;

            case EffectOp.FORCE_CRIT_NEXT:
                RequireCharges(effect, problems);
                if (effect.Value is not null)
                {
                    problems.Add("FORCE_CRIT_NEXT carries a value; 18 §2.4 gives it none — how hard a " +
                                 "forced crit hits is the actor's own CDMG (05 §4 step 4)");
                }

                break;

            case EffectOp.SUMMON:
                RequireValue(effect, problems);
                if (effect.Archetype is null)
                {
                    problems.Add("SUMMON names no archetype; 18 §7.8 writes it as archetype: SWARM");
                }

                if (effect.MaxAlive is { } maxAlive && maxAlive < 1)
                {
                    problems.Add($"SUMMON caps living summons at " +
                                 $"{maxAlive.ToString(CultureInfo.InvariantCulture)}; 18 §7.8's ceiling is a " +
                                 "count of actors, so an effect that may have none alive summons nothing");
                }

                break;

            case EffectOp.EXTRA_ATTACK:
            case EffectOp.REDUCE_COOLDOWN:
            case EffectOp.SURVIVE_LETHAL:
            case EffectOp.REVIVE:
            case EffectOp.SET_TARGET_PRIORITY:
            case EffectOp.DAMAGE_TAKEN_MULT:
                RequireValue(effect, problems);
                break;

            case EffectOp.CLEAR_SUMMONS:
                break;

            case EffectOp.RANDOM_OUTCOME:
                RequireOutcomes(effect, problems);
                if (effect.Value is not null)
                {
                    problems.Add("RANDOM_OUTCOME carries a value; 18 §10.1 E6 gives it none — its table " +
                                 "is the 'outcomes' key and its own number is the 1-based index of the " +
                                 "row that won, which nothing authors");
                }

                break;

            // These ops have no resolver yet; what's checkable today is the shape — the op is
            // declared, it's a run/board op, and nothing combat-side is asked of it.
            case EffectOp.GRANT_CURRENCY:
            case EffectOp.GRANT_ITEM:
            case EffectOp.GRANT_PERK:
            case EffectOp.UPGRADE_PERK:
            case EffectOp.MOVE_NODES:
            case EffectOp.REVEAL_TILES:
            case EffectOp.RESOLVE_TILE_AGAIN:
            case EffectOp.MODIFY_SHOP:
            case EffectOp.MODIFY_DROP_TABLE:
            case EffectOp.APPLY_CURSE:
            case EffectOp.CLEANSE_CURSE:
                break;

            default:
                problems.Add($"op {(int)effect.Op} is not one of 18 §2's 42");
                break;
        }

        // Keys that belong to exactly one op, checked against every other op — without this a
        // {"op":"STAT_ADD_PCT","charges":3} built in code would validate and silently ignore the 3.
        Exclusive(effect, problems, effect.ToStat is not null, "toStat",
            EffectOp.STAT_CONVERT, EffectOp.STAT_CAP_OVERRIDE);
        Exclusive(effect, problems, effect.Charges is not null, "charges",
            EffectOp.ATTACK_MULT_NEXT, EffectOp.FORCE_CRIT_NEXT);
        Exclusive(effect, problems, effect.CapKind is not null, "capKind", EffectOp.STAT_CAP_OVERRIDE);
        Exclusive(effect, problems, effect.SourceCapPct is not null, "sourceCapPct", EffectOp.SHIELD);
        Exclusive(effect, problems, effect.Archetype is not null, "archetype", EffectOp.SUMMON);
        Exclusive(effect, problems, effect.MaxAlive is not null, "maxAlive", EffectOp.SUMMON);
        Exclusive(effect, problems, effect.StatusTag is not null, "statusTag", EffectOp.REMOVE_STATUS);
        Exclusive(effect, problems, effect.Outcomes is not null, "outcomes", EffectOp.RANDOM_OUTCOME);

        // The last two op-specific keys, checked as predicates rather than as the params array the
        // other eight use: an array literal of four-plus constants here would emit a compiler-generated
        // type into the global namespace that trips the namespace-boundary test.
        // REVIVE is excluded even though RulesFor gives it a row — its valueMode is SURVIVE_LETHAL's
        // alone; having a value-mode table entry isn't the same as admitting an authored valueMode.
        ExclusiveTo(
            effect, problems, effect.ValueMode is not null, "valueMode",
            "STAT_SET, the six 18 §2.2 ops, SHIELD and SURVIVE_LETHAL",
            (RulesFor(effect.Op) is not null && effect.Op != EffectOp.REVIVE)
                || effect.Op == EffectOp.STAT_SET);

        ExclusiveTo(
            effect, problems, effect.StatusId is not null, "statusId",
            "18 §2.3's four status-naming ops",
            effect.Op is EffectOp.APPLY_STATUS or EffectOp.REMOVE_STATUS
                      or EffectOp.EXTEND_STATUS or EffectOp.IMMUNE_STATUS);

        // Which value modes the op actually admits — the schema can't state this since the sets
        // differ per op inside one branch. Without it a mode mismatch is well-formed here and throws
        // only at fire time.
        RequireAdmittedMode(effect, problems);

        return problems;
    }

    /// <summary>True when the effect's keys are the ones its op takes.</summary>
    internal static bool IsWellFormed(EffectDefinition effect) => Problems(effect).Count == 0;

    /// <summary>The op's own admitted-mode set, from the one table (<see cref="OpValueRules"/>) the resolver uses.</summary>
    /// <remarks>Read from the same table rather than restated, so a mode admitted at fire time and refused here can't happen.</remarks>
    private static void RequireAdmittedMode(EffectDefinition effect, List<string> problems)
    {
        if (effect.ValueMode is not { } mode)
        {
            return;
        }

        var rules = RulesFor(effect.Op);
        if (rules is not null && !rules.Admits.Contains(mode))
        {
            problems.Add(
                $"{effect.Op} carries valueMode {mode}, and 18 §2.2 gives it " +
                $"[{string.Join(", ", rules.Admits)}] — {rules.Reason}");
        }
    }

    /// <summary>The value-mode rules for the ops that have them; <c>null</c> for the rest.</summary>
    private static OpValueRules? RulesFor(EffectOp op) => op switch
    {
        EffectOp.DAMAGE => OpValueRules.Damage,
        EffectOp.DAMAGE_TRUE => OpValueRules.DamageTrue,
        EffectOp.DAMAGE_MAXHP_PCT => OpValueRules.DamageMaxHpPct,
        EffectOp.HEAL => OpValueRules.Heal,
        EffectOp.HEAL_LEECH => OpValueRules.HealLeech,
        EffectOp.SHIELD => OpValueRules.Shield,
        EffectOp.REFLECT => OpValueRules.Reflect,
        EffectOp.SURVIVE_LETHAL => OpValueRules.SurviveLethal,
        EffectOp.REVIVE => OpValueRules.Revive,
        _ => null,
    };

    private static void RequireValue(EffectDefinition effect, List<string> problems)
    {
        if (effect.Value is null)
        {
            problems.Add($"{effect.Op} carries no value, and 18 §1 makes value the effect's magnitude");
        }
    }

    private static void RequireStat(
        EffectDefinition effect, List<string> problems, bool groupAllowed, bool copySelectorAllowed = false)
    {
        if (effect.Stat is not { } selector)
        {
            problems.Add($"{effect.Op} names no stat, and every 18 §2.1 stat op does");
            return;
        }

        var permitted = selector.Kind switch
        {
            StatSelectorKind.SINGLE => true,
            StatSelectorKind.ALL_COMBAT => groupAllowed,
            StatSelectorKind.HIGHEST_PCT_BONUS => copySelectorAllowed,
            _ => false,
        };

        if (!permitted)
        {
            problems.Add($"{effect.Op} names the {selector} selector; 18 §9.1 admits ALL_COMBAT on the " +
                         "three aggregating stat ops only, and HIGHEST_PCT_BONUS on STAT_COPY only");
        }
    }

    private static void RequireToStat(EffectDefinition effect, List<string> problems, string why)
    {
        if (effect.ToStat is null)
        {
            problems.Add($"{effect.Op} names no toStat — {why}");
        }
        else if (effect.Stat is { Kind: StatSelectorKind.SINGLE, Stat: { } from } && from == effect.ToStat)
        {
            problems.Add($"{effect.Op} names {from} as both source and destination, which moves nothing");
        }
    }

    private static void RequireCharges(EffectDefinition effect, List<string> problems)
    {
        if (effect.Charges is not { } charges)
        {
            problems.Add($"{effect.Op} names no charges, and 18 §2.4 covers 'the next N attacks'");
        }
        else if (charges < 1)
        {
            problems.Add($"{effect.Op} grants {charges.ToString(CultureInfo.InvariantCulture)} charges; " +
                         "N is a whole number of attacks, so at least one");
        }
    }

    /// <summary>
    /// <c>RANDOM_OUTCOME</c>'s <c>outcomes</c> table, checked against everything
    /// <see cref="Rng.DeterministicRng.WeightedPick{T}"/> would refuse at fire time, plus two rules
    /// only this op has: a choice needs two rows, and a row may not name the roll itself.
    /// </summary>
    /// <remarks>Each rule adds its own message, since several are reachable at once from one badly authored table.</remarks>
    private static void RequireOutcomes(EffectDefinition effect, List<string> problems)
    {
        if (effect.Outcomes is not { } outcomes)
        {
            problems.Add("RANDOM_OUTCOME names no outcomes, and 18 §10.1 E6 makes that table the whole " +
                         "op — 17 §9's Roll of Fate is one d6 with three mutually exclusive results");
            return;
        }

        if (outcomes.Count < 2)
        {
            problems.Add($"RANDOM_OUTCOME offers {outcomes.Count.ToString(CultureInfo.InvariantCulture)} " +
                         "outcome(s); one outcome is not a choice — author that effect directly rather " +
                         "than spending a combat draw index to reach it");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var total = 0.0;

        foreach (var outcome in outcomes)
        {
            if (!double.IsFinite(outcome.Weight) || outcome.Weight < 0.0)
            {
                problems.Add($"RANDOM_OUTCOME weighs '{outcome.EffectId}' at " +
                             $"{outcome.Weight.ToString("R", CultureInfo.InvariantCulture)}; 14 §8.0 takes " +
                             "a finite, non-negative weight — anything else makes the cumulative walk " +
                             "non-monotonic and its answer arbitrary");
            }
            else
            {
                total += outcome.Weight;
            }

            // The two checks below also read the id, so a blank one stops here rather than being
            // reported three times over.
            if (string.IsNullOrWhiteSpace(outcome.EffectId))
            {
                problems.Add("a RANDOM_OUTCOME row names no effectId; 18 §10.1 E6's rows ARE effect " +
                             "ids — a blank one names no sibling of the content that owns the roll, " +
                             "and there is no registry a wider lookup could fall back to");
                continue;
            }

            if (!seen.Add(outcome.EffectId))
            {
                problems.Add($"RANDOM_OUTCOME names '{outcome.EffectId}' twice; 14 §8.0's weighted walk " +
                             "would pick the FIRST row every time and the second's weight would silently " +
                             "only widen the first's share");
            }

            if (string.Equals(outcome.EffectId, effect.Id, StringComparison.Ordinal))
            {
                problems.Add($"RANDOM_OUTCOME names its own id '{effect.Id}' as an outcome, which rolls " +
                             "the roll — an unbounded recursion that spends a draw index per turn of it");
            }
        }

        if (outcomes.Count > 0 && total <= 0.0)
        {
            problems.Add("every RANDOM_OUTCOME row weighs zero, so no row can be picked; 14 §8.0's walk " +
                         "is strict, which is how content disables ONE row without disabling the roll");
        }
    }

    /// <remarks>
    /// The owner list is joined inside the failure, not on the way in — this runs many times per
    /// <see cref="Problems"/> call, and <see cref="Problems"/> is on a battle path
    /// (<c>CombatFlowOps.RandomOutcome</c> re-reads its own table before every draw), so an eagerly
    /// built message would allocate strings per roll to describe a failure that never happened.
    /// </remarks>
    private static void Exclusive(
        EffectDefinition effect, List<string> problems, bool present, string key, params EffectOp[] owners)
    {
        if (!present || owners.Contains(effect.Op))
        {
            return;
        }

        ExclusiveTo(effect, problems, present: true, key, string.Join(", ", owners), owned: false);
    }

    /// <summary>The same rule with the ownership stated as a predicate rather than as a list.</summary>
    private static void ExclusiveTo(
        EffectDefinition effect, List<string> problems, bool present, string key, string owners, bool owned)
    {
        if (present && !owned)
        {
            problems.Add(
                $"{effect.Op} carries '{key}', which belongs to [{owners}]. " +
                "game-data/schema/effect.schema.json partitions the 44 ops into closed key shapes so " +
                "that a borrowed key is a failure rather than a field that silently means nothing.");
        }
    }
}
