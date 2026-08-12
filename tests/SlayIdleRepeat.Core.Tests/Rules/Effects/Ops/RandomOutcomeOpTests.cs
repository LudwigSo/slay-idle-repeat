using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// 🔒 `18` §2.4 / §10.1 <b>E6</b> — <c>RANDOM_OUTCOME</c>, the forty-fourth op: one draw over a
/// weighted table of <b>mutually exclusive</b> outcomes.
/// </summary>
/// <remarks>
/// <para>
/// The mechanic is `17` §9's Dicelord <em>Roll of Fate</em> — one visible d6 with three results, of
/// which exactly one happens. `18` §4's conditions are <em>"pure functions of current state"</em>
/// and a draw is not state, so three <c>chance</c>-gated effects are three <b>independent</b> draws:
/// all three can fire, or none, and they spend <b>three</b> draw indices where `14` §8.0's
/// <see cref="DeterministicRng.WeightedPick{T}"/> spends <b>one</b>. Because
/// <see cref="DeterministicRng.Position"/> is the persisted state of the stream, the two readings
/// desynchronise every later draw of the battle — which is why the single-draw case below is the
/// load-bearing one `18` §10 step 3 asks for.
/// </para>
/// <para>
/// 🔴 <b>PHASE 1a.</b> <c>CombatFlowOps.RandomOutcome</c> is a stub that throws
/// <see cref="NotSupportedException"/> naming M2-12's boss-engine phase, so every <b>behaviour</b>
/// case here is red on purpose and describes what replaces it. The <b>paperwork</b> cases —
/// <see cref="EffectOpValidation"/>, the family, the vocabulary count — are green today.
/// </para>
/// </remarks>
public sealed class RandomOutcomeOpTests
{
    // `17` §9's two authored tables. Phase 1 is the three-outcome d6; phase 2 drops the
    // hero-favourable row (kickoff decision 1(c)) and reweights what is left.
    private const string BossAtk = "BOSS_DICELORD_FATE_BOSS_ATK";
    private const string HeroAtk = "BOSS_DICELORD_FATE_HERO_ATK";
    private const string BothAspd = "BOSS_DICELORD_FATE_BOTH_ASPD";

    // ════════════════════════════════════════════════════ 1 · the single-draw proof

    /// <summary>
    /// 🔒 The load-bearing case: one roll costs <b>exactly one</b> draw index, which is the whole
    /// reason the op exists rather than three <c>chance</c> gates.
    /// </summary>
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

    /// <summary>
    /// 🔒 The negative control: a roll refused for a malformed table spends <b>no</b> draw index.
    /// <see cref="DeterministicRng.WeightedPick{T}"/>'s own contract is that <em>a rejected call is
    /// not a call</em>, and this op validates before it draws for exactly that reason.
    /// </summary>
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

    // ════════════════════════════════════════════════════ 2 · numeric behaviour

    /// <summary>
    /// 🔒 `18` §10 step 3's numeric assertion, pinned at a literal seed: which row won, what the op
    /// returned, and what the seam was handed.
    /// </summary>
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

    // ════════════════════════════════════════════════════ 3 · the weights actually bias the draw

    /// <summary>
    /// 🔒 The weights are read, and a <b>uniform</b> pick over the same rows at the same seed would
    /// answer differently — which a table of three equal weights could never show.
    /// </summary>
    /// <remarks>
    /// Both rows of the theory are the same seed-and-shape trick pointed opposite ways, so a
    /// "weighted" implementation that happened to agree with <c>Range(0, count)</c> on one of them
    /// cannot agree with both:
    /// <list type="bullet">
    ///   <item>seed 8 draws unit <c>0.202427</c>. Over <c>1/9</c> the threshold is <c>2.024</c>, so
    ///   the heavy <b>second</b> row wins; a uniform pick would take <c>floor(0.202 × 2) = 0</c>,
    ///   the first.</item>
    ///   <item>seed 3 draws unit <c>0.522598</c>. Over <c>9/1</c> the threshold is <c>5.226</c>, so
    ///   the heavy <b>first</b> row wins; a uniform pick would take <c>floor(0.523 × 2) = 1</c>,
    ///   the second.</item>
    /// </list>
    /// </remarks>
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

    /// <summary>
    /// 🔒 A zero-weight row is <b>unreachable</b>, wherever it sits: `14` §8.0's walk compares
    /// strictly, and that is how authored content disables one outcome without disabling the roll.
    /// </summary>
    [Fact]
    public void A_zero_weight_outcome_is_never_drawn_across_five_hundred_seeds()
    {
        var disabledFirst = Roll(
            "BOSS_DICELORD_ROLL_OF_FATE_DISABLED",
            new RandomOutcomeEntry("EFF_DISABLED", 0.0),
            new RandomOutcomeEntry("EFF_LIVE_A", 1.0),
            new RandomOutcomeEntry("EFF_LIVE_B", 1.0));

        var picks = PicksAcrossSeeds(disabledFirst, seeds: 500);

        // S3 — the floor. ShouldNotContain over an empty list is vacuously true.
        picks.Count.ShouldBe(500, "one pick per seed, and every roll must have produced one");

        picks.ShouldNotContain("EFF_DISABLED", "a weight of 0 disables the row");
        picks.ShouldContain("EFF_LIVE_A", "the control: the live rows ARE reachable");
        picks.ShouldContain("EFF_LIVE_B");
    }

