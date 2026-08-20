using System.Text;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>Tests <c>schema/effect.schema.json</c>, the vocabulary schema for embedded effects.</summary>
/// <remarks>
/// This schema governs no data file, so the loader never validates an instance against it — these
/// cases are what actually exercises it. The parity cases keep the C# enums and the schema enums,
/// two statements of one vocabulary, in agreement.
/// </remarks>
public sealed class EffectSchemaTests
{
    private const string SchemaPath = "schema/effect.schema.json";

    private static readonly Lazy<ContentValue> LazySchema = new(ReadSchema);

    private static ContentValue Schema => LazySchema.Value;

    // ---------------------------------------------------------------- the schema itself

    /// <summary>An unimplemented schema keyword is a build failure, never a silent pass; the sweep covers branches no instance reaches.</summary>
    [Fact]
    public void The_schema_uses_only_keywords_the_validator_implements()
    {
        JsonSchemaValidator.CheckSchemaKeywords(Schema, SchemaPath).ShouldBeEmpty();
    }

    /// <summary>Every op appears in exactly one branch of the schema's root <c>oneOf</c>.</summary>
    /// <remarks>
    /// An op in two branches fails validation for every instance (it matches two branches); an op
    /// in none makes it unauthorable while <c>EffectOp</c> still declares it. Both are silent until
    /// a content file happens to use that op.
    /// </remarks>
    [Fact]
    public void Every_op_appears_in_exactly_one_branch_of_the_schema()
    {
        var declared = Enum.GetNames<EffectOp>().ToHashSet(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var op in BranchOps())
        {
            counts[op] = counts.GetValueOrDefault(op) + 1;
        }

        counts.Keys.Where(op => !declared.Contains(op))
              .ShouldBeEmpty("the schema names an op that EffectOp does not declare");

        declared.Where(op => !counts.ContainsKey(op))
                .ShouldBeEmpty("EffectOp declares an op no schema branch admits, so it is unauthorable");

        counts.Where(c => c.Value > 1).Select(c => c.Key)
              .ShouldBeEmpty("an op in two branches matches two oneOf branches and can never validate");

        counts.Count.ShouldBe(42, "18 §11 — and S3's floor under the loops above");
    }

    /// <summary>The same partition over the 23 trigger kinds and the schema's trigger branches.</summary>
    [Fact]
    public void Every_trigger_kind_appears_in_exactly_one_trigger_branch_of_the_schema()
    {
        var declared = Enum.GetNames<TriggerKind>().ToHashSet(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var kind in TriggerBranchKinds())
        {
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }

        counts.Keys.Where(k => !declared.Contains(k)).ShouldBeEmpty();
        declared.Where(k => !counts.ContainsKey(k)).ShouldBeEmpty();
        counts.Where(c => c.Value > 1).Select(c => c.Key).ShouldBeEmpty();

        counts.Count.ShouldBe(23, "18 §11");
    }

    // ---------------------------------------------------------------- C# ↔ schema parity

    [Theory]
    [InlineData("target", typeof(EffectTarget))]
    [InlineData("stat", typeof(StatId))]
    [InlineData("valueMode", typeof(ValueMode))]
    [InlineData("conditionFunction", typeof(ConditionFunction))]
    public void A_schema_enum_holds_exactly_the_members_its_C_sharp_enum_declares(string definition, Type enumType)
    {
        Members(definition).ShouldBe(
            Enum.GetNames(enumType).OrderBy(n => n, StringComparer.Ordinal),
            Case.Sensitive,
            $"$defs/{definition} and {enumType.Name} are two statements of one vocabulary");
    }

