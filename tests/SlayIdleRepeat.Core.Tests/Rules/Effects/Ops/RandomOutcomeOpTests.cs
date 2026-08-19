using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary><c>RANDOM_OUTCOME</c>: one draw over a weighted table of mutually exclusive outcomes.</summary>
/// <remarks>
/// Internal seam: the draw-stream <c>Position</c> and the losing rows' non-application are visible in
/// no <c>CombatEvent</c> — a public fight shows only the winner's consequences. Validation throws
/// before any fight exists.
/// </remarks>
public sealed class RandomOutcomeOpTests
{
    private const string BossAtk = "BOSS_DICELORD_FATE_BOSS_ATK";
    private const string HeroAtk = "BOSS_DICELORD_FATE_HERO_ATK";
    private const string BothAspd = "BOSS_DICELORD_FATE_BOTH_ASPD";

    /// <summary>One roll costs exactly one draw index — the whole reason the op exists rather than three <c>chance</c> gates.</summary>
    [Fact]
    public void RANDOM_OUTCOME_consumes_exactly_one_draw_index_per_roll()
    {
        var bench = new OpTestBench();
        var rng = EffectTestBattle.CombatRng(6);
        var evaluation = Battle(rng);

        rng.Position.ShouldBe(0UL, "the control: a fresh combat stream has spent nothing");

        CombatFlowOps.RandomOutcome(PhaseOne(), bench.Context(evaluation));

        rng.Position.ShouldBe(
            1UL,
            "14 §8.0's WeightedPick is ONE draw. Three chance-gated effects would spend three, and " +
            "Position is the persisted state of the stream — so the difference desynchronises every " +
            "later draw of the battle between client and server");
    }

    /// <summary>The negative control: the op validates before it draws, so a refused roll shifts no later draw.</summary>
    [Fact]
    public void A_refused_RANDOM_OUTCOME_consumes_no_draw_index()
    {
        var bench = new OpTestBench();
        var rng = EffectTestBattle.CombatRng(6);

        var allZero = Roll(
            "BOSS_DICELORD_ROLL_OF_FATE_DEAD",
            new RandomOutcomeEntry(BossAtk, 0.0),
            new RandomOutcomeEntry(HeroAtk, 0.0));

        Should.Throw<EffectContextException>(
                  () => CombatFlowOps.RandomOutcome(allZero, bench.Context(Battle(rng))))
              .Message.ShouldContain("every RANDOM_OUTCOME row weighs zero", Case.Sensitive);

        rng.Position.ShouldBe(0UL, "a rejected call is not a call — a spent index here would shift " +
                                   "every later draw of the battle");
        bench.RandomOutcomes.ShouldBeEmpty();
    }

    [Fact]
    public void RANDOM_OUTCOME_returns_the_1_based_index_of_the_row_its_seed_drew()
    {
        var bench = new OpTestBench();

        var index = CombatFlowOps.RandomOutcome(
            PhaseOne(), bench.Context(Battle(EffectTestBattle.CombatRng(6))));

        // battleSeed 6, combat draw 0 → unit 0.332338; over 2/2/2 (total 6) the threshold is 1.994,
        // which the first row's cumulative 2 is the first to exceed.
        index.ShouldBe(1.0, "the 1-based index of the winning row IS this op's number");
        bench.RandomOutcomes.ShouldBe([("BOSS_DICELORD", BossAtk, "BOSS_DICELORD_ROLL_OF_FATE_P1")]);
    }

    /// <summary>
    /// The same table on two other seeds reaches the other two rows — so the case above is pinning a
    /// draw and not a table position the op always returns.
    /// </summary>
    [Theory]
    [InlineData(1UL, HeroAtk, 2.0)]
    [InlineData(5UL, BothAspd, 3.0)]
    public void A_different_seed_over_one_table_reaches_a_different_row(
        ulong battleSeed, string expected, double expectedIndex)
    {
        var bench = new OpTestBench();

        var index = CombatFlowOps.RandomOutcome(
            PhaseOne(), bench.Context(Battle(EffectTestBattle.CombatRng(battleSeed))));

        index.ShouldBe(expectedIndex);
        bench.RandomOutcomes.Select(o => o.ChosenEffectId).ShouldBe([expected], Case.Sensitive);
    }