    // ════════════════════════════════════════════════════ 4 · mutual exclusivity

    /// <summary>
    /// 🔒 Exactly <b>one</b> outcome fires per roll — the property three <c>chance</c>-gated effects
    /// cannot have, since each gate is drawn independently and all three can pass.
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

    // ════════════════════════════════════════════════════ 5 · the Dicelord's two authored tables

    /// <summary>
    /// 🔒 `17` §9's two <em>Roll of Fate</em> tables — phase 1's <c>2/2/2</c> over three outcomes and
    /// phase 2's <c>4/2</c> over two — are both legal for the <b>one</b> op, with no code path
    /// between them. That is the whole claim of E6: the boss script is data.
    /// </summary>
    [Theory]
    [InlineData(6UL, BossAtk, 1.0)]
    [InlineData(5UL, BothAspd, 3.0)]
    public void The_Dicelords_phase_1_table_of_three_draws_from_the_one_op(
        ulong battleSeed, string expected, double expectedIndex) =>
        Draws(PhaseOne(), battleSeed, expected, expectedIndex);

    /// <summary>
    /// Phase 2's two-row <c>4/2</c> table, through the identical code path — the index of the same
    /// winning effect moves from 3 to 2 because the row above it is gone, and nothing else changes.
    /// </summary>
    [Theory]
    [InlineData(6UL, BossAtk, 1.0)]
    [InlineData(5UL, BothAspd, 2.0)]
    public void The_Dicelords_phase_2_table_of_two_draws_from_the_same_op(
        ulong battleSeed, string expected, double expectedIndex) =>
        Draws(PhaseTwo(), battleSeed, expected, expectedIndex);

    /// <summary>Both authored tables are well-formed as data, which is the half `18` §10 fixes.</summary>
    [Fact]
    public void Both_of_the_Dicelords_authored_tables_are_well_formed()
    {
        EffectOpValidation.Problems(PhaseOne()).ShouldBeEmpty();
        EffectOpValidation.Problems(PhaseTwo()).ShouldBeEmpty();

        // The shape the tables are actually authored in — 18 §3's PERIODIC trigger, no value.
        PhaseOne().Trigger!.Kind.ShouldBe(TriggerKind.PERIODIC);
        PhaseOne().Trigger!.Interval.ShouldBe(10.0);
        PhaseOne().Value.ShouldBeNull("18 §10.1 E6 adds a KEY, never a number");
    }

    // ════════════════════════════════════════════════════ 6 · validation, one rule at a time

    /// <summary>The positive control: a well-formed roll reports nothing at all.</summary>
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

    /// <summary>🔒 One outcome is not a choice — author the effect directly instead.</summary>
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
    /// 🔴 A row that names <b>no</b> effect id. <see cref="RandomOutcomeEntry"/> is a record struct,
    /// so <c>default</c> — and a JSON row that omits <c>effectId</c> — carries a null one; the boss
    /// encounter builder's sibling lookup would then raise a bare <see cref="ArgumentNullException"/>
    /// naming no rule at all (steering S2).
    /// </summary>
    /// <remarks>
    /// 🔒 The weight rule still reads the row, and the negative control below it is
    /// <see cref="A_well_formed_RANDOM_OUTCOME_reports_no_problem"/>: a table whose ids are all present
    /// raises none of this.
    /// </remarks>
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

    /// <summary>`14` §8.0 takes a finite, non-negative weight and nothing else.</summary>
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

    /// <summary>Every row zero: `14` §8.0 has no row to pick, so the roll is dead weight.</summary>
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

    /// <summary>🔒 A row naming the roll itself rolls the roll — unbounded, one draw index per turn.</summary>
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
    /// 🔒 <c>RANDOM_OUTCOME</c> carries no <c>value</c>: its number is the winning row's index, which
    /// nothing authors. Refused rather than ignored — dropping it would ship whichever misreading
    /// put it there (the <c>FORCE_CRIT_NEXT</c> precedent).
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

    // ════════════════════════════════════════════════════ 7 · exhaustiveness and count