    [Fact]
    public void The_duration_and_stacking_enums_match_their_C_sharp_declarations()
    {
        Members("duration", "properties", "scope").ShouldBe(
            Enum.GetNames<DurationScope>().OrderBy(n => n, StringComparer.Ordinal));

        Members("duration", "properties", "until").ShouldBe(
            Enum.GetNames<DurationTerminator>().OrderBy(n => n, StringComparer.Ordinal));

        Members("stacking", "properties", "mode").ShouldBe(
            Enum.GetNames<StackingMode>().OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void The_comparators_match_their_C_sharp_declarations_lower_cased()
    {
        Members("comparator").ShouldBe(
            Enum.GetNames<ConditionComparator>()
                .Select(n => n.ToLowerInvariant())
                .OrderBy(n => n, StringComparer.Ordinal),
            Case.Sensitive,
            "18 §4 writes the comparators in lower case, alone among the DSL's tokens");
    }

    /// <summary>ALL_COMBAT is admitted only by the three aggregating stat ops, and is not a member of <c>stat</c>.</summary>
    [Fact]
    public void ALL_COMBAT_is_a_selector_and_not_a_stat()
    {
        Members("stat").ShouldNotContain("ALL_COMBAT");
        Members("statSelector").ShouldContain("ALL_COMBAT");
        Members("statCopySelector").ShouldNotContain("ALL_COMBAT");

        Members("statSelector").ShouldBe(
            Enum.GetNames<StatId>().Append("ALL_COMBAT").OrderBy(n => n, StringComparer.Ordinal));

        Members("statCopySelector").ShouldBe(
            Enum.GetNames<StatId>().Append("HIGHEST_PCT_BONUS").OrderBy(n => n, StringComparer.Ordinal));
    }

    // ---------------------------------------------------------------- 18's own worked examples

    [Theory]
    [MemberData(nameof(WorkedExamples))]
    public void A_worked_example_from_18_validates(string name, string json)
    {
        name.ShouldNotBeNullOrWhiteSpace();

        Validate(json).ShouldBeEmpty();
    }

    public static TheoryData<string, string> WorkedExamples() => new()
    {
        {
            "18 §1 — the canonical effect",
            """
            { "id": "PK_SHARP_EDGE_T1_ATK", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.12,
              "valueScale": null, "trigger": { "kind": "ALWAYS" }, "condition": null,
              "target": "SELF", "duration": null,
              "stacking": { "mode": "ADDITIVE", "maxStacks": 1 }, "tags": ["offense"] }
            """
        },
        {
            "18 §1.1 — PK_BERSERK, capped valueScale",
            """
            { "id": "PK_BERSERK_T1", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.01,
              "trigger": {"kind":"ALWAYS"}, "target": "SELF",
              "valueScale": { "fn": "SELF_MISSING_HP_PCT", "per": 0.01, "cap": 45 } }
            """
        },
        {
            "18 §1.1 — PK_HOARD, uncapped valueScale",
            """
            { "id": "PK_HOARD", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.01,
              "trigger": {"kind":"ALWAYS"}, "target": "SELF",
              "valueScale": { "fn": "GOLD_HELD", "per": 100, "cap": null } }
            """
        },
        {
            // The STATUS_STACKS valueScale function needs an argument (statusId) that was originally
            // offered with no field to carry it; conditionTerm's keys fill the gap.
            "18 §1.1 — a valueScale over STATUS_STACKS, which needs an argument",
            """
            { "id": "PK_SUNDERER", "op": "STAT_ADD_PCT", "stat": "DMG_PCT", "value": 0.05,
              "trigger": {"kind":"ALWAYS"}, "target": "CURRENT_TARGET",
              "valueScale": { "fn": "STATUS_STACKS", "per": 1, "cap": 5, "statusId": "SUNDER" } }
            """
        },
        {
            "18 §2.2 — PK_TRANSFUSION, an overheal shield with a source cap",
            """
            { "id": "PK_TRANSFUSION", "op": "SHIELD", "valueMode": "OVERHEAL_AMOUNT", "value": 1.0,
              "sourceCapPct": 0.20, "trigger": {"kind":"ON_HEAL"}, "target": "SELF" }
            """
        },
        {
            "18 §7.2 — PK_EXECUTIONER, a condition",
            """
            { "id": "PK_EXECUTIONER_T1", "op": "STAT_ADD_PCT", "stat": "DMG_PCT", "value": 0.25,
              "trigger": {"kind":"ALWAYS"}, "target": "SELF",
              "condition": {"fn":"TARGET_HP_PCT","op":"lt","value":0.30} }
            """
        },
        {
            "18 §7.3 — PK_FLURRY, everyNth",
            """
            { "id": "PK_FLURRY_T1", "op": "EXTRA_ATTACK", "value": 1,
              "trigger": {"kind":"ON_ATTACK","everyNth":5}, "target": "CURRENT_TARGET" }
            """
        },
        {
            // Without valueMode, "value": 1 would read as a fraction of Max HP (full health)
            // instead of a flat 1 HP.
            "18 §7.4 — PK_UNBREAKABLE, ON_LETHAL once, no target, FLAT 1 HP",
            """
            { "id": "PK_UNBREAKABLE_T1_SURVIVE", "op": "SURVIVE_LETHAL", "value": 1,
              "valueMode": "FLAT", "trigger": {"kind":"ON_LETHAL","once":true} }
            """
        },
        {
            "18 §7.5 — CP_BLOOD_PRICE's drawback, a tagged Max-HP cost",
            """
            { "id": "CP_BLOOD_PRICE_COST", "op": "DAMAGE_MAXHP_PCT", "value": 0.03,
              "trigger": {"kind":"ON_BATTLE_END"}, "target": "SELF", "tags": ["drawback"] }
            """
        },
        {
            "18 §7.6 — Avatar of War's cap override",
            """
            { "id": "TAL_AVATAR_OF_WAR_CAP", "op": "STAT_CAP_OVERRIDE", "stat": "MAX_HP", "value": 0.80,
              "capKind": "HEAL_CEILING", "trigger": {"kind":"ALWAYS"} }
            """
        },
        {
            "18 §7.7 — PET_STORMFANG's active, an effect with no trigger of its own",
            """
            { "id": "PET_STORMFANG_ACTIVE_STUN", "op": "APPLY_STATUS", "statusId": "STUN",
              "duration": {"seconds":1.0,"scope":"BATTLE"}, "target": "ALL_ENEMIES" }
            """
        },
        {
            "18 §7.8 — Thornmaw's periodic summon with maxAlive",
            """
            { "id": "BOSS_THORNMAW_P3_SWARM_PERIODIC", "op": "SUMMON", "archetype": "SWARM", "value": 2,
              "maxAlive": 3, "trigger": {"kind":"PERIODIC","interval":12.0} }
            """
        },
        {
            "18 §7.10 — PK_STALWART, an any over the attacker predicates",
            """
            { "id": "PK_STALWART_T1", "op": "DAMAGE_TAKEN_MULT", "value": 0.80,
              "trigger": {"kind":"ALWAYS"}, "target": "SELF",
              "condition": { "any": [
                  { "fn": "ATTACKER_IS_ELITE", "op": "eq", "value": true },
                  { "fn": "ATTACKER_IS_BOSS",  "op": "eq", "value": true } ] } }
            """
        },
        {
            "18 §7.10 — PK_CLEAVE, OTHER_ENEMIES",
            """
            { "id": "PK_CLEAVE_T1", "op": "DAMAGE", "value": 0.40, "trigger": {"kind":"ON_HIT"},
              "target": "OTHER_ENEMIES" }
            """
        },
        {
            "18 §7.10 — the Volatile elite's ON_DEATH explosion",
            """
            { "id": "EL_VOLATILE_EXPLODE", "op": "DAMAGE_MAXHP_PCT", "value": 0.15,
              "valueMode": "TARGET_MAXHP_PCT", "trigger": {"kind":"ON_DEATH"}, "target": "ALL_ENEMIES" }
            """
        },
        {
            "18 §7.10 — Ossify, a duration with both a timer and a terminator",
            """
            { "id": "BOSS_OSSUARY_KING_OSSIFY_DR", "op": "STAT_ADD_PCT", "stat": "DR_PCT", "value": 0.30,
              "trigger": {"kind":"PERIODIC","interval":14.0}, "target": "SELF",
              "duration": { "seconds": 6.0, "scope": "BATTLE", "until": "WARD_BROKEN" } }
            """
        },
        {
            "18 §7.10 — Bog Air, a PHASE scope with no timer",
            """
            { "id": "BOSS_GULGROT_BOG_AIR", "op": "STAT_ADD_PCT", "stat": "HEAL_PCT", "value": -0.35,
              "trigger": {"kind":"ON_PHASE_ENTER","phase":2}, "target": "ALL_ENEMIES",
              "duration": { "scope": "PHASE" } }
            """
        },
        {
            "18 §9.1 — CP_GLASS_HEART's ALL_COMBAT multiplier, no trigger",
            """
            { "id": "CP_GLASS_HEART_MULT", "op": "STAT_MULT", "stat": "ALL_COMBAT", "value": 2.0 }
            """
        },
        {
            "18 §9.1 — CP_GLASS_HEART's Max-HP set",
            """
            { "id": "CP_GLASS_HEART_SET_HP", "op": "STAT_SET", "stat": "MAX_HP", "value": 1.0,
              "valueMode": "FLAT" }
            """
        },
        {
            "05 §3.1 — the built-in SYS_ENRAGE, expressed with no new concept",
            """
            { "id": "SYS_ENRAGE", "op": "STAT_MULT", "stat": "ATK", "value": 1.08,
              "trigger": {"kind":"PERIODIC","interval":1.0,"startDelay":70.0}, "target": "SELF",
              "duration": {"scope":"BATTLE"},
              "stacking": {"mode":"MULTIPLICATIVE","maxStacks":null} }
            """
        },
        {
            "17 §8 — Sporequeen's sporelings, a deprioritising targeting weight",
            """
            { "id": "BOSS_SPOREQUEEN_SPORELING_PRIORITY", "op": "SET_TARGET_PRIORITY", "value": -1,
              "trigger": {"kind":"ON_BATTLE_START"}, "target": "SELF" }
            """
        },

        // ---- effects that appear inside multi-effect snippets, several with shapes nothing else exercises
        {
            "18 §7.1 — PK_SHARP_EDGE, the simplest effect in the document",
            """
            { "id": "PK_SHARP_EDGE_T1", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.12,
              "trigger": {"kind":"ALWAYS"}, "target": "SELF" }
            """
        },
        {
            "18 §7.4 — PK_UNBREAKABLE's second effect: a SELF_MAXHP_PCT shield on ON_LETHAL once",
            """
            { "id": "PK_UNBREAKABLE_T1_WARD", "op": "SHIELD", "value": 0.25,
              "valueMode": "SELF_MAXHP_PCT", "trigger": {"kind":"ON_LETHAL","once":true},
              "target": "SELF" }
            """
        },
        {
            "18 §7.5 — CP_BLOOD_PRICE's benefit, an ALWAYS effect with no target",
            """
            { "id": "CP_BLOOD_PRICE_ATK", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.45,
              "trigger": {"kind":"ALWAYS"} }
            """
        },
        {
            "18 §7.6 — Avatar of War's STAT_MULT, no target",
            """
            { "id": "TAL_AVATAR_OF_WAR_ATK", "op": "STAT_MULT", "stat": "ATK", "value": 1.20,
              "trigger": {"kind":"ALWAYS"} }
            """
        },
        {
            "18 §7.7 — PET_STORMFANG's aura",
            """
            { "id": "PET_STORMFANG_AURA_ASPD", "op": "STAT_ADD_PCT", "stat": "ASPD", "value": 0.06,
              "trigger": {"kind":"ALWAYS"} }
            """
        },
        {
            "18 §7.7 — PET_STORMFANG's active damage: no trigger, no valueMode",
            """
            { "id": "PET_STORMFANG_ACTIVE_DAMAGE", "op": "DAMAGE", "value": 2.0,
              "target": "ALL_ENEMIES" }
            """
        },
        {
            "18 §7.8 — Thornmaw's phase-3 entry summon",
            """
            { "id": "BOSS_THORNMAW_P3_SWARM_ENTRY", "op": "SUMMON", "archetype": "SWARM", "value": 2,
              "trigger": {"kind":"ON_PHASE_ENTER","phase":3} }
            """
        },
        {
            "18 §7.8 — Thornmaw's phase-3 RAGE, a 999-second BATTLE duration",
            """
            { "id": "BOSS_THORNMAW_P3_RAGE", "op": "APPLY_STATUS", "statusId": "RAGE", "value": 0.30,
              "duration": {"seconds":999,"scope":"BATTLE"},
              "trigger": {"kind":"ON_PHASE_ENTER","phase":3}, "target": "SELF" }
            """
        },
        {
            "18 §7.10 — Ossify's ward, the first of the pair",
            """
            { "id": "BOSS_OSSUARY_KING_OSSIFY_WARD", "op": "SHIELD", "value": 0.20,
              "valueMode": "SELF_MAXHP_PCT", "trigger": {"kind":"PERIODIC","interval":14.0},
              "target": "SELF" }
            """
        },

        // ---- shapes the schema declares but no worked example writes; previously unexercised
        {
            "18 §4 — the worked 'all' combinator, the only combinator 18 writes outside §7.10",
            """
            { "id": "PK_LAST_STAND", "op": "STAT_ADD_PCT", "stat": "DMG_PCT", "value": 0.20,
              "trigger": {"kind":"ALWAYS"}, "target": "SELF",
              "condition": { "all": [
                  { "fn": "SELF_HP_PCT", "op": "gte", "value": 1.0 },
                  { "fn": "ENEMY_COUNT",  "op": "eq",  "value": 1 } ] } }
            """
        },
        {
            "18 §4 — the 'not' combinator, and IS_PVP as the gate 18 §9.3 describes",
            """
            { "id": "GEAR_AFFIX_GOLD_GAIN", "op": "STAT_ADD_PCT", "stat": "GOLD_PCT", "value": 0.10,
              "trigger": {"kind":"ALWAYS"}, "target": "SELF",
              "condition": { "not": { "fn": "IS_PVP", "op": "eq", "value": true } } }
            """
        },
        {
            "18 §4 — 'between', whose two-element form is an assumption this pins",
            """
            { "id": "PK_MIDGAME", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.10,
              "trigger": {"kind":"ALWAYS"},
              "condition": { "fn": "BATTLE_TIME", "op": "between", "value": [10, 40] } }
            """
        },
        {
            "18 §4 — a condition argument key: STATUS_STACKS by status id, nested under 'any'",
            """
            { "id": "PK_PLAGUEBEARER", "op": "STAT_ADD_PCT", "stat": "DMG_PCT", "value": 0.15,
              "trigger": {"kind":"ALWAYS"},
              "condition": { "any": [
                  { "fn": "STATUS_STACKS", "op": "gte", "value": 3, "statusId": "POISON" },
                  { "fn": "HAS_STATUS", "op": "eq", "value": true, "statusId": "BLEED" } ] } }
            """
        },
        {
            "18 §4 — PERK_COUNT by category, inside an all",
            """
            { "id": "PK_STARGAZER", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.05,
              "trigger": {"kind":"ALWAYS"},
              "condition": { "all": [
                  { "fn": "GOLD_HELD", "op": "gte", "value": 100 },
                  { "fn": "PERK_COUNT", "op": "gte", "value": 3, "category": "OFFENSE" } ] } }
            """
        },
        {
            "18 §6 — the stacking block with refreshOnReapply, which no §7 example writes",
            """
            { "id": "PK_MOMENTUM", "op": "STAT_ADD_PCT", "stat": "ASPD", "value": 0.04,
              "trigger": {"kind":"ON_KILL","everyNth":1}, "target": "SELF",
              "duration": {"seconds":4.0,"scope":"BATTLE"},
              "stacking": {"mode":"ADDITIVE","maxStacks":5,"refreshOnReapply":true} }
            """
        },
        {
            "18 §9.1 — an explicit null trigger, the form the type branch admits",
            """
            { "id": "CP_GLASS_HEART_MULT_EXPLICIT", "op": "STAT_MULT", "stat": "ALL_COMBAT",
              "value": 2.0, "trigger": null }
            """
        },
        {
            "17 §6 — Rimehold's Core, a state flag rather than a second actor",
            """
            { "id": "BOSS_RIMEHOLD_CORE_VULNERABLE", "op": "DAMAGE_TAKEN_MULT", "value": 1.6,
              "trigger": {"kind":"ON_PHASE_ENTER","phase":2}, "target": "SELF",
              "duration": {"scope":"PHASE"} }
            """
        },
        {
            "17 §4 — the Ossuary King's Rise Again, despawning its own summons",
            """
            { "id": "BOSS_OSSUARY_KING_CLEAR_SUMMONS", "op": "CLEAR_SUMMONS",
              "trigger": {"kind":"ON_PHASE_ENTER","phase":3}, "target": "SELF" }
            """
        },
        {
            "18 §5 — OWNER, the target that skips on a non-summon (Sporequeen's sporelings)",
            """
            { "id": "BOSS_SPOREQUEEN_SPORELING_DEATH_HEAL", "op": "HEAL", "value": 0.02,
              "valueMode": "SELF_MAXHP_PCT", "trigger": {"kind":"ON_DEATH"}, "target": "OWNER" }
            """
        },
    };

    // ---------------------------------------------------------------- three failure classes

    /// <summary>An unknown op name must fail schema validation.</summary>
    [Fact]
    public void An_unknown_op_is_rejected()
    {
        var issues = Validate("""
        { "id": "PK_TIME_STOP", "op": "STOP_TIME", "value": 1.0, "trigger": {"kind":"ALWAYS"} }
        """);

        issues.ShouldNotBeEmpty();
        issues.ShouldContain(
            i => i.Message.Contains("STOP_TIME", StringComparison.Ordinal),
            "the finding has to name the op, or it tells the author nothing about what to add");
    }

    /// <summary>An unknown key, on an op whose branch is otherwise satisfied.</summary>
    [Fact]
    public void An_unknown_key_is_rejected()
    {
        var issues = Validate("""
        { "id": "PK_SHARP_EDGE_T1_ATK", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.12,
          "trigger": {"kind":"ALWAYS"}, "target": "SELF", "frobnicate": true }
        """);

        issues.ShouldNotBeEmpty();
        issues.ShouldContain(i => i.Message.Contains("frobnicate", StringComparison.Ordinal));
    }

    /// <summary>An unknown member of a closed enum — here, a stat that does not exist.</summary>
    [Fact]
    public void An_unknown_enum_member_is_rejected()
    {
        var issues = Validate("""
        { "id": "PK_LUCK", "op": "STAT_ADD_PCT", "stat": "LUCK", "value": 0.12,
          "trigger": {"kind":"ALWAYS"}, "target": "SELF" }
        """);

        issues.ShouldNotBeEmpty();
        issues.ShouldContain(i => i.Message.Contains("LUCK", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the closure the oneOf buys

    /// <summary>ALL_COMBAT is rejected where one concrete stat is required.</summary>
    /// <remarks>
    /// Asserted as a single-token edit between an accepted and a rejected document, rather than a
    /// message match — a oneOf failure reports whichever branch missed by the smallest margin, so
    /// matching on "ALL_COMBAT" in the message could pin the wrong branch. The extra required keys
    /// per op (<c>toStat</c>, <c>capKind</c>) keep the control valid so the stat token stays the
    /// only edit.
    /// </remarks>
    [Theory]
    [InlineData("STAT_SET", "")]
    [InlineData("STAT_CONVERT", ", \"toStat\": \"ATK\"")]
    [InlineData("STAT_CAP_OVERRIDE", ", \"capKind\": \"STAT_MAX\"")]
    public void ALL_COMBAT_is_rejected_where_one_concrete_stat_is_required(string op, string requiredKeys)
    {
        Validate($$"""
        { "id": "CP_GLASS_HEART_BAD", "op": "{{op}}", "stat": "MAX_HP", "value": 1.0{{requiredKeys}} }
        """).ShouldBeEmpty($"the control: {op} over one concrete stat is valid");

        Validate($$"""
        { "id": "CP_GLASS_HEART_BAD", "op": "{{op}}", "stat": "ALL_COMBAT", "value": 1.0{{requiredKeys}} }
        """).ShouldNotBeEmpty($"{op} needs one stat, not a group of fourteen — and the stat token is the only edit");
    }

    [Fact]
    public void HIGHEST_PCT_BONUS_is_accepted_only_by_STAT_COPY()
    {
        Validate("""
        { "id": "BOSS_COGITATOR_RECALIBRATE", "op": "STAT_COPY", "stat": "HIGHEST_PCT_BONUS",
          "value": 1.0, "target": "SELF" }
        """).ShouldBeEmpty();

        Validate("""
        { "id": "PK_BAD_COPY", "op": "STAT_ADD_PCT", "stat": "HIGHEST_PCT_BONUS", "value": 1.0 }
        """).ShouldNotBeEmpty();
    }

    /// <summary>The Dicelord's Roll of Fate, both authored tables: phase 1's 2/2/2 over three outcomes and phase 2's 4/2 over two.</summary>
    [Fact]
    public void The_Dicelords_Roll_of_Fate_validates_as_authored()
    {
        Validate("""
        { "id": "BOSS_DICELORD_ROLL_OF_FATE_P1", "op": "RANDOM_OUTCOME",
          "trigger": { "kind": "PERIODIC", "interval": 10.0 }, "target": "SELF",
          "outcomes": [ { "effectId": "BOSS_DICELORD_FATE_BOSS_ATK",  "weight": 2 },
                        { "effectId": "BOSS_DICELORD_FATE_HERO_ATK",  "weight": 2 },
                        { "effectId": "BOSS_DICELORD_FATE_BOTH_ASPD", "weight": 2 } ] }
        """).ShouldBeEmpty();

        Validate("""
        { "id": "BOSS_DICELORD_ROLL_OF_FATE_P2", "op": "RANDOM_OUTCOME",
          "trigger": { "kind": "PERIODIC", "interval": 10.0 }, "target": "SELF",
          "outcomes": [ { "effectId": "BOSS_DICELORD_FATE_BOSS_ATK",  "weight": 4 },
                        { "effectId": "BOSS_DICELORD_FATE_BOTH_ASPD", "weight": 2 } ] }
        """).ShouldBeEmpty();
    }

    /// <summary>
    /// Each row is a single-token edit away from a control asserted in the test body (not just
    /// implied): without proving <c>{id, op, outcomes}</c> alone is valid, a rejection could equally
    /// mean the schema requires the dropped trigger/target keys rather than rejecting the table.
    /// </summary>
    [Theory]
    // one outcome is not a choice — $defs/outcomes has minItems 2
    [InlineData("""
        "outcomes": [ { "effectId": "BOSS_DICELORD_FATE_BOSS_ATK", "weight": 2 } ]
        """)]
    // no table at all: 'outcomes' is required, and the table IS the op
    [InlineData("\"target\": \"SELF\"")]
    // a negative weight — must be finite and non-negative
    [InlineData("""
        "outcomes": [ { "effectId": "EFF_A", "weight": -1 }, { "effectId": "EFF_B", "weight": 2 } ]
        """)]
    // a row key nobody authored: the row object is additionalProperties: false too
    [InlineData("""
        "outcomes": [ { "effectId": "EFF_A", "weight": 1, "chance": 0.5 },
                      { "effectId": "EFF_B", "weight": 2 } ]
        """)]
    // two identical rows — uniqueItems. This catches the identical pair only; one effect named
    // twice at DIFFERENT weights is EffectOpValidation's, which the $defs description records.
    [InlineData("""
        "outcomes": [ { "effectId": "EFF_A", "weight": 1 }, { "effectId": "EFF_A", "weight": 1 } ]
        """)]
    public void A_malformed_RANDOM_OUTCOME_table_is_rejected(string body)
    {
        Validate("""
        { "id": "BOSS_DICELORD_ROLL_OF_FATE_P1", "op": "RANDOM_OUTCOME",
          "outcomes": [ { "effectId": "EFF_A", "weight": 1 }, { "effectId": "EFF_B", "weight": 2 } ] }
        """).ShouldBeEmpty(
            "the control: the same shape with a well-formed table and no trigger or target IS valid, " +
            "so every rejection below is about the table and not about a missing key");

        Validate($$"""
        { "id": "BOSS_DICELORD_ROLL_OF_FATE_P1", "op": "RANDOM_OUTCOME", {{body}} }
        """).ShouldNotBeEmpty();
    }

    /// <summary>
    /// RANDOM_OUTCOME carries no value or valueScale — its number is the winning row's index, which
    /// nothing authors — so the branch omits both keys and additionalProperties:false rejects them
    /// at validation rather than mid-battle.
    /// </summary>
    [Theory]
    [InlineData("\"value\": 3")]
    [InlineData("\"valueScale\": { \"fn\": \"ENEMY_COUNT\", \"per\": 1 }")]
    public void A_RANDOM_OUTCOME_carrying_a_magnitude_is_rejected_by_its_own_branch(string extraKey)
    {
        const string table = """
            "outcomes": [ { "effectId": "EFF_A", "weight": 1 }, { "effectId": "EFF_B", "weight": 2 } ]
            """;

        Validate($$"""
        { "id": "BOSS_DICELORD_ROLL_OF_FATE_P1", "op": "RANDOM_OUTCOME", {{table}} }
        """).ShouldBeEmpty("the control: the same effect without the magnitude is valid");

        Validate($$"""
        { "id": "BOSS_DICELORD_ROLL_OF_FATE_P1", "op": "RANDOM_OUTCOME", {{table}}, {{extraKey}} }
        """).ShouldNotBeEmpty($"{extraKey} is the only edit, and the branch admits neither");
    }

    /// <summary>
    /// An op-specific key on the wrong op. A single permissive object over the union of all keys
    /// would accept this; the seventeen-branch partition is what makes it a failure.
    /// </summary>
    [Theory]
    [InlineData("\"archetype\": \"SWARM\"")]
    [InlineData("\"maxAlive\": 3")]
    [InlineData("\"capKind\": \"HEAL_CEILING\"")]
    [InlineData("\"sourceCapPct\": 0.2")]
    [InlineData("\"statusId\": \"BURN\"")]
    [InlineData("\"newFace\": {\"kind\":\"Star\"}")]
    // Each of these keys belongs to a closed set of ops; admitting them everywhere would let
    // {"op":"STAT_ADD_PCT","charges":3} validate with "charges" meaning nothing.
    [InlineData("\"toStat\": \"ATK\"")]
    [InlineData("\"charges\": 3")]
    [InlineData("\"statusTag\": \"control\"")]
    [InlineData("\"outcomes\": [{\"effectId\":\"EFF_A\",\"weight\":1},{\"effectId\":\"EFF_B\",\"weight\":2}]")]
    public void An_op_specific_key_on_the_wrong_op_is_rejected(string extraKey)
    {
        Validate("""
        { "id": "PK_SHARP_EDGE_T1_ATK", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.12 }
        """).ShouldBeEmpty("the control: the same effect without the borrowed key is valid");

        Validate($$"""
        { "id": "PK_SHARP_EDGE_T1_ATK", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.12, {{extraKey}} }
        """).ShouldNotBeEmpty($"{extraKey} belongs to another op's branch, and it is the only edit");
    }

    /// <summary>A trigger parameter on a kind that does not take it.</summary>
    /// <remarks>
    /// Each row pairs a valid control (the kind with everything it needs) against the same kind
    /// plus a borrowed parameter — the parameter is the only edit. The ON_KILL/chance row matters
    /// specifically: ON_ATTACK takes chance and ON_KILL deliberately does not, so without this row
    /// the two kinds could be silently re-merged into one branch. Controls for PERIODIC, ON_LOW_HP,
    /// and ON_PHASE_ENTER carry their required parameter since those kinds are no longer valid bare.
    /// </remarks>
    [Theory]
    [InlineData("ALWAYS", "", "\"chance\":0.5")]
    [InlineData("ON_HIT", "", "\"cooldown\":3.0")]
    [InlineData("ON_KILL", "", "\"interval\":1.0")]
    [InlineData("ON_KILL", "", "\"chance\":0.5")]
    [InlineData("PERIODIC", "\"interval\":8.0", "\"everyNth\":3")]
    [InlineData("ON_DEATH", "", "\"once\":true")]
    [InlineData("ON_LOW_HP", "\"threshold\":0.3", "\"cooldown\":3.0")]
    [InlineData("ON_PHASE_ENTER", "\"phase\":2", "\"everyNth\":3")]
    public void A_trigger_parameter_on_a_kind_that_does_not_take_it_is_rejected(
        string kind, string constitutive, string parameter)
    {
        var needed = constitutive.Length > 0 ? "," + constitutive : string.Empty;

        Validate($$"""
        { "id": "PK_X", "op": "EXTRA_ATTACK", "value": 1, "trigger": {"kind":"{{kind}}"{{needed}}} }
        """).ShouldBeEmpty($"the control: {kind} with only what 18 §3 requires is valid");

        Validate($$"""
        { "id": "PK_X", "op": "EXTRA_ATTACK", "value": 1, "trigger": {"kind":"{{kind}}"{{needed}},{{parameter}}} }
        """).ShouldNotBeEmpty($"18 §3 does not give {parameter} to {kind}");
    }

    /// <summary>The three constitutive trigger parameters are required by the schema, so a trigger stating no rule fails the content build rather than a battle.</summary>
    /// <remarks>
    /// PERIODIC with no interval has no period, ON_LOW_HP with no threshold names no crossing, and
    /// ON_PHASE_ENTER with no phase cannot say which entry it means — the schema refuses rather than
    /// defaulting these at read time.
    /// </remarks>
    [Theory]
    [InlineData("PERIODIC", "\"interval\":8.0")]
    [InlineData("ON_LOW_HP", "\"threshold\":0.3")]
    [InlineData("ON_PHASE_ENTER", "\"phase\":2")]
    public void A_constitutive_trigger_parameter_is_required(string kind, string constitutive)
    {
        Validate($$"""
        { "id": "PK_X", "op": "EXTRA_ATTACK", "value": 1, "trigger": {"kind":"{{kind}}",{{constitutive}}} }
        """).ShouldBeEmpty($"the control: {kind} with its constitutive parameter is valid");

        Validate($$"""
        { "id": "PK_X", "op": "EXTRA_ATTACK", "value": 1, "trigger": {"kind":"{{kind}}"} }
        """).ShouldNotBeEmpty($"18 §3.1 — {kind} states no rule without {constitutive}");
    }

    /// <summary>ON_ATTACK takes chance, alone and alongside everyNth.</summary>
    [Theory]
    [InlineData("{\"kind\":\"ON_ATTACK\",\"chance\":0.25}")]
    [InlineData("{\"kind\":\"ON_ATTACK\",\"everyNth\":5,\"chance\":0.25}")]
    [InlineData("{\"kind\":\"ON_ATTACK\",\"everyNth\":5}")]
    public void ON_ATTACK_takes_a_chance_as_well_as_an_everyNth(string trigger)
    {
        Validate($$"""
        { "id": "PK_WILD_SWING", "op": "EXTRA_ATTACK", "value": 1, "trigger": {{trigger}},
          "target": "CURRENT_TARGET" }
        """).ShouldBeEmpty();
    }

    /// <summary>FORCE_CRIT_NEXT carries no magnitude at all — the schema enforces this.</summary>
    /// <remarks>
    /// How hard a forced crit hits is the actor's own CDMG; <c>CombatFlowOps</c> refuses
    /// <c>{"charges": 2, "value": 3}</c> because it reads as "three attacks" to whoever wrote it.
    /// The merged ATTACK_MULT_NEXT/FORCE_CRIT_NEXT branch used to admit that shape while its own
    /// description denied it, so authored content would have passed CI and thrown mid-battle.
    /// </remarks>
    [Theory]
    [InlineData("\"value\": 3")]
    [InlineData("\"valueScale\": {\"fn\":\"GOLD_HELD\",\"per\":100}")]
    public void FORCE_CRIT_NEXT_admits_no_magnitude_at_all(string extraKey)
    {
        Validate("""
        { "id": "PK_SURE_STRIKE", "op": "FORCE_CRIT_NEXT", "charges": 2 }
        """).ShouldBeEmpty("the control: charges alone is valid");

        Validate($$"""
        { "id": "PK_SURE_STRIKE", "op": "FORCE_CRIT_NEXT", "charges": 2, {{extraKey}} }
        """).ShouldNotBeEmpty($"{extraKey} is the only edit, and 18 §2.4 gives the op no magnitude");

        Validate("""
        { "id": "PK_OPENER_T1", "op": "ATTACK_MULT_NEXT", "charges": 1, "value": 3 }
        """).ShouldBeEmpty("its sibling DOES take a multiplier — which is why they are two branches");
    }

    [Fact]
    public void An_effect_with_no_id_is_rejected()
    {
        var issues = Validate("""
        { "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.12, "trigger": {"kind":"ALWAYS"} }
        """);

        issues.ShouldNotBeEmpty(
            "18 §8's effect-id order is over a field that must therefore exist on every effect");
    }

    [Theory]
    [InlineData("\"pk_lower\"")]
    [InlineData("\"1_LEADING_DIGIT\"")]
    [InlineData("\"WITH SPACE\"")]
    [InlineData("\"\"")]
    public void An_id_that_breaks_the_shape_convention_is_rejected(string id)
    {
        Validate("""
        { "id": "PK_FINE", "op": "EXTRA_ATTACK", "value": 1 }
        """).ShouldBeEmpty("the control: the same effect with a conventional id is valid");

        Validate($$"""
        { "id": {{id}}, "op": "EXTRA_ATTACK", "value": 1 }
        """).ShouldNotBeEmpty($"{id} is the only edit");
    }

    /// <summary><c>per</c> is the divisor and <c>cap</c> a maximum, so the schema refuses a non-positive <c>per</c> and a negative <c>cap</c>, matching <see cref="ValueScale"/>.</summary>
    [Theory]
    [InlineData("\"fn\": \"GOLD_HELD\", \"per\": 0, \"cap\": 1")]
    [InlineData("\"fn\": \"GOLD_HELD\", \"per\": -100, \"cap\": 1")]
    [InlineData("\"fn\": \"MOON_PHASE\", \"per\": 100, \"cap\": 1")]
    [InlineData("\"fn\": \"GOLD_HELD\", \"per\": 100, \"cap\": -1")]
    [InlineData("\"fn\": \"GOLD_HELD\", \"cap\": 1")]

    // These argument keys are closed the same way: the enum member has to exist, and an
    // unrecognised key is a validation failure rather than a field that silently means nothing.
    [InlineData("\"fn\": \"STATUS_STACKS\", \"per\": 1, \"cap\": 1, \"statusId\": \"NOT_A_STATUS\"")]
    [InlineData("\"fn\": \"STATUS_STACKS\", \"per\": 1, \"cap\": 1, \"arg\": \"SUNDER\"")]
    public void A_malformed_value_scale_is_rejected(string brokenScale)
    {
        Validate("""
        { "id": "PK_HOARD", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.01,
          "valueScale": { "fn": "GOLD_HELD", "per": 100, "cap": 1 } }
        """).ShouldBeEmpty("the control: 18 §1.1's own PK_HOARD shape, capped");

        Validate($$"""
        { "id": "PK_HOARD", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.01,
          "valueScale": { {{brokenScale}} } }
        """).ShouldNotBeEmpty($"one token differs from the control: {brokenScale}");
    }

    /// <summary>A comparison requires fn, op, and value all three — without an operand, it would gate on whatever the evaluator decided an absent value meant.</summary>
    [Fact]
    public void A_comparison_with_no_value_to_compare_against_is_rejected()
    {
        Validate("""
        { "id": "PK_X", "op": "EXTRA_ATTACK", "value": 1,
          "condition": { "fn": "SELF_HP_PCT", "op": "lt", "value": 0.3 } }
        """).ShouldBeEmpty("the control: the same condition with its operand is valid");

        Validate("""
        { "id": "PK_X", "op": "EXTRA_ATTACK", "value": 1,
          "condition": { "fn": "SELF_HP_PCT", "op": "lt" } }
        """).ShouldNotBeEmpty("the operand is the only edit");
    }

    /// <summary>
    /// The single-token enum the schema and the C# both restate. A small set, but a divergence in it
    /// is a token nobody can author against a record that still declares it.
    /// </summary>
    /// <remarks>
    /// ⚠️ It used to check <c>DieFaceScope</c> alongside it, off the <c>MODIFY_DIE_FACE</c> branch of
    /// the <c>oneOf</c>. Both are gone with the die's special faces.
    /// </remarks>
    [Fact]
    public void The_single_token_enums_agree_between_the_schema_and_the_C_sharp()
    {
        // Members sorts ordinally so a schema enum in document order and a C# enum in wire order
        // still compare correctly.
        Members("capKind").ShouldBe(
            Enum.GetNames<StatCapKind>().OrderBy(n => n, StringComparer.Ordinal),
            Case.Sensitive,
            "$defs/capKind and StatCapKind are two statements of one vocabulary");
    }

    /// <summary>Guards effect.schema.json under <c>ContentLoader.VocabularySchemas</c> — the one exemption in the repo with no mechanical expiry of its own.</summary>
    /// <remarks>
    /// <c>effect.schema.json</c> governs no file and never will, so the stale-exemption check can't
    /// catch it another way. <see cref="JsonSchemaValidator"/> only resolves same-document pointers,
    /// so other schemas cannot <c>$ref</c> this one — the risk is a schema pasting the op enum or
    /// trigger partition inline instead, which then drifts. This test fails on the commit that does
    /// that.
    /// </remarks>
    [Fact]
    public void No_other_schema_restates_the_effect_vocabulary()
    {
        var offenders = RepoData.Documents
            .Where(d => d.Key.StartsWith("schema/", StringComparison.Ordinal))
            .Where(d => !d.Key.Equals(SchemaPath, StringComparison.Ordinal))
            .Where(d => Enum.GetNames<EffectOp>().Count(op => d.Value.Contains($"\"{op}\"", StringComparison.Ordinal)) >= 3)
            .Select(d => d.Key)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty(
            "a second schema now enumerates the 18 §2 op vocabulary. Two copies of a 44-member " +
            "closed set drift, and 18 §10's 'add the op to the JSON schema' stops naming one file. " +
            "Extract it or record the duplication deliberately in ContentLoader.VocabularySchemas.");

        // The filter above is a substring scan over the schema set; if that set were empty or
        // unreadable it would report success over nothing.
        RepoData.Documents.Count(d => d.Key.StartsWith("schema/", StringComparison.Ordinal))
            .ShouldBeGreaterThanOrEqualTo(20, "19 schemas from M0-10 plus effect.schema.json");
    }

    // ---------------------------------------------------------------- helpers

    private static IReadOnlyList<ContentIssue> Validate(string effectJson)
    {
        JsonContentReader.TryRead("effect.json", Encoding.UTF8.GetBytes(effectJson), out var instance, out var issues)
            .ShouldBeTrue($"the fixture must be valid JSON: {string.Join("; ", issues.Select(i => i.Message))}");

        return JsonSchemaValidator.Validate(instance!, Schema, "effect.json");
    }

    private static ContentValue ReadSchema()
    {
        var text = RepoData.Documents[SchemaPath];

        JsonContentReader.TryRead(SchemaPath, Encoding.UTF8.GetBytes(text), out var schema, out var issues)
            .ShouldBeTrue($"schema/effect.schema.json must parse: {string.Join("; ", issues.Select(i => i.Message))}");

        return schema!;
    }

    /// <summary>The ordinal-sorted members of an <c>enum</c> keyword under <c>$defs</c>.</summary>
    private static IReadOnlyList<string> Members(params string[] path)
    {
        Schema.TryGetMember("$defs", out var defs).ShouldBeTrue();

        var node = defs!;
        foreach (var segment in path)
        {
            node.TryGetMember(segment, out var next).ShouldBeTrue($"$defs/{string.Join('/', path)} is missing at '{segment}'");
            node = next!;
        }

        node.TryGetMember("enum", out var members).ShouldBeTrue($"$defs/{string.Join('/', path)} declares no enum");

        return members!.Items.Select(i => i.AsText()).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Every op token named by the root <c>oneOf</c>'s branches, with duplicates kept.</summary>
    private static IEnumerable<string> BranchOps()
    {
        Schema.TryGetMember("oneOf", out var branches).ShouldBeTrue();
        // The count is asserted, not merely implied by the partition below, so a branch appearing
        // or vanishing is a decision.
        branches!.Items.Count.ShouldBe(16, "18 §2's 42 ops partition into sixteen key shapes");

        foreach (var branch in branches.Items)
        {
            branch.TryGetMember("properties", out var properties).ShouldBeTrue();
            properties!.TryGetMember("op", out var op).ShouldBeTrue();

            foreach (var token in Tokens(op!))
            {
                yield return token;
            }
        }
    }

    /// <summary>Every trigger kind named by <c>$defs/trigger</c>'s branches, with duplicates kept.</summary>
    private static IEnumerable<string> TriggerBranchKinds()
    {
        Schema.TryGetMember("$defs", out var defs).ShouldBeTrue();
        defs!.TryGetMember("trigger", out var trigger).ShouldBeTrue();
        trigger!.TryGetMember("oneOf", out var branches).ShouldBeTrue();

        branches!.Items.Count.ShouldBe(
            15,
            "fourteen parameter shapes plus the null branch 18 §9.1 and §7.7 need. It was thirteen " +
            "shapes until M2-04's R11 gave ON_ATTACK a chance and ON_KILL none, which split the " +
            "branch the two kinds used to share");

        foreach (var branch in branches.Items)
        {
            if (!branch.TryGetMember("properties", out var properties))
            {
                continue; // the "no trigger" branch, which names no kind
            }

            properties!.TryGetMember("kind", out var kind).ShouldBeTrue();

            foreach (var token in Tokens(kind!))
            {
                yield return token;
            }
        }
    }

    /// <summary>The token(s) a <c>const</c> or <c>enum</c> schema position names.</summary>
    private static IEnumerable<string> Tokens(ContentValue schema)
    {
        if (schema.TryGetMember("const", out var single))
        {
            return [single!.AsText()];
        }

        schema.TryGetMember("enum", out var many).ShouldBeTrue("a discriminator must be a const or an enum");

        return many!.Items.Select(i => i.AsText()).ToArray();
    }
}