    /// <summary>The op's outcome travels through the resolver as a <c>RESOLVED</c> combat-flow op.</summary>
    [Fact]
    public void The_resolver_reports_the_winning_index_as_the_ops_resolved_amount()
    {
        var bench = new OpTestBench();

        var outcome = EffectOpResolver.Resolve(
            PhaseOne(), bench.Context(Battle(EffectTestBattle.CombatRng(6))));

        outcome.Op.ShouldBe(EffectOp.RANDOM_OUTCOME);
        outcome.Amount.ShouldBe(1.0);
        outcome.Disposition.ShouldBe(
            OpDisposition.RESOLVED,
            "a roll happens in the battle — it is neither queued for the run controller nor applied " +
            "by 18 §8's aggregation");
    }

    /// <summary>
    /// The weights are read: the two rows point the same trick opposite ways, so an implementation
    /// that happened to agree with a uniform pick on one seed cannot agree on both. Seed 8 draws
    /// 0.202 (weighted takes the heavy SECOND row, uniform the first); seed 3 draws 0.523 (weighted
    /// takes the heavy FIRST, uniform the second).
    /// </summary>
    [Theory]
    [InlineData(8UL, 1.0, 9.0, "EFF_SECOND", 2.0)]
    [InlineData(3UL, 9.0, 1.0, "EFF_FIRST", 1.0)]
    public void The_weights_bias_the_draw_where_a_uniform_pick_would_answer_otherwise(
        ulong battleSeed, double firstWeight, double secondWeight, string expected, double expectedIndex)
    {
        var bench = new OpTestBench();

        var lopsided = Roll(
            "BOSS_DICELORD_ROLL_OF_FATE_LOPSIDED",
            new RandomOutcomeEntry("EFF_FIRST", firstWeight),
            new RandomOutcomeEntry("EFF_SECOND", secondWeight));

        var index = CombatFlowOps.RandomOutcome(
            lopsided, bench.Context(Battle(EffectTestBattle.CombatRng(battleSeed))));

        bench.RandomOutcomes.Select(o => o.ChosenEffectId).ShouldBe([expected], Case.Sensitive);
        index.ShouldBe(expectedIndex);
    }

    /// <summary>A zero-weight row is unreachable, wherever it sits — how authored content disables one outcome without disabling the roll.</summary>
    [Fact]
    public void A_zero_weight_outcome_is_never_drawn_across_five_hundred_seeds()
    {
        var disabledFirst = Roll(
            "BOSS_DICELORD_ROLL_OF_FATE_DISABLED",
            new RandomOutcomeEntry("EFF_DISABLED", 0.0),
            new RandomOutcomeEntry("EFF_LIVE_A", 1.0),
            new RandomOutcomeEntry("EFF_LIVE_B", 1.0));

        var picks = PicksAcrossSeeds(disabledFirst, seeds: 500);

        picks.Count.ShouldBe(500, "the floor: ShouldNotContain over an empty list is vacuously true");

        picks.ShouldNotContain("EFF_DISABLED", "a weight of 0 disables the row");
        picks.ShouldContain("EFF_LIVE_A", "the control: the live rows ARE reachable");
        picks.ShouldContain("EFF_LIVE_B");
    }

    /// <summary>
    /// Exactly one outcome fires per roll — the property three <c>chance</c>-gated effects cannot
    /// have, since each gate is drawn independently and all three can pass.
    /// </summary>
    [Fact]
    public void Exactly_one_outcome_fires_per_roll_and_the_seam_is_called_once()
    {
        var bench = new OpTestBench();

        CombatFlowOps.RandomOutcome(PhaseOne(), bench.Context(Battle(EffectTestBattle.CombatRng(6))));

        bench.RandomOutcomes.Count.ShouldBe(
            1, "three outcomes, one draw, one winner — that is what 'mutually exclusive' means here");
        bench.Calls.ShouldBe([$"RandomOutcome:{BossAtk}(BOSS_DICELORD, 0, BOSS_DICELORD_ROLL_OF_FATE_P1)"],
                             Case.Sensitive,
                             "and nothing else on any seam: the losing rows are not applied at all");
    }

    /// <summary>The positive control for the refusal cases below.</summary>
    [Fact]
    public void A_well_formed_RANDOM_OUTCOME_reports_no_problem()
    {
        EffectOpValidation.Problems(PhaseOne()).ShouldBeEmpty();
        EffectOpValidation.IsWellFormed(PhaseOne()).ShouldBeTrue();
    }