    /// <summary>The op is in the vocabulary, in `18` §2.4's family, and routed by the resolver.</summary>
    [Fact]
    public void RANDOM_OUTCOME_is_the_forty_fourth_op_and_a_combat_flow_one()
    {
        // S3 — the floor under the membership assertion below.
        EffectOps.All.Count.ShouldBe(44, "18 §11: '44 ops = 41 + CLEAR_SUMMONS + STAT_COPY + RANDOM_OUTCOME'");

        EffectOps.All.ShouldContain(EffectOp.RANDOM_OUTCOME);
        EffectOps.FamilyOf(EffectOp.RANDOM_OUTCOME).ShouldBe(EffectOpFamily.COMBAT_FLOW);
        EffectOps.IsRunAndBoard(EffectOp.RANDOM_OUTCOME).ShouldBeFalse(
            "a roll happens inside the battle; 18 §2.5's queue is for what the run controller applies");

        ((int)EffectOp.RANDOM_OUTCOME).ShouldBe(
            44, "the numbers are wire values — append, never renumber, never reuse");
    }

    /// <summary>
    /// 🔒 The resolver does not reach its <c>default</c> arm for this op — the arm that would report
    /// <em>"is not one of 18 §2's 44"</em>.
    /// </summary>
    [Fact]
    public void The_resolver_routes_RANDOM_OUTCOME_rather_than_falling_through()
    {
        var bench = new OpTestBench();

        var outcome = EffectOpResolver.Resolve(
            PhaseOne(), bench.Context(Battle(EffectTestBattle.CombatRng(6))));

        outcome.Op.ShouldBe(EffectOp.RANDOM_OUTCOME);
    }

    /// <summary>
    /// 🔒 The op with no draw stream is refused rather than answering with the first row — a stable,
    /// reproducible, wrong "random", exactly as `18` §5's <c>RANDOM_ENEMY</c> is refused.
    /// </summary>
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

    /// <summary>
    /// 🔒 <see cref="EffectOpSeams.Strict"/>'s doctrine holds for the new member too: an unwired flow
    /// sink <b>throws naming M2-08</b> rather than quietly doing nothing.
    /// </summary>
    /// <remarks>
    /// This is the case <c>EffectOpSeamTests.An_unwired_combat_flow_op_names_M2_08</c> excludes while
    /// the handler is a stub — it never reaches a seam at all today. When this goes green that
    /// exclusion comes off.
    /// </remarks>
    [Fact]
    public void The_unwired_flow_sink_refuses_a_RANDOM_OUTCOME_naming_M2_08()
    {
        var context = new EffectOpContext
        {
            Evaluation = Battle(EffectTestBattle.CombatRng(6)),
            Seams = EffectOpSeams.Strict,
        };

        var thrown = Should.Throw<EffectContextException>(
            () => CombatFlowOps.RandomOutcome(PhaseOne(), context));

        thrown.Message.ShouldContain("M2-08", Case.Sensitive);
        thrown.Message.ShouldStartWith(EffectContextException.Marker, Case.Sensitive);
    }

    // ════════════════════════════════════════════════════ fixtures

    /// <summary>`17` §9's phase 1 <em>Roll of Fate</em>: one d6, three equally weighted results.</summary>
    private static EffectDefinition PhaseOne() => Roll(
        "BOSS_DICELORD_ROLL_OF_FATE_P1",
        new RandomOutcomeEntry(BossAtk, 2.0),
        new RandomOutcomeEntry(HeroAtk, 2.0),
        new RandomOutcomeEntry(BothAspd, 2.0));

    /// <summary>
    /// `17` §9's phase 2: the hero-favourable row is gone (kickoff decision 1(c)) and the two that
    /// remain are weighted <c>4/2</c>. Same op, different data, no branch.
    /// </summary>
    private static EffectDefinition PhaseTwo() => Roll(
        "BOSS_DICELORD_ROLL_OF_FATE_P2",
        new RandomOutcomeEntry(BossAtk, 4.0),
        new RandomOutcomeEntry(BothAspd, 2.0));

    /// <summary>A <c>RANDOM_OUTCOME</c> in the shape `17` §9 authors: PERIODIC, on SELF, no value.</summary>
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

    /// <summary>One roll of one table at one seed, asserted as an id and a 1-based index.</summary>
    private static void Draws(
        EffectDefinition roll, ulong battleSeed, string expected, double expectedIndex)
    {
        var bench = new OpTestBench();

        var index = CombatFlowOps.RandomOutcome(
            roll, bench.Context(Battle(EffectTestBattle.CombatRng(battleSeed))));

        bench.RandomOutcomes.Select(o => o.ChosenEffectId).ShouldBe([expected], Case.Sensitive);
        index.ShouldBe(expectedIndex);
    }

    /// <summary>One roll per seed over <c>1..seeds</c>, and the effect id each one chose.</summary>
    /// <remarks>
    /// The loop lives here rather than in a test body: `18` §10's own rule is that a disabled row is
    /// unreachable <em>wherever it sits</em>, and one seed cannot show that.
    /// </remarks>
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
