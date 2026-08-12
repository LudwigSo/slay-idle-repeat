using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// 🔒 Whether an effect is <b>well-formed for its op</b> — the in-code counterpart of
/// <c>game-data/schema/effect.schema.json</c>'s sixteen key-shape branches, answerable without a
/// battle.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The schema is the authority; this is not a second one.</b> M2-01 records the reason it
/// exists: <em>"anything that builds an effect in code rather than loading authored JSON — the
/// balance harness (`05` §9), a test, <c>InMemoryGame</c> — is outside that enforcement, and is
/// responsible for building shapes the schema would accept."</em> This is how such a caller checks,
/// and it deliberately covers <b>only</b> the op-to-key partition — not id shape, not enum
/// membership, not numeric ranges, all of which the schema states once and the C# types state again
/// by being enums.
/// </para>
/// <para>
/// 🔒 <b>Why the thirteen §2.5 ops are the point of this class.</b> A4: the run and board ops are
/// <em>declared, validated and unit-tested — but not wired</em>. With no resolver, "does it
/// validate" is the only question that can be asked of them at all in M2, and it is a real one: it
/// is what makes the difference between an op that M3 can pick up and an op that was never
/// authorable.
/// </para>
/// <para>
/// ⚠️ <b>The resolver does not trust this.</b> <see cref="EffectOpResolver"/> re-checks every key it
/// uses, and throws its own <see cref="EffectContextException"/>. A resolver that assumed a prior
/// validation would be a guard whose subject set is "whoever remembered to call it" — steering S3's
/// failure shape exactly.
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
                    // 🔒 Only a redirect has a destination. ⚠️ effect.schema.json cannot state this —
                    //    JsonSchemaValidator implements no `not` and no if/then/else, so a
                    //    conditional-required rule is not expressible there. Recorded as a known
                    //    limit of the schema and closed here.
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

            // 🔒 §2.5 — A4. Every argument these need beyond the eight-part shape is UNAUTHORED, and
            //    M3 authors it when it builds the resolvers. What is checkable today is the shape:
            //    the op is declared, it is a run/board op, and nothing combat-side is asked of it.
            case EffectOp.GRANT_CURRENCY:
            case EffectOp.GRANT_ITEM:
            case EffectOp.GRANT_PERK:
            case EffectOp.UPGRADE_PERK:
            case EffectOp.GRANT_REROLL:
            case EffectOp.MOVE_NODES:
            case EffectOp.REVEAL_TILES:
            case EffectOp.RESOLVE_TILE_AGAIN:
            case EffectOp.MODIFY_SHOP:
            case EffectOp.MODIFY_DROP_TABLE:
            case EffectOp.APPLY_CURSE:
            case EffectOp.CLEANSE_CURSE:
                break;

            case EffectOp.MODIFY_DIE_FACE:
                if (effect.NewFace is null)
                {
                    problems.Add("MODIFY_DIE_FACE names no newFace; 18 §7.9 replaces a face WITH one, and " +
                                 "an effect with no replacement is a face deleted");
                }

                break;

            default:
                problems.Add($"op {(int)effect.Op} is not one of 18 §2's 43");
                break;
        }

        // 🔒 Keys that belong to exactly one op, checked against every OTHER op — the schema's
        //    additionalProperties: false, restated for the code path. Without this a
        //    {"op":"STAT_ADD_PCT","charges":3} built in code would validate and silently ignore the 3.
        Exclusive(effect, problems, effect.ToStat is not null, "toStat",
            EffectOp.STAT_CONVERT, EffectOp.STAT_CAP_OVERRIDE);
        Exclusive(effect, problems, effect.Charges is not null, "charges",
            EffectOp.ATTACK_MULT_NEXT, EffectOp.FORCE_CRIT_NEXT);
        Exclusive(effect, problems, effect.CapKind is not null, "capKind", EffectOp.STAT_CAP_OVERRIDE);
        Exclusive(effect, problems, effect.SourceCapPct is not null, "sourceCapPct", EffectOp.SHIELD);
        Exclusive(effect, problems, effect.Archetype is not null, "archetype", EffectOp.SUMMON);
        Exclusive(effect, problems, effect.MaxAlive is not null, "maxAlive", EffectOp.SUMMON);
        Exclusive(effect, problems, effect.StatusTag is not null, "statusTag", EffectOp.REMOVE_STATUS);
        Exclusive(effect, problems, effect.FaceIndex is not null, "faceIndex", EffectOp.MODIFY_DIE_FACE);
        Exclusive(effect, problems, effect.NewFace is not null, "newFace", EffectOp.MODIFY_DIE_FACE);
        Exclusive(effect, problems, effect.Scope is not null, "scope", EffectOp.MODIFY_DIE_FACE);

        // 🔒 The last two op-specific keys. The schema admits `valueMode` on nine ops and `statusId`
        //    on four; without these, {"op":"EXTRA_ATTACK","valueMode":"FLAT"} and
        //    {"op":"DAMAGE","statusId":"BURN"} were well-formed in code and rejected by the schema —
        //    the two enforcement paths disagreeing about the same effect.
        //    ⚠️ These two are stated as PREDICATES rather than as the `params` array the other ten
        //    use, and it is not a style choice: an array literal of four or more constants makes
        //    Roslyn emit a `<PrivateImplementationDetails>/__StaticArrayInitTypeSize=N` blob in the
        //    GLOBAL namespace, which
        //    AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace reports as
        //    an undocumented `30` §11.4 namespace (the nested blob type carries no
        //    CompilerGeneratedAttribute, so the rule's filter misses it). Recorded as errata against
        //    that rule; the ten short lists below are under the threshold and are unaffected.
        //    ⚠️ REVIVE is EXCLUDED although RulesFor gives it a row. 18 §10.1 records E4 —
        //    SURVIVE_LETHAL's valueMode — as taken for that op ALONE, and the schema's REVIVE branch
        //    omits the key accordingly. Having a value-mode table entry is not the same as admitting
        //    an authored valueMode: the table is what the op falls back to, not a key it takes.
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

        // 🔒 And which of 18 §2.2's eight the op actually admits — a rule the schema cannot state at
        //    all, because the sets differ per op inside one branch. Without it
        //    {"op":"HEAL_LEECH","valueMode":"ATK_MULT"} is "well-formed" and throws at fire time.
        RequireAdmittedMode(effect, problems);

        return problems;
    }

    /// <summary>True when the effect's keys are the ones its op takes.</summary>
    internal static bool IsWellFormed(EffectDefinition effect) => Problems(effect).Count == 0;

    /// <summary>
    /// The op's own `18` §2.2 admitted-mode set, from the one table
    /// (<see cref="OpValueRules"/>) the resolver uses.
    /// </summary>
    /// <remarks>
    /// Read from the same table rather than restated, so a mode admitted at fire time and refused
    /// here — or the reverse — cannot happen.
    /// </remarks>
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

    /// <summary>The `18` §2.2 rules for the ops that have them; <c>null</c> for the rest.</summary>
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

    private static void Exclusive(
        EffectDefinition effect, List<string> problems, bool present, string key, params EffectOp[] owners) =>
        ExclusiveTo(effect, problems, present, key, string.Join(", ", owners), owners.Contains(effect.Op));

    /// <summary>The same rule with the ownership stated as a predicate rather than as a list.</summary>
    private static void ExclusiveTo(
        EffectDefinition effect, List<string> problems, bool present, string key, string owners, bool owned)
    {
        if (present && !owned)
        {
            problems.Add(
                $"{effect.Op} carries '{key}', which belongs to [{owners}]. " +
                "game-data/schema/effect.schema.json partitions the 43 ops into closed key shapes so " +
                "that a borrowed key is a failure rather than a field that silently means nothing.");
        }
    }
}
