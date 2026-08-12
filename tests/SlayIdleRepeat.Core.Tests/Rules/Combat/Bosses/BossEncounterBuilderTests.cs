using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1.2 / §11 — one <see cref="BossScript"/> resolved into the encounter the simulator runs:
/// the statline, every phase block on the plan, the two universal built-ins, and the six authoring
/// rules that fire before a tick runs.
/// </summary>
/// <remarks>
/// Every refusal below asserts <b>which</b> rule fired (steering S2). A builder that threw one
/// message for six different authoring mistakes would send M2-13 looking in the wrong place five
/// times out of six.
/// </remarks>
public sealed class BossEncounterBuilderTests
{
    private const string Bask = "BOSS_THORNMAW_P1_BASK";
    private const string Snap = "BOSS_THORNMAW_P2_SNAP";
    private const string Bloom = "BOSS_THORNMAW_P3_BLOOM";

    // ════════════════════════════════════════════════════ 1 · the statline

    /// <summary>
    /// 🔴 `05` §6.3 / `17` §1 — <em>"<c>EnemyPower(i)</c> … already includes
    /// <c>StageMult.Boss = 2.20</c>. Do <b>not</b> multiply again."</em> The builder derives from the
    /// power it was handed, and the expectation here is <c>EnemyDerivation</c>'s own answer at that
    /// same power.
    /// </summary>
    [Fact]
    public void The_statline_derives_from_the_power_as_handed_in_and_never_multiplies_it_again()
    {
        var encounter = Build();

        var row = EnemyFixtures.Row(EnemyArchetype.GRUNT) with
        {
            HpCoef = BossTestBench.ThornmawCoefficients.Hp,
            AtkCoef = BossTestBench.ThornmawCoefficients.Atk,
            DefCoef = BossTestBench.ThornmawCoefficients.Def,
            AspdCoef = BossTestBench.ThornmawCoefficients.Aspd,
        };

        var expected = EnemyDerivation.Derive(10_000.0, row, EnemyFixtures.Constants());

        encounter.Plan.BaseStats[StatId.MAX_HP].ShouldBe(
            expected[StatId.MAX_HP],
            "10000 x 0.60 x 2.40 = 14400 — and 2.20 x that (31680) would be the double-multiplication " +
            "05 §6.3 forbids");
        encounter.Plan.BaseStats[StatId.ATK].ShouldBe(expected[StatId.ATK]);
        encounter.Plan.BaseStats[StatId.ASPD].ShouldBe(
            expected[StatId.ASPD], "17 §2's 'ASPD 0.7' is this base coefficient, not a phase aura");

        encounter.Plan.BaseStats[StatId.MAX_HP].ShouldNotBe(
            EnemyDerivation.Derive(22_000.0, row, EnemyFixtures.Constants())[StatId.MAX_HP],
            "the negative control: a builder that applied StageMult.Boss itself would land here");
    }

    /// <summary>
    /// 🔒 `17` §1.2 — <em>"a per-boss coefficient row instead of a shared archetype"</em>, with the
    /// baseline's secondaries kept: CRIT 0.05, CDMG 0.50, DODGE 0, LS 0.
    /// </summary>
    [Fact]
    public void The_statline_row_takes_the_scripts_coefficients_and_the_baselines_secondaries()
    {
        var row = BossEncounterBuilder.StatlineRow(
            BossTestBench.ThornmawCoefficients, BossTestBench.Baseline);

        row.HpCoef.ShouldBe(2.40);
        row.AtkCoef.ShouldBe(0.80);
        row.DefCoef.ShouldBe(0.80);
        row.AspdCoef.ShouldBe(0.70);

        row.Crit.ShouldBe(0.05, "17 §1.2's baseline secondaries are not the script's to author");
        row.CritDamage.ShouldBe(0.50);
        row.Dodge.ShouldBe(0.00);
        row.Lifesteal.ShouldBe(0.00);
    }

    // ════════════════════════════════════════════════════ 2 · what lands on the plan

    /// <summary>
    /// 🔴 <b>Every</b> phase block goes onto <see cref="ActorPlan.Effects"/>, phases 2 and 3
    /// included.
    /// </summary>
    /// <remarks>
    /// The battle's effect table is built once from the <b>opening roster</b>, and its positions are
    /// `05` §7's <c>Telegraph</c> and <c>RunEffectQueued</c> indices. A phase-3 mechanic that arrived
    /// mid-fight would have no position in it, so <c>CombatLog.AppendTelegraph</c> could not name it
    /// and the replayer — which rebuilds the same table — could not resolve it.
    /// </remarks>
    [Fact]
    public void Every_phase_block_is_on_the_plan_under_the_phase_tagged_instance_id()
    {
        var encounter = Build();

        var byId = encounter.Plan.Effects.ToDictionary(h => h.Effect.Id, h => h.InstanceId, StringComparer.Ordinal);

        byId.Keys.ShouldContain(Bask);
        byId.Keys.ShouldContain(Snap);
        byId.Keys.ShouldContain(Bloom, "a phase-3 mechanic is on the OPENING roster, not added later");

        byId[Snap]!.Value.Value.ShouldBe("BOSS_THORNMAW#P2#BOSS_THORNMAW_P2_SNAP");
        byId[Bloom]!.Value.Value.ShouldBe("BOSS_THORNMAW#P3#BOSS_THORNMAW_P3_BLOOM");

        encounter.PhaseOfInstance[BossBuiltIns.PhaseInstance("BOSS_THORNMAW", 3, Bloom)].ShouldBe(3);
    }