    /// <summary>An op with no table at all — the table IS the op.</summary>
    [Fact]
    public void A_RANDOM_OUTCOME_with_no_outcomes_is_refused()
    {
        var bare = new EffectDefinition { Id = "BOSS_X_ROLL", Op = EffectOp.RANDOM_OUTCOME };

        EffectOpValidation.Problems(bare)
                          .ShouldContain(p => p.Contains("names no outcomes", StringComparison.Ordinal));
    }

    /// <summary>One outcome is not a choice — author the effect directly instead.</summary>
    [Fact]
    public void A_RANDOM_OUTCOME_with_a_single_outcome_is_refused()
    {
        var lonely = Roll("BOSS_X_ROLL", new RandomOutcomeEntry(BossAtk, 1.0));

        EffectOpValidation.Problems(lonely)
                          .ShouldContain(p => p.Contains("outcome(s); one outcome is not a choice",
                                                         StringComparison.Ordinal));
    }

    /// <summary>The same effect twice: the walk would answer with the first row every time.</summary>
    [Fact]
    public void A_RANDOM_OUTCOME_naming_one_effect_twice_is_refused()
    {
        var doubled = Roll(
            "BOSS_X_ROLL",
            new RandomOutcomeEntry(BossAtk, 1.0),
            new RandomOutcomeEntry(BossAtk, 5.0));

        EffectOpValidation.Problems(doubled)
                          .ShouldContain(p => p.Contains($"names '{BossAtk}' twice", StringComparison.Ordinal));
    }

