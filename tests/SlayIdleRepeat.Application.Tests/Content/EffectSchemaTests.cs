using System.Text;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// 🔒 <c>game-data/schema/effect.schema.json</c> against `18` — the vocabulary schema every later
/// M2 task keys on.
/// </summary>
/// <remarks>
/// <para>
/// This schema governs no data file (an effect is always embedded in the thing that owns it), so
/// the loader never validates an instance against it. That is exactly why these cases exist: a
/// schema nothing exercises is a schema nobody has checked. Every worked example in `18` is
/// validated here, and each of the three failure classes `18` §10 relies on — unknown op, unknown
/// key, unknown enum member — has a case that proves the rejection.
/// </para>
/// <para>
/// The parity cases are the other half. The C# enums and the schema enums are two statements of one
/// vocabulary, and nothing but a test can make them agree.
/// </para>
/// </remarks>
public sealed class EffectSchemaTests
{
    private const string SchemaPath = "schema/effect.schema.json";

    private static readonly Lazy<ContentValue> LazySchema = new(ReadSchema);

    private static ContentValue Schema => LazySchema.Value;

    // ---------------------------------------------------------------- the schema itself

    /// <summary>
    /// 🔒 M0-09's rule: an unimplemented keyword is a build failure, never a silent pass. The sweep
    /// is eager and covers branches no instance reaches, which is the whole point for a schema with
    /// thirteen op branches and fourteen trigger branches.
    /// </summary>
    [Fact]
    public void The_schema_uses_only_keywords_the_validator_implements()
    {
        JsonSchemaValidator.CheckSchemaKeywords(Schema, SchemaPath).ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 The 43 ops are partitioned across the root's <c>oneOf</c> branches: every op in exactly
    /// one branch, and no branch naming an op that is not declared.
    /// </summary>
    /// <remarks>
    /// A op in two branches would make every instance of it match two <c>oneOf</c> branches and
    /// fail validation; an op in none would make it unauthorable while <c>EffectOp</c> still
    /// declared it. Both are silent until a content file happens to use that op, which could be
    /// three milestones from now.
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

        counts.Count.ShouldBe(43, "18 §11 — and S3's floor under the loops above");
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

    /// <summary>
    /// 🔒 The <c>ALL_COMBAT</c> ruling, stated in the schema: it is admitted by the three
    /// aggregating stat ops and by nothing else, and it is not a member of <c>stat</c>.
    /// </summary>
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
            "18 §7.4 — PK_UNBREAKABLE, ON_LETHAL once, no target",
            """
            { "id": "PK_UNBREAKABLE_T1_SURVIVE", "op": "SURVIVE_LETHAL", "value": 1,
              "trigger": {"kind":"ON_LETHAL","once":true} }
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
            "18 §7.9 — TILE_DICE_FORGE, a run/board op on a run trigger",
            """
            { "id": "TILE_DICE_FORGE_FACE", "op": "MODIFY_DIE_FACE", "faceIndex": "PLAYER_CHOICE",
              "newFace": {"kind":"Pip","value":4}, "duration": {"scope":"RUN"},
              "trigger": {"kind":"ON_TILE_RESOLVED","tileType":"TILE_DICE_FORGE"} }
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
            "18 §9.2 — PET_DICEBEAST's ON_BATTLE_END die-face grant",
            """
            { "id": "PET_DICEBEAST_ACTIVE", "op": "MODIFY_DIE_FACE", "scope": "NEXT_3_ROLLS",
              "newFace": {"kind":"Star"}, "trigger": {"kind":"ON_BATTLE_END","onlyIfWon":true} }
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
        {
            "18 §2.5 — the combat-context exception: MODIFY_DIE_FACE from a PERIODIC combat trigger",
            """
            { "id": "BOSS_DICELORD_SCRAMBLE", "op": "MODIFY_DIE_FACE", "newFace": {"kind":"Void"},
              "trigger": {"kind":"PERIODIC","interval":15.0} }
            """
        },
    };

    // ---------------------------------------------------------------- 18 §10's three failure classes

    /// <summary>
    /// 🔒 `18` §10 step 1: <em>"write the design as JSON using a <b>new op name</b> and let the
    /// schema validation fail."</em> That instruction is only true if it actually fails.
    /// </summary>
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

    /// <summary>
    /// 🔒 The <c>ALL_COMBAT</c> ruling's teeth: it is rejected where one concrete stat is required.
    /// `18` §9.1 is the proof it has to be — the ruling is TWO effects precisely because the group
    /// selector cannot set <c>MAX_HP</c>.
    /// </summary>
    /// <remarks>
    /// Steering S2 — stated as a <b>single edit</b> rather than as a message match. A oneOf failure
    /// reports the branch that failed by the smallest margin, and with 43 ops in thirteen branches
    /// that is often a branch whose only complaint is the op discriminator, so matching on the word
    /// <c>ALL_COMBAT</c> in the message would be asserting which branch happened to be closest.
    /// Two documents differing in exactly one token, one accepted and one rejected, pins the rule
    /// that fired without depending on the reporting.
    /// </remarks>
    [Theory]
    [InlineData("STAT_SET")]
    [InlineData("STAT_CONVERT")]
    [InlineData("STAT_CAP_OVERRIDE")]
    public void ALL_COMBAT_is_rejected_where_one_concrete_stat_is_required(string op)
    {
        Validate($$"""
        { "id": "CP_GLASS_HEART_BAD", "op": "{{op}}", "stat": "MAX_HP", "value": 1.0 }
        """).ShouldBeEmpty($"the control: {op} over one concrete stat is valid");

        Validate($$"""
        { "id": "CP_GLASS_HEART_BAD", "op": "{{op}}", "stat": "ALL_COMBAT", "value": 1.0 }
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

    /// <summary>
    /// An op-specific key on the wrong op. A single permissive object over the union of all keys
    /// would accept this; the thirteen-branch partition is what makes it a failure.
    /// </summary>
    [Theory]
    [InlineData("\"archetype\": \"SWARM\"")]
    [InlineData("\"maxAlive\": 3")]
    [InlineData("\"capKind\": \"HEAL_CEILING\"")]
    [InlineData("\"sourceCapPct\": 0.2")]
    [InlineData("\"statusId\": \"BURN\"")]
    [InlineData("\"newFace\": {\"kind\":\"Star\"}")]
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
    /// Each row carries its own control: the same kind with the parameter removed. The parameter is
    /// the only edit between the two, so what fired is the partition of `18` §3's 23 kinds into
    /// thirteen parameter shapes and not, say, a typo in the kind.
    /// </remarks>
    [Theory]
    [InlineData("ALWAYS", "\"chance\":0.5")]
    [InlineData("ON_HIT", "\"cooldown\":3.0")]
    [InlineData("ON_KILL", "\"interval\":1.0")]
    [InlineData("PERIODIC", "\"everyNth\":3")]
    [InlineData("ON_DEATH", "\"once\":true")]
    public void A_trigger_parameter_on_a_kind_that_does_not_take_it_is_rejected(string kind, string parameter)
    {
        Validate($$"""
        { "id": "PK_X", "op": "EXTRA_ATTACK", "value": 1, "trigger": {"kind":"{{kind}}"} }
        """).ShouldBeEmpty($"the control: {kind} with no parameters is valid");

        Validate($$"""
        { "id": "PK_X", "op": "EXTRA_ATTACK", "value": 1, "trigger": {"kind":"{{kind}}",{{parameter}}} }
        """).ShouldNotBeEmpty($"18 §3 does not give {parameter} to {kind}");
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
        Validate($$"""
        { "id": {{id}}, "op": "EXTRA_ATTACK", "value": 1 }
        """).ShouldNotBeEmpty();
    }

    /// <summary>`18` §1.1 — <c>per</c> is the divisor, so the schema refuses zero as the record does.</summary>
    [Fact]
    public void A_value_scale_with_a_per_of_zero_is_rejected()
    {
        Validate("""
        { "id": "PK_BAD_SCALE", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.01,
          "valueScale": { "fn": "GOLD_HELD", "per": 0 } }
        """).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_value_scale_over_something_that_is_not_a_condition_function_is_rejected()
    {
        Validate("""
        { "id": "PK_BAD_SCALE", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.01,
          "valueScale": { "fn": "MOON_PHASE", "per": 1 } }
        """).ShouldNotBeEmpty();
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
        branches!.Items.Count.ShouldBe(13, "18 §2's 43 ops partition into thirteen key shapes");

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
            14,
            "thirteen parameter shapes plus the null branch 18 §9.1 and §7.7 need");

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