    /// <summary>
    /// 🔒 `17` §11 — the two universal built-ins are attached <b>by the builder</b>, once, for every
    /// boss: the 70 s enrage and the phase-3 <c>STUN</c>/<c>FREEZE</c> immunity.
    /// </summary>
    [Fact]
    public void The_three_universal_built_ins_are_attached_to_every_boss()
    {
        var encounter = Build();

        var ids = encounter.Plan.Effects.Select(h => h.Effect.Id).ToArray();

        ids.Length.ShouldBeGreaterThan(0, "the floor under the membership assertions");
        ids.ShouldContain(BossBuiltIns.EnrageId);
        ids.ShouldContain(BossBuiltIns.Phase3StunImmunityId);
        ids.ShouldContain(BossBuiltIns.Phase3FreezeImmunityId);

        encounter.PhaseOfInstance.Keys.Select(k => k.Value).ShouldNotContain(
            "BOSS_THORNMAW#SYS_ENRAGE",
            "🔒 the enrage is deliberately OUTSIDE the phase map — nothing a transition walks can " +
            "reach it, so nothing can re-anchor its R8 clock");
    }

    /// <summary>The encounter carries the boundaries `17` §1 gives it, for the first-clear flag.</summary>
    [Theory]
    [InlineData(false, 0.66)]
    [InlineData(true, 0.5920)]
    public void The_encounter_carries_17_1s_boundaries_for_its_first_clear_flag(
        bool firstClear, double expected)
    {
        var encounter = Build(firstClear: firstClear);

        encounter.FirstClear.ShouldBe(firstClear);
        encounter.Phase2HpFraction.ShouldBe(expected);
        encounter.Phase3HpFraction.ShouldBe(0.33, "only phase 1 is extended");
    }

    // ════════════════════════════════════════════════════ 3 · the authoring rules, one at a time

