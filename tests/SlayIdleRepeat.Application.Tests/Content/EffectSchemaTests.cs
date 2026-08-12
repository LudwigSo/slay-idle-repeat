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
            // 🔴 The M2-06 extension, by 18 §10's route. 18 §1.1 offers fn "any condition function
            // from §4" and names STATUS_STACKS and DIE_FACE_COUNT in its own list, but §4 types
            // those "by status id" and "by face kind" and §1.1's table declared no field to carry
            // one — so both were offered for something the vocabulary could not express. The three
            // keys are conditionTerm's own, pointed at the same $defs. Erratum on §1.1.
            "18 §1.1 — a valueScale over STATUS_STACKS, which needs an argument",
            """
            { "id": "PK_SUNDERER", "op": "STAT_ADD_PCT", "stat": "DMG_PCT", "value": 0.05,
              "trigger": {"kind":"ALWAYS"}, "target": "CURRENT_TARGET",
              "valueScale": { "fn": "STATUS_STACKS", "per": 1, "cap": 5, "statusId": "SUNDER" } }
            """
        },
        {
            "18 §1.1 — a valueScale over DIE_FACE_COUNT, likewise",
            """
            { "id": "PK_STARGAZER", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.03,
              "trigger": {"kind":"ALWAYS"}, "target": "SELF",
              "valueScale": { "fn": "DIE_FACE_COUNT", "per": 1, "cap": null, "faceKind": "Star" } }
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

        // ---- the effects inside 18's MULTI-effect snippets, which the entries above only
        // ---- represent by their first element. Several carry a shape nothing else exercises.
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

        // ---- shapes the schema declares that no 18 example happens to write. Each was
        // ---- unexercised until this block, so a typo in it validated nothing and broke nobody.
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
            "18 §4 — DIE_FACE_COUNT by face kind, and PERK_COUNT by category",
            """
            { "id": "PK_STARGAZER", "op": "STAT_ADD_PCT", "stat": "ATK", "value": 0.05,
              "trigger": {"kind":"ALWAYS"},
              "condition": { "all": [
                  { "fn": "DIE_FACE_COUNT", "op": "gte", "value": 2, "faceKind": "Star" },
                  { "fn": "PERK_COUNT", "op": "gte", "value": 3, "category": "OFFENSE" } ] } }
            """
        },
        {
            "18 §7.9 — the numbered faceIndex form (Weighted Faces), 1-based over 04 §1's six faces",
            """
            { "id": "TILE_WEIGHTED_FACES", "op": "MODIFY_DIE_FACE", "faceIndex": 6,
              "newFace": {"kind":"Star"}, "duration": {"scope":"RUN"},
              "trigger": {"kind":"ON_TILE_RESOLVED","tileType":"TILE_DICE_FORGE"} }
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
        Validate("""
        { "id": "PK_FINE", "op": "EXTRA_ATTACK", "value": 1 }
        """).ShouldBeEmpty("the control: the same effect with a conventional id is valid");

        Validate($$"""
        { "id": {{id}}, "op": "EXTRA_ATTACK", "value": 1 }
        """).ShouldNotBeEmpty($"{id} is the only edit");
    }

    /// <summary>
    /// `18` §1.1 — <c>per</c> is the divisor and <c>cap</c> a maximum, so the schema refuses a
    /// non-positive <c>per</c> and a negative <c>cap</c> exactly as <see cref="ValueScale"/> does.
    /// Each row differs from the control in one token.
    /// </summary>
    [Theory]
    [InlineData("\"fn\": \"GOLD_HELD\", \"per\": 0, \"cap\": 1")]
    [InlineData("\"fn\": \"GOLD_HELD\", \"per\": -100, \"cap\": 1")]
    [InlineData("\"fn\": \"MOON_PHASE\", \"per\": 100, \"cap\": 1")]
    [InlineData("\"fn\": \"GOLD_HELD\", \"per\": 100, \"cap\": -1")]
    [InlineData("\"fn\": \"GOLD_HELD\", \"cap\": 1")]

    // 🔴 The M2-06 argument keys are conditionTerm's own, and they are closed the same way: the
    // enum member has to exist, and a key nobody agreed on is still a validation failure rather
    // than a field that silently means nothing. Without these rows the extension would have
    // widened the schema with nothing pinning where the new surface stops.
    [InlineData("\"fn\": \"STATUS_STACKS\", \"per\": 1, \"cap\": 1, \"statusId\": \"NOT_A_STATUS\"")]
    [InlineData("\"fn\": \"DIE_FACE_COUNT\", \"per\": 1, \"cap\": 1, \"faceKind\": \"Sparkle\"")]
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

    /// <summary>
    /// `18` §4's comparison is <c>{"fn": …, "op": …, "value": …}</c> — all three. A comparison with
    /// no operand compares the function against nothing and would gate on whatever the evaluator
    /// decided an absent value meant.
    /// </summary>
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

    /// <summary>`04` §1 gives the die six faces; the schema and <see cref="DieFaceIndex"/> agree on the bound.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("7")]
    [InlineData("\"RANDOM\"")]
    public void A_face_index_outside_the_die_is_rejected(string faceIndex)
    {
        Validate("""
        { "id": "TILE_WEIGHTED_FACES", "op": "MODIFY_DIE_FACE", "faceIndex": 6,
          "newFace": {"kind":"Star"} }
        """).ShouldBeEmpty("the control: face 6 is on the die");

        Validate($$"""
        { "id": "TILE_WEIGHTED_FACES", "op": "MODIFY_DIE_FACE", "faceIndex": {{faceIndex}},
          "newFace": {"kind":"Star"} }
        """).ShouldNotBeEmpty($"faceIndex {faceIndex} is the only edit");
    }

    /// <summary>
    /// The bounds `04` §1 fixes are stated in three places — the schema's <c>faceIndex</c>, the
    /// schema's <c>dieFace.value</c> and <see cref="DieFaceIndex"/>'s constants. Nothing but this
    /// makes them agree.
    /// </summary>
    [Fact]
    public void The_die_face_bounds_agree_between_the_schema_and_the_record()
    {
        Bound("faceIndex", "oneOf", 1).ShouldBe((DieFaceIndex.MinFace, DieFaceIndex.MaxFace));
        Bound("dieFace", "properties", "value").ShouldBe((DieFaceIndex.MinFace, DieFaceIndex.MaxFace));
    }

    /// <summary>
    /// The two single-token enums the schema and the C# both restate. Small sets, but a divergence
    /// in one of them is a token nobody can author against a record that still declares it.
    /// </summary>
    [Fact]
    public void The_single_token_enums_agree_between_the_schema_and_the_C_sharp()
    {
        Members("capKind").ShouldBe(Enum.GetNames<StatCapKind>());

        Schema.TryGetMember("oneOf", out var branches).ShouldBeTrue();
        var dieFaceBranch = branches!.Items.Single(b =>
        {
            b.TryGetMember("properties", out var p);
            p!.TryGetMember("op", out var op);
            return op!.TryGetMember("const", out var c) && c!.AsText() == nameof(EffectOp.MODIFY_DIE_FACE);
        });

        dieFaceBranch.TryGetMember("properties", out var properties).ShouldBeTrue();
        properties!.TryGetMember("scope", out var scope).ShouldBeTrue();
        scope!.TryGetMember("enum", out var members).ShouldBeTrue();

        members!.Items.Select(i => i.AsText()).ShouldBe(Enum.GetNames<DieFaceScope>());
    }

    /// <summary>
    /// 🔒 The guard under <c>ContentLoader.VocabularySchemas</c> — the one exemption in the repo with
    /// no mechanical expiry of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>effect.schema.json</c> governs no file and never will, so the stale-exemption check that
    /// protects <c>SchemasAwaitingContent</c> cannot fire for it. What can go wrong instead is
    /// concrete and near: <see cref="JsonSchemaValidator"/> resolves same-document pointers only, so
    /// the perk, pet, mount, curse and boss schemas M2-07 and M3 author cannot <c>$ref</c> this
    /// file — the tempting alternative is to paste the 43-op enum, the thirteen-way trigger
    /// partition and the recursive condition tree into each of them, at which point five copies
    /// drift and `18` §10's "add the op to the JSON schema" becomes ambiguous about which.
    /// </para>
    /// <para>
    /// This fails on the commit that does it, which is what S4 asks of a declared exception. The fix
    /// at that point is a generator or a deliberate decision recorded here — not a quiet fifth copy.
    /// </para>
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
            "a second schema now enumerates the 18 §2 op vocabulary. Two copies of a 43-member " +
            "closed set drift, and 18 §10's 'add the op to the JSON schema' stops naming one file. " +
            "Extract it or record the duplication deliberately in ContentLoader.VocabularySchemas.");

        // S3 — the floor. The filter above is a substring scan over the schema set; if that set
        // were empty or unreadable it would report success over nothing.
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

    /// <summary>The <c>minimum</c>/<c>maximum</c> pair at a <c>$defs</c> path, for the bound checks.</summary>
    /// <remarks>
    /// A numeric path segment indexes into a <c>oneOf</c>, so <c>faceIndex/oneOf/1</c> reaches the
    /// integer branch of `18` §7.9's two forms.
    /// </remarks>
    private static (int Minimum, int Maximum) Bound(string definition, params object[] path)
    {
        Schema.TryGetMember("$defs", out var defs).ShouldBeTrue();
        defs!.TryGetMember(definition, out var node).ShouldBeTrue();

        foreach (var segment in path)
        {
            if (segment is int index)
            {
                node = node!.Items[index];
                continue;
            }

            node!.TryGetMember((string)segment, out var next).ShouldBeTrue($"$defs/{definition} has no '{segment}'");
            node = next!;
        }

        node!.TryGetMember("minimum", out var minimum).ShouldBeTrue();
        node.TryGetMember("maximum", out var maximum).ShouldBeTrue();

        return (minimum!.AsInt32(), maximum!.AsInt32());
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
