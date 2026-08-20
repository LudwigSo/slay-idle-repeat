using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// The op-to-key partition, checkable without a battle — the in-code counterpart of
/// <c>effect.schema.json</c>'s branches, which nothing built in code passes through.
/// </summary>
public sealed class EffectOpValidationTests
{
    /// <summary>Every op reaches a deliberate arm of the validator, rather than its <c>default</c>.</summary>
    /// <remarks>
    /// Does not claim more than that: a new op added as <c>case NEW_OP: break;</c> would still pass
    /// here. Only the op that reaches no arm at all is caught.
    /// </remarks>
    [Fact]
    public void No_op_falls_through_to_the_validators_default_arm()
    {
        EffectOps.All.Count.ShouldBe(41, "18 §11 — the floor under the loop. ⚠️ 41, not the document's 42: REVEAL_TILES went with the tile preview (16 D42).");

        var uncovered = new List<string>();

        foreach (var op in EffectOps.All)
        {
            var bare = new EffectDefinition { Id = "X", Op = op };

            // A bare effect either validates (the ops that need only the spine) or reports at least
            // one problem. What must never happen is the default arm firing.
            if (EffectOpValidation.Problems(bare).Any(p => p.Contains("not one of 18 §2's 44", StringComparison.Ordinal)))
            {
                uncovered.Add(op.ToString());
            }
        }

        uncovered.ShouldBeEmpty();
    }

    /// <summary>Each op-specific key is refused on an op that does not own it.</summary>
    [Theory]
    [InlineData(EffectOp.STAT_ADD_PCT, "toStat")]
    [InlineData(EffectOp.DAMAGE, "charges")]
    [InlineData(EffectOp.APPLY_STATUS, "statusTag")]
    [InlineData(EffectOp.HEAL, "capKind")]
    [InlineData(EffectOp.DAMAGE_TRUE, "sourceCapPct")]
    public void A_key_that_belongs_to_another_op_is_a_problem(EffectOp op, string key)
    {
        var control = Minimal(op);
        EffectOpValidation.Problems(control).ShouldBeEmpty($"the control: a bare {op} is well-formed");

        var borrowed = key switch
        {
            "toStat" => control with { ToStat = StatId.ATK },
            "charges" => control with { Charges = 2 },
            "statusTag" => control with { StatusTag = new StatusTag("control") },
            "capKind" => control with { CapKind = StatCapKind.STAT_MAX },
            _ => control with { SourceCapPct = 0.2 },
        };

        EffectOpValidation.Problems(borrowed)
                          .ShouldContain(p => p.Contains($"carries '{key}'", StringComparison.Ordinal));
    }

    /// <summary><c>ALL_COMBAT</c> is admitted by the three aggregating stat ops and by nothing else.</summary>
    [Theory]
    [InlineData(EffectOp.STAT_ADD_FLAT, true)]
    [InlineData(EffectOp.STAT_ADD_PCT, true)]
    [InlineData(EffectOp.STAT_MULT, true)]
    [InlineData(EffectOp.STAT_SET, false)]
    [InlineData(EffectOp.STAT_CONVERT, false)]
    [InlineData(EffectOp.STAT_CAP_OVERRIDE, false)]
    [InlineData(EffectOp.STAT_COPY, false)]
    public void ALL_COMBAT_is_admitted_only_by_the_three_aggregating_stat_ops(EffectOp op, bool admitted)
    {
        var group = Minimal(op) with { Stat = StatSelector.AllCombat };

        EffectOpValidation.Problems(group)
                          .Any(p => p.Contains("names the ALL_COMBAT selector", StringComparison.Ordinal))
                          .ShouldBe(!admitted);
    }

    [Fact]
    public void HIGHEST_PCT_BONUS_is_admitted_only_by_STAT_COPY()
    {
        EffectOpValidation.Problems(Minimal(EffectOp.STAT_COPY) with { Stat = StatSelector.HighestPctBonus })
                          .ShouldBeEmpty();

        EffectOpValidation.Problems(Minimal(EffectOp.STAT_ADD_PCT) with { Stat = StatSelector.HighestPctBonus })
                          .ShouldContain(
                              p => p.Contains("names the HIGHEST_PCT_BONUS selector", StringComparison.Ordinal),
                              "S2 — the selector rule fired, not one of the other six the op could break");
    }

    /// <summary>The two op-specific keys the in-code validator and the schema must agree on.</summary>
    /// <remarks>
    /// The schema admits <c>valueMode</c> on nine ops and <c>statusId</c> on four; if the code path
    /// polices neither, the two enforcement paths can disagree about the same effect.
    /// </remarks>
    [Theory]
    // REVIVE has an internal fallback value mode but still takes no authored valueMode key.
    [InlineData(EffectOp.REVIVE, "valueMode")]
    [InlineData(EffectOp.EXTRA_ATTACK, "valueMode")]
    [InlineData(EffectOp.SUMMON, "valueMode")]
    [InlineData(EffectOp.DAMAGE, "statusId")]
    [InlineData(EffectOp.CLEAR_SUMMONS, "statusId")]
    public void valueMode_and_statusId_are_refused_on_the_ops_that_do_not_take_them(
        EffectOp op, string key)
    {
        var control = Minimal(op);
        EffectOpValidation.Problems(control).ShouldBeEmpty($"the control: a bare {op} is well-formed");

        var borrowed = key == "valueMode"
            ? control with { ValueMode = ValueMode.FLAT }
            : control with { StatusId = "BURN" };

        EffectOpValidation.Problems(borrowed)
                          .ShouldContain(p => p.Contains($"carries '{key}'", StringComparison.Ordinal));
    }