    /// <summary>🔒 `17` §1 — <em>"exactly 3"</em> phases, numbered 1, 2, 3, in that order.</summary>
    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 2, 3, 3)]
    [InlineData(2, 1, 3)]
    [InlineData(1, 2, 4)]
    public void A_script_whose_phase_blocks_are_not_1_2_3_in_order_is_refused(params int[] phases)
    {
        var script = BossTestBench.Script(
            "BOSS_THORNMAW", phases.Select(p => BossTestBench.Block(p)).ToArray());

        var thrown = Should.Throw<EffectContextException>(
            () => BossEncounterBuilder.Build(
                BossTestBench.Request(script, BossTestBench.Lookup())));

        thrown.Message.ShouldContain("phases", Case.Insensitive);
        thrown.Message.ShouldContain("BOSS_THORNMAW", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A mechanic is a <b>sibling reference</b> — an id the same script declares — so one that
    /// resolves to nothing in <see cref="BossEncounterRequest.Effects"/> is refused.
    /// </summary>
    [Fact]
    public void A_mechanic_whose_effect_id_resolves_to_nothing_is_refused()
    {
        var script = BossTestBench.Script(
            "BOSS_THORNMAW",
            BossTestBench.Block(1, new BossMechanic("BOSS_THORNMAW_P1_TYPO")),
            BossTestBench.Block(2),
            BossTestBench.Block(3));

        var thrown = Should.Throw<EffectContextException>(
            () => BossEncounterBuilder.Build(
                BossTestBench.Request(script, BossTestBench.Lookup(Aura(Bask)))));

        thrown.Message.ShouldContain("BOSS_THORNMAW_P1_TYPO", Case.Sensitive, "which mechanic");
    }

    /// <summary>
    /// 🔴 A phase-2 or phase-3 block may not carry an <c>ON_BATTLE_START</c> trigger. `05` §3.1's 0b
    /// sweep runs <b>before</b> 0c, so such an effect fires while the boss is still in phase 1 — a
    /// phase-3 mechanic landing at battle start, in a fight that looks entirely legal.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void A_phase_2_or_3_block_carrying_an_ON_BATTLE_START_trigger_is_refused(int phase)
    {
        var opener = Aura($"BOSS_THORNMAW_P{phase}_OPENER") with
        {
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_START },
        };

        var blocks = new[] { 1, 2, 3 }
            .Select(p => p == phase
                ? BossTestBench.Block(p, new BossMechanic(opener.Id))
                : BossTestBench.Block(p))
            .ToArray();

        var thrown = Should.Throw<EffectContextException>(
            () => BossEncounterBuilder.Build(
                BossTestBench.Request(
                    BossTestBench.Script("BOSS_THORNMAW", blocks), BossTestBench.Lookup(opener))));

        thrown.Message.ShouldContain("ON_BATTLE_START", Case.Sensitive);
        thrown.Message.ShouldContain(opener.Id, Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The negative control for the rule above: phase <b>1</b> may carry one, because 0b runs for
    /// the phase the boss is actually in.
    /// </summary>
    [Fact]
    public void A_phase_1_block_may_carry_an_ON_BATTLE_START_trigger()
    {
        var opener = Aura(Bask) with
        {
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_START },
        };

        var script = BossTestBench.Script(
            "BOSS_THORNMAW",
            BossTestBench.Block(1, new BossMechanic(opener.Id)),
            BossTestBench.Block(2),
            BossTestBench.Block(3));

        var encounter = BossEncounterBuilder.Build(
            BossTestBench.Request(script, BossTestBench.Lookup(opener)));

        encounter.Plan.Effects.Select(h => h.Effect.Id).ShouldContain(Bask);
    }

    /// <summary>
    /// 🔒 `17` §11 — the built-ins are <em>"implemented once, applied to all bosses"</em>, so a
    /// script that authored one would be a second, disagreeing copy.
    /// </summary>
    [Theory]
    [InlineData("SYS_ENRAGE")]
    [InlineData("SYS_PHASE3_IMMUNE_STUN")]
    public void A_script_that_authors_one_of_the_built_ins_is_refused(string builtInId)
    {
        var script = BossTestBench.Script(
            "BOSS_THORNMAW",
            BossTestBench.Block(1, new BossMechanic(builtInId)),
            BossTestBench.Block(2),
            BossTestBench.Block(3));

        var thrown = Should.Throw<EffectContextException>(
            () => BossEncounterBuilder.Build(
                BossTestBench.Request(script, BossTestBench.Lookup(BossBuiltIns.Enrage))));

        thrown.Message.ShouldContain(builtInId, Case.Sensitive);
    }

    /// <summary>
    /// 🔒 `17` §1 — <em>"capped at 3 alive at once"</em>. The cap is `18` §2.4's authored
    /// <c>maxAlive</c>, and the builder is where the authoring is checked against the document.
    /// </summary>
    [Fact]
    public void A_SUMMON_mechanic_above_the_three_alive_cap_is_refused()
    {
        var summon = BossTestBench.Summon("BOSS_THORNMAW_P3_ADDS", maxAlive: 4);

        var script = BossTestBench.Script(
            "BOSS_THORNMAW",
            BossTestBench.Block(1),
            BossTestBench.Block(2),
            BossTestBench.Block(3, new BossMechanic(summon.Id)));

        var thrown = Should.Throw<EffectContextException>(
            () => BossEncounterBuilder.Build(
                BossTestBench.Request(script, BossTestBench.Lookup(summon))));

        thrown.Message.ShouldContain("3", Case.Sensitive, "17 §1's cap");
        thrown.Message.ShouldContain(summon.Id, Case.Sensitive);
    }

    /// <summary>The positive control: `17` §2's own phase-3 summon, at the authored cap, is accepted.</summary>
    [Fact]
    public void A_SUMMON_mechanic_at_the_three_alive_cap_is_accepted()
    {
        var summon = BossTestBench.Summon("BOSS_THORNMAW_P3_ADDS");

        var script = BossTestBench.Script(
            "BOSS_THORNMAW",
            BossTestBench.Block(1),
            BossTestBench.Block(2),
            BossTestBench.Block(3, new BossMechanic(summon.Id)));

        BossEncounterBuilder.Build(BossTestBench.Request(script, BossTestBench.Lookup(summon)))
                            .Plan.Effects.Select(h => h.Effect.Id).ShouldContain(summon.Id);
    }

    // ════════════════════════════════════════════════════ 4 · O1 — the outcomes' sibling scope

    /// <summary>
    /// 🔒 <b>O1</b> — a <c>RANDOM_OUTCOME</c> row names a <b>sibling</b>: an effect id the
    /// <em>same</em> script declares. There is no registry for it to reach past its owner into, so an
    /// id outside <see cref="BossEncounterRequest.Effects"/> is an authoring error and is refused
    /// <b>here</b>, before a tick runs.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Two shapes, because the row and the mechanic are different scopes to get wrong.</b>
    /// <c>notASibling</c> is an id that exists nowhere; <c>siblingOfAnotherBoss</c> is an id that is
    /// perfectly real content belonging to a <em>different</em> boss — which is the case a registry
    /// would have accepted and which a sibling scope must not. Both are refused, and the message
    /// names the rule (steering S2) so M2-13 is not left comparing eight scripts by hand.
    /// </remarks>
    [Theory]
    [InlineData("BOSS_DICELORD_FATE_TYPO", "an id nothing declares")]
    [InlineData("BOSS_THORNMAW_P2_SNAP", "a real effect id — of ANOTHER boss's script")]
    public void A_RANDOM_OUTCOME_row_naming_a_non_sibling_effect_id_is_refused(
        string outsider, string why)
    {
        var roll = BossTestBench.RollOfFateP1() with
        {
            Outcomes = new[]
            {
                new RandomOutcomeEntry(BossTestBench.FateBossAtk, 2.0),
                new RandomOutcomeEntry(outsider, 2.0),
            },
        };

        var script = BossTestBench.Script(
            BossTestBench.Dicelord,
            BossTestBench.Block(1, new BossMechanic(roll.Id)),
            BossTestBench.Block(2),
            BossTestBench.Block(3));

        // 🔒 The lookup is this script's own effect set. The first row IS in it; the second is not.
        var effects = BossTestBench.Lookup(
            roll, BossTestBench.FateOutcome(BossTestBench.FateBossAtk));

        var thrown = Should.Throw<EffectContextException>(
            () => BossEncounterBuilder.Build(BossTestBench.Request(script, effects)));

        thrown.Message.ShouldContain("O1", Case.Sensitive, $"which rule fired — {why}");
        thrown.Message.ShouldContain(outsider, Case.Sensitive, "which row");
        thrown.Message.ShouldContain(roll.Id, Case.Sensitive, "which RANDOM_OUTCOME");
        thrown.Message.ShouldContain(BossTestBench.Dicelord, Case.Sensitive, "which boss");
    }

    /// <summary>
    /// 🔒 The negative control for O1, and the half that makes the rule a scope rather than a ban:
    /// every row naming a sibling of the <b>same</b> script is accepted, and each of those siblings
    /// lands on <see cref="ActorPlan.Effects"/> — which is why it is a reference and not an embedded
    /// effect object in the first place.
    /// </summary>
    [Fact]
    public void Outcome_rows_that_are_siblings_of_the_same_script_are_accepted_and_land_on_the_plan()
    {
        var roll = BossTestBench.RollOfFateP1();

        var script = BossTestBench.Script(
            BossTestBench.Dicelord,
            BossTestBench.Block(
                1,
                new BossMechanic(roll.Id),
                new BossMechanic(BossTestBench.FateBossAtk),
                new BossMechanic(BossTestBench.FateHeroAtk),
                new BossMechanic(BossTestBench.FateBothAspd)),
            BossTestBench.Block(2),
            BossTestBench.Block(3));

        var effects = BossTestBench.Lookup(
            roll,
            BossTestBench.FateOutcome(BossTestBench.FateBossAtk),
            BossTestBench.FateOutcome(BossTestBench.FateHeroAtk),
            BossTestBench.FateOutcome(BossTestBench.FateBothAspd));

        var ids = BossEncounterBuilder.Build(BossTestBench.Request(script, effects))
                                      .Plan.Effects.Select(h => h.Effect.Id).ToArray();

        ids.Length.ShouldBeGreaterThan(0, "the floor under the membership assertions");
        ids.ShouldContain(roll.Id);
        ids.ShouldContain(BossTestBench.FateBossAtk);
        ids.ShouldContain(BossTestBench.FateHeroAtk);
        ids.ShouldContain(
            BossTestBench.FateBothAspd,
            "an outcome row is an ordinary phase mechanic: it has to be on the plan to be " +
            "registered, telegraphable and resolvable in the battle's effect table, which is why " +
            "the row references it instead of embedding a second copy of it");
    }

    // ════════════════════════════════════════════════════ fixtures

    private static EffectDefinition Aura(string id) => BossTestBench.OnPhaseEnter(id, phase: 1);

    private static BossEncounter Build(bool firstClear = false)
    {
        var script = BossTestBench.Script(
            "BOSS_THORNMAW",
            BossTestBench.Block(1, new BossMechanic(Bask)),
            BossTestBench.Block(2, new BossMechanic(Snap)),
            BossTestBench.Block(3, new BossMechanic(Bloom)));

        var effects = BossTestBench.Lookup(
            BossTestBench.OnPhaseEnter(Bask, 1),
            BossTestBench.OnPhaseEnter(Snap, 2),
            BossTestBench.OnPhaseEnter(Bloom, 3));

        return BossEncounterBuilder.Build(
            BossTestBench.Request(script, effects, firstClear: firstClear));
    }
}
