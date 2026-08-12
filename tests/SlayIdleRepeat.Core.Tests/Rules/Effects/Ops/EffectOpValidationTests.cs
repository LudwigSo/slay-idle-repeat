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
    /// <summary>
    /// 🔒 S3's floor: <b>every</b> one of the 43 ops reaches a deliberate arm of the validator,
    /// rather than its <c>default</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ What this does <b>not</b> claim, said plainly: a forty-fourth op added as
    /// <c>case NEW_OP: break;</c> would report no problems and pass here. Nothing short of a
    /// per-op expected-shape table could catch that, and such a table would be a third statement of
    /// the partition <c>effect.schema.json</c> and <see cref="EffectOpValidation"/> already make.
    /// What is caught is the op that reaches no arm at all.
    /// </remarks>
    [Fact]
    public void No_op_falls_through_to_the_validators_default_arm()
    {
        EffectOps.All.Count.ShouldBe(43, "18 §11 — the floor under the loop");

        var uncovered = new List<string>();

        foreach (var op in EffectOps.All)
        {
            var bare = new EffectDefinition { Id = "X", Op = op };

            // A bare effect either validates (the ops that need only the spine) or reports at least
            // one problem. What must never happen is the default arm reporting "not one of 18 §2's 43".
            if (EffectOpValidation.Problems(bare).Any(p => p.Contains("not one of 18 §2's 43", StringComparison.Ordinal)))
            {
                uncovered.Add(op.ToString());
            }
        }

        uncovered.ShouldBeEmpty();
    }

    /// <summary>Each of the five `18` §10 keys is refused on an op that does not own it.</summary>
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

    /// <summary>
    /// <c>ALL_COMBAT</c> is admitted by the three aggregating stat ops and by nothing else — `18`
    /// §9.1's ruling, which is why <c>CP_GLASS_HEART</c> is two effects.
    /// </summary>
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

    /// <summary><c>HIGHEST_PCT_BONUS</c> is <c>STAT_COPY</c>'s alone (`18` §2.4).</summary>
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

    /// <summary>
    /// 🔒 The two op-specific keys the in-code validator and the schema had disagreed about.
    /// </summary>
    /// <remarks>
    /// The schema admits <c>valueMode</c> on nine ops and <c>statusId</c> on four; the code path
    /// policed neither, so <c>{"op":"EXTRA_ATTACK","valueMode":"FLAT"}</c> was well-formed in code
    /// and rejected by the schema — the two enforcement paths disagreeing about one effect.
    /// </remarks>
    [Theory]
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

    /// <summary>
    /// 🔒 A <c>valueMode</c> the op's own `18` §2.2 row rules out is a problem at validation, not a
    /// surprise at fire time.
    /// </summary>
    [Theory]
    [InlineData(EffectOp.HEAL_LEECH, ValueMode.ATK_MULT)]
    [InlineData(EffectOp.DAMAGE, ValueMode.FLAT)]
    [InlineData(EffectOp.REVIVE, ValueMode.FLAT)]
    [InlineData(EffectOp.REFLECT, ValueMode.SELF_MAXHP_PCT)]
    public void A_value_mode_the_op_does_not_admit_is_a_problem(EffectOp op, ValueMode mode)
    {
        EffectOpValidation.Problems(Minimal(op) with { ValueMode = mode })
                          .ShouldContain(p => p.Contains($"carries valueMode {mode}", StringComparison.Ordinal));
    }

    /// <summary>The five keys `18` §10 added are each required where their op needs them.</summary>
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

    /// <summary>
    /// 🔒 Every exemplar the three op suites share is well-formed — so the seam, resolver and
    /// validation suites cannot disagree about what an authorable effect looks like.
    /// </summary>
    [Fact]
    public void Every_exemplar_the_op_suites_share_is_well_formed()
    {
        EffectOps.All.Count.ShouldBe(43, "the floor under the loop");

        EffectOps.All
                 .SelectMany(op => EffectOpValidation.Problems(OpFixtures.Exemplar(op))
                                                     .Select(p => $"{op}: {p}"))
                 .ShouldBeEmpty();
    }

    /// <summary>`18` §7's worked examples all validate, as authored.</summary>
    [Fact]
    public void The_worked_examples_of_18_7_are_well_formed()
    {
        var examples = new EffectDefinition[]
        {
            // §7.1 PK_SHARP_EDGE
            new()
            {
                Id = "PK_SHARP_EDGE_T1_ATK", Op = EffectOp.STAT_ADD_PCT,
                Stat = StatSelector.Of(StatId.ATK), Value = 0.12, Target = EffectTarget.SELF,
            },

            // §7.4 PK_UNBREAKABLE, with the valueMode 18 §10 E4 added
            new()
            {
                Id = "PK_UNBREAKABLE_T1_SURVIVE", Op = EffectOp.SURVIVE_LETHAL,
                Value = 1, ValueMode = ValueMode.FLAT,
            },

            // §7.5 CP_BLOOD_PRICE's drawback
            new()
            {
                Id = "CP_BLOOD_PRICE_COST", Op = EffectOp.DAMAGE_MAXHP_PCT, Value = 0.03,
                Target = EffectTarget.SELF, Tags = ["drawback"],
            },

            // §7.6 Avatar of War's cap override
            new()
            {
                Id = "TAL_AVATAR_OF_WAR_CAP", Op = EffectOp.STAT_CAP_OVERRIDE,
                Stat = StatSelector.Of(StatId.MAX_HP), Value = 0.80, CapKind = StatCapKind.HEAL_CEILING,
            },

            // §7.8 Thornmaw's summon
            new()
            {
                Id = "BOSS_THORNMAW_P3_SWARM", Op = EffectOp.SUMMON, Archetype = "SWARM",
                Value = 2, MaxAlive = 3,
            },

            // §7.9 TILE_DICE_FORGE
            new()
            {
                Id = "TILE_DICE_FORGE_FACE", Op = EffectOp.MODIFY_DIE_FACE,
                FaceIndex = DieFaceIndex.PlayerChoice, NewFace = new DieFaceSpec("Pip", 4),
            },

            // §7.10 PK_CLEAVE
            new()
            {
                Id = "PK_CLEAVE_T1", Op = EffectOp.DAMAGE, Value = 0.40, Target = EffectTarget.OTHER_ENEMIES,
            },
        };

        // S3 — the floor under the loop: an empty fixture list would report success over nothing.
        examples.Length.ShouldBe(7);

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
            EffectOp.MODIFY_DIE_FACE => effect with { NewFace = new DieFaceSpec("Star") },
            _ => effect,
        };
    }
}