    /// <summary>A <c>valueMode</c> the op does not admit is a problem at validation, not a surprise at fire time.</summary>
    [Theory]
    [InlineData(EffectOp.HEAL_LEECH, ValueMode.ATK_MULT)]
    [InlineData(EffectOp.DAMAGE, ValueMode.FLAT)]
    [InlineData(EffectOp.REFLECT, ValueMode.SELF_MAXHP_PCT)]
    public void A_value_mode_the_op_does_not_admit_is_a_problem(EffectOp op, ValueMode mode)
    {
        EffectOpValidation.Problems(Minimal(op) with { ValueMode = mode })
                          .ShouldContain(p => p.Contains($"carries valueMode {mode}", StringComparison.Ordinal));
    }

    /// <summary>Each op-specific key is required where its op needs it.</summary>
    [Fact]
    public void The_18_10_keys_are_required_on_the_ops_that_carry_them()
    {
        EffectOpValidation.Problems(Minimal(EffectOp.STAT_CONVERT) with { ToStat = null })
                          .ShouldContain(p => p.Contains("names no toStat", StringComparison.Ordinal));

        EffectOpValidation.Problems(Minimal(EffectOp.STAT_CAP_OVERRIDE) with { CapKind = null })
                          .ShouldContain(p => p.Contains("names no capKind", StringComparison.Ordinal));

        EffectOpValidation.Problems(Minimal(EffectOp.ATTACK_MULT_NEXT) with { Charges = null })
                          .ShouldContain(p => p.Contains("names no charges", StringComparison.Ordinal));

        EffectOpValidation.Problems(Minimal(EffectOp.FORCE_CRIT_NEXT) with { Charges = null })
                          .ShouldContain(p => p.Contains("names no charges", StringComparison.Ordinal));

        EffectOpValidation.Problems(Minimal(EffectOp.REMOVE_STATUS) with { StatusId = null })
                          .ShouldContain(p => p.Contains("names neither", StringComparison.Ordinal));
    }

    /// <summary>A set of representative worked examples all validate, as authored.</summary>
    [Fact]
    public void The_worked_examples_of_18_7_are_well_formed()
    {
        var examples = new EffectDefinition[]
        {
            new()
            {
                Id = "PK_SHARP_EDGE_T1_ATK", Op = EffectOp.STAT_ADD_PCT,
                Stat = StatSelector.Of(StatId.ATK), Value = 0.12, Target = EffectTarget.SELF,
            },

            // With the valueMode SURVIVE_LETHAL's FLAT fallback added.
            new()
            {
                Id = "PK_UNBREAKABLE_T1_SURVIVE", Op = EffectOp.SURVIVE_LETHAL,
                Value = 1, ValueMode = ValueMode.FLAT,
            },

            new()
            {
                Id = "CP_BLOOD_PRICE_COST", Op = EffectOp.DAMAGE_MAXHP_PCT, Value = 0.03,
                Target = EffectTarget.SELF, Tags = ["drawback"],
            },

            new()
            {
                Id = "TAL_AVATAR_OF_WAR_CAP", Op = EffectOp.STAT_CAP_OVERRIDE,
                Stat = StatSelector.Of(StatId.MAX_HP), Value = 0.80, CapKind = StatCapKind.HEAL_CEILING,
            },

            new()
            {
                Id = "BOSS_THORNMAW_P3_SWARM", Op = EffectOp.SUMMON, Archetype = "SWARM",
                Value = 2, MaxAlive = 3,
            },

            new()
            {
                Id = "PK_CLEAVE_T1", Op = EffectOp.DAMAGE, Value = 0.40, Target = EffectTarget.OTHER_ENEMIES,
            },
        };

        // The floor under the loop: an empty fixture list would report success over nothing.
        examples.Length.ShouldBe(6);

        examples.SelectMany(e => EffectOpValidation.Problems(e).Select(p => $"{e.Id}: {p}"))
                .ShouldBeEmpty();
    }

    /// <summary>A minimal well-formed effect for one op.</summary>
    private static EffectDefinition Minimal(EffectOp op)
    {
        var effect = new EffectDefinition { Id = "X", Op = op, Value = 1.0 };

        return op switch
        {
            EffectOp.STAT_ADD_FLAT or EffectOp.STAT_ADD_PCT or EffectOp.STAT_MULT or EffectOp.STAT_SET =>
                effect with { Stat = StatSelector.Of(StatId.ATK) },
            EffectOp.STAT_CONVERT => effect with { Stat = StatSelector.Of(StatId.DEF), ToStat = StatId.ATK },
            EffectOp.STAT_CAP_OVERRIDE =>
                effect with { Stat = StatSelector.Of(StatId.CRIT), CapKind = StatCapKind.STAT_MAX },
            EffectOp.STAT_COPY => effect with { Stat = StatSelector.Of(StatId.CRIT) },
            EffectOp.APPLY_STATUS or EffectOp.EXTEND_STATUS or EffectOp.IMMUNE_STATUS =>
                effect with { StatusId = "BURN" },
            EffectOp.REMOVE_STATUS => effect with { StatusId = "BURN" },
            EffectOp.ATTACK_MULT_NEXT => effect with { Charges = 1 },
            EffectOp.FORCE_CRIT_NEXT => effect with { Value = null, Charges = 1 },
            EffectOp.SUMMON => effect with { Archetype = "SWARM" },

            // RANDOM_OUTCOME carries no value at all; two rows because one outcome is not a choice.
            EffectOp.RANDOM_OUTCOME => effect with
            {
                Value = null,
                Outcomes = new[] { new RandomOutcomeEntry("EFF_A", 1.0), new RandomOutcomeEntry("EFF_B", 1.0) },
            },

            _ => effect,
        };
    }
}