    /// <summary>
    /// A row that names no effect id. <see cref="RandomOutcomeEntry"/> is a record struct, so a JSON
    /// row omitting <c>effectId</c> carries a null one, which would otherwise raise a bare
    /// <see cref="ArgumentNullException"/> naming no rule at all.
    /// </summary>
    [Theory]
    [InlineData(null, "an absent effectId")]
    [InlineData("", "an empty one")]
    [InlineData("   ", "a whitespace one")]
    public void A_RANDOM_OUTCOME_row_naming_no_effect_id_is_refused(string? blank, string why)
    {
        var nameless = Roll(
            "BOSS_X_ROLL",
            new RandomOutcomeEntry(BossAtk, 1.0),
            new RandomOutcomeEntry(blank!, 1.0));

        EffectOpValidation.Problems(nameless)
                          .ShouldContain(
                              p => p.Contains("names no effectId", StringComparison.Ordinal),
                              $"which rule fired — {why}");
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_RANDOM_OUTCOME_with_a_weight_that_is_not_finite_and_non_negative_is_refused(double weight)
    {
        var broken = Roll(
            "BOSS_X_ROLL",
            new RandomOutcomeEntry(BossAtk, 1.0),
            new RandomOutcomeEntry(HeroAtk, weight));

        EffectOpValidation.Problems(broken)
                          .ShouldContain(p => p.Contains($"weighs '{HeroAtk}' at", StringComparison.Ordinal));
    }

    /// <summary>Every row zero: there is no row to pick, so the roll is dead weight.</summary>
    [Fact]
    public void A_RANDOM_OUTCOME_whose_every_weight_is_zero_is_refused()
    {
        var dead = Roll(
            "BOSS_X_ROLL",
            new RandomOutcomeEntry(BossAtk, 0.0),
            new RandomOutcomeEntry(HeroAtk, 0.0));

        EffectOpValidation.Problems(dead)
                          .ShouldContain(p => p.Contains("every RANDOM_OUTCOME row weighs zero",
                                                         StringComparison.Ordinal));
    }

    /// <summary>A row naming the roll itself rolls the roll — unbounded, one draw index per turn.</summary>
    [Fact]
    public void A_RANDOM_OUTCOME_naming_its_own_id_as_an_outcome_is_refused()
    {
        var ouroboros = Roll(
            "BOSS_DICELORD_ROLL_OF_FATE_P1",
            new RandomOutcomeEntry(BossAtk, 1.0),
            new RandomOutcomeEntry("BOSS_DICELORD_ROLL_OF_FATE_P1", 1.0));

        EffectOpValidation.Problems(ouroboros)
                          .ShouldContain(p => p.Contains(
                              "names its own id 'BOSS_DICELORD_ROLL_OF_FATE_P1' as an outcome",
                              StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>RANDOM_OUTCOME</c> carries no <c>value</c>: its number is the winning row's index, which
    /// nothing authors. Refused rather than ignored, so a stray value can't ship a misreading.
    /// </summary>
    [Fact]
    public void A_RANDOM_OUTCOME_carrying_a_value_is_refused_rather_than_ignored()
    {
        EffectOpValidation.Problems(PhaseOne() with { Value = 3.0 })
                          .ShouldContain(p => p.Contains("RANDOM_OUTCOME carries a value",
                                                         StringComparison.Ordinal));
    }

    /// <summary>And <c>outcomes</c> on any other op is a borrowed key, exactly like the other ten.</summary>
    [Fact]
    public void The_outcomes_key_on_another_op_is_refused()
    {
        var control = OpFixtures.Effect("PK_SHARP_EDGE_T1_ATK", EffectOp.STAT_ADD_PCT, 0.12) with
        {
            Stat = StatSelector.Of(StatId.ATK),
        };

        EffectOpValidation.Problems(control).ShouldBeEmpty("the control: the same effect without the key");

        EffectOpValidation.Problems(control with
        {
            Outcomes = new[] { new RandomOutcomeEntry(BossAtk, 1.0), new RandomOutcomeEntry(HeroAtk, 1.0) },
        }).ShouldContain(p => p.Contains("carries 'outcomes'", StringComparison.Ordinal));
    }

    /// <summary>The op with no draw stream is refused rather than answering with the first row — a stable, reproducible, wrong "random".</summary>
    [Fact]
    public void A_RANDOM_OUTCOME_in_a_context_with_no_draw_stream_is_refused()
    {
        var bench = new OpTestBench();
        var noStream = Battle(rng: null);

        Should.Throw<EffectContextException>(
                  () => CombatFlowOps.RandomOutcome(PhaseOne(), bench.Context(noStream)))
              .Message.ShouldContain("no draw stream", Case.Sensitive);

        bench.RandomOutcomes.ShouldBeEmpty();
    }

    /// <summary>The Dicelord's phase 1 <em>Roll of Fate</em>: one d6, three equally weighted results.</summary>
    private static EffectDefinition PhaseOne() => Roll(
        "BOSS_DICELORD_ROLL_OF_FATE_P1",
        new RandomOutcomeEntry(BossAtk, 2.0),
        new RandomOutcomeEntry(HeroAtk, 2.0),
        new RandomOutcomeEntry(BothAspd, 2.0));

    /// <summary>A <c>RANDOM_OUTCOME</c> in its authored shape: PERIODIC, on SELF, no value.</summary>
    private static EffectDefinition Roll(string id, params RandomOutcomeEntry[] outcomes) =>
        new()
        {
            Id = id,
            Op = EffectOp.RANDOM_OUTCOME,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 10.0 },
            Target = EffectTarget.SELF,
            Outcomes = outcomes,
        };

    /// <summary>The Dicelord alone against the hero, with the battle's combat stream handed in.</summary>
    private static EffectEvaluationContext Battle(DeterministicRng? rng)
    {
        var hero = EffectTestBattle.Hero(maxHp: 1000);
        var dicelord = EffectTestBattle.Enemy("BOSS_DICELORD", 1, maxHp: 5000) with { IsBoss = true };

        return EffectTestBattle.Context(dicelord, hero, dicelord) with
        {
            CurrentTarget = hero,
            Rng = rng,
        };
    }

    /// <summary>One roll per seed over <c>1..seeds</c>, and the effect id each one chose — one seed cannot show that a disabled row is unreachable wherever it sits.</summary>
    private static IReadOnlyList<string> PicksAcrossSeeds(EffectDefinition roll, int seeds)
    {
        var picks = new List<string>();

        for (var seed = 1UL; seed <= (ulong)seeds; seed++)
        {
            var bench = new OpTestBench();
            CombatFlowOps.RandomOutcome(roll, bench.Context(Battle(EffectTestBattle.CombatRng(seed))));
            picks.AddRange(bench.RandomOutcomes.Select(o => o.ChosenEffectId));
        }

        return picks;
    }
}
