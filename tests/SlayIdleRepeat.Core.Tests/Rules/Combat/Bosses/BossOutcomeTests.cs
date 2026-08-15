using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// <c>RANDOM_OUTCOME</c> end to end: the op takes one draw and names one effect id,
/// <see cref="IBossOutcomes"/> resolves it, and the Dicelord's two authored tables run through the
/// identical code path with no engine branch between them.
/// </summary>
public sealed class BossOutcomeTests
{
    private const string RollInstance = "BOSS_DICELORD#P1#BOSS_DICELORD_ROLL_OF_FATE_P1";

    /// <summary>
    /// Phase 1 is entered at pre-tick 0c, so the roll is anchored at 0 and its 10 s period first
    /// fires at tick 200.
    /// </summary>
    private const int FiringTick = 200;

    /// <summary>The battle seed, pinned so every "which row won" assertion is computable.</summary>
    private const ulong DefaultSeed = 0xC0FFEE_1234_5678UL;

    // ════════════════════════════════════════════════════ 1 · the seam

    /// <summary>The strict default refuses, naming the tasks that own it, because an op only reaches a seam because authored content rolled.</summary>
    [Fact]
    public void The_strict_seam_refuses_a_RANDOM_OUTCOME_naming_M2_12_and_M2_13()
    {
        var dicelord = new BattleActor(
            BossTestBench.Boss(BossTestBench.Dicelord), NoStatusTimeline.Instance);

        var thrown = Should.Throw<EffectContextException>(
            () => BattleSeams.Strict.Outcomes.Resolve(
                dicelord, BossTestBench.FateBossAtk, "BOSS_DICELORD_ROLL_OF_FATE_P1"));

        thrown.Message.ShouldContain("M2-12", Case.Sensitive, "the engine that owns the seam");
        thrown.Message.ShouldContain(
            "M2-13", Case.Sensitive, "and the task that authors the content which reaches it");
        thrown.Message.ShouldContain(BossTestBench.FateBossAtk, Case.Sensitive);
        thrown.Message.ShouldStartWith(EffectContextException.Marker, Case.Sensitive);
    }

    /// <summary>
    /// The seam is the seventh member of <see cref="BattleSeams"/>, and its strict default is
    /// <see cref="NoBossOutcomes"/> — the same shape <see cref="NoSummons"/> uses.
    /// </summary>
    [Fact]
    public void BattleSeams_carries_the_outcome_seam_alongside_the_other_six()
    {
        BattleSeams.Strict.Outcomes.ShouldBeOfType<NoBossOutcomes>();

        typeof(BattleSeams).GetProperties().Select(p => p.Name).ShouldBe(
            new[] { "Attack", "Statuses", "Timeline", "Phases", "Summons", "Pets", "Outcomes" },
            ignoreOrder: true,
            "🔒 seven seams — the tick loop's whole face to its extensions");
    }

    // ════════════════════════════════════════════════════ 2 · end to end, through the tick loop

    /// <summary>
    /// The single-draw proof, end to end. One <c>PERIODIC</c> firing of the Dicelord's <em>Roll of
    /// Fate</em> costs the combat stream exactly one draw index and resolves exactly one outcome.
    /// </summary>
    /// <remarks>
    /// Three <c>chance</c>-gated effects would be three <b>independent</b> draws — all three can fire, or
    /// none — spending three indices where <c>WeightedPick</c> spends one. The position is persisted
    /// state, so the difference desynchronises every later draw of the fight between client and server.
    /// </remarks>
    [Fact]
    public void One_Roll_of_Fate_firing_costs_one_draw_index_and_resolves_one_outcome()
    {
        var outcomes = new RecordingOutcomes();

        var run = Fight(outcomes, BossTestBench.RollOfFateP1());

        outcomes.Resolutions.Count.ShouldBe(
            1, "three outcomes, one draw, one winner — that is what 'mutually exclusive' means");

        var resolved = outcomes.Resolutions[0];

        resolved.Holder.ShouldBe(BossTestBench.Dicelord, "17 §9's Dicelord rolled it");
        resolved.SourceEffectId.ShouldBe("BOSS_DICELORD_ROLL_OF_FATE_P1");
        resolved.ChosenEffectId.ShouldBe(
            BossTestBench.FateBossAtk,
            "🔒 the LITERAL row the pinned seed drew, not 'one of the three': battleSeed " +
            "0xC0FFEE12345678 on 14 §8.1's combat stream draws unit 0.2902859852621159, and over " +
            "17 §9's 2/2/2 table (total 6) the threshold 1.741 is first exceeded by the first row's " +
            "cumulative 2. An assertion that accepted any of the three would pass on an op that " +
            "always answered with row 1");

        // The single-draw proof, which is what this case is named for. Without it the name
        // promises an assertion the body never made: three chance-gated effects would resolve
        // one winner just as often as this does, while spending three indices.
        run.Driver.RngPositionAt(FiringTick).ShouldBe(
            0UL,
            "the control: nothing in this fight has drawn before the roll, so the roll IS combat " +
            "draw 0 — which is what makes the literal row above computable from the seed alone");

        run.Driver.RngPositionAt(FiringTick + 1).ShouldBe(
            1UL,
            "14 §8.0's WeightedPick is ONE draw. Position is the persisted state of the stream, so " +
            "three would desynchronise every later draw of the battle between client and server");

        run.Driver.RngPositionAt(FiringTick + 50).ShouldBe(
            1UL, "and nothing else in the fight draws either — the roll is the only spender");
    }

    /// <summary>
    /// The negative control for the single-draw proof: on a tick with no firing the stream advances
    /// by zero. Without it, "the position was 1 after the roll" is equally consistent with a stream
    /// that advances once per tick regardless.
    /// </summary>
    [Fact]
    public void A_tick_with_no_roll_advances_the_draw_stream_by_nothing()
    {
        var run = Fight(new RecordingOutcomes(), BossTestBench.RollOfFateP1());

        run.Driver.RngPositionAt(FiringTick - 1).ShouldBe(run.Driver.RngPositionAt(FiringTick));
        run.Driver.RngPositionAt(FiringTick + 2).ShouldBe(run.Driver.RngPositionAt(FiringTick + 1));
    }

    /// <summary>
    /// The same fight on a different battle seed reaches a different row, which is what proves the
    /// case above pinned a draw and not a row the op always answers with.
    /// </summary>
    [Fact]
    public void A_different_battle_seed_over_the_same_table_reaches_a_different_row()
    {
        var outcomes = new RecordingOutcomes();

        Fight(outcomes, BossTestBench.RollOfFateP1(), battleSeed: 5UL);

        outcomes.Resolutions.Count.ShouldBe(1, "the floor: the roll still fired exactly once");
        outcomes.Resolutions[0].ChosenEffectId.ShouldBe(
            BossTestBench.FateBothAspd,
            "battleSeed 5 draws unit 0.7877096435394318; over 2/2/2 the threshold 4.726 is first " +
            "exceeded by the THIRD row's cumulative 6");
    }

    /// <summary>
    /// The Dicelord's two authored tables are driven by the same op through the same seam: the boss
    /// script is data, and there is no branch between the phases.
    /// </summary>
    /// <remarks>
    /// Both rows run on the same battle seed and the winners differ: seed 1 draws unit 0.4852, and
    /// over phase 1's 2/2/2 the threshold falls to the second row while over phase 2's 4/2 the same
    /// threshold falls to the first. One draw, one walk, two data shapes, two answers — where an
    /// assertion of the form "the winner is in the table" was true of every implementation.
    /// </remarks>
    [Theory]
    [InlineData("BOSS_DICELORD_ROLL_OF_FATE_P1", 3, BossTestBench.FateHeroAtk)]
    [InlineData("BOSS_DICELORD_ROLL_OF_FATE_P2", 2, BossTestBench.FateBossAtk)]
    public void Both_of_the_Dicelords_tables_reach_the_seam_through_the_one_op(
        string rollId, int rows, string expected)
    {
        var roll = rollId.EndsWith("P1", StringComparison.Ordinal)
            ? BossTestBench.RollOfFateP1()
            : BossTestBench.RollOfFateP2();

        roll.Outcomes!.Count.ShouldBe(rows, "the floor: the table really has that many rows");

        var outcomes = new RecordingOutcomes();

        Fight(outcomes, roll, battleSeed: 1UL);

        outcomes.Resolutions.Count.ShouldBe(1, "one firing, one winner, whichever table it was");
        outcomes.Resolutions[0].SourceEffectId.ShouldBe(rollId);

        outcomes.Resolutions[0].ChosenEffectId.ShouldBe(
            expected,
            "the SAME seed over the two authored tables, and the tables disagree — which is what " +
            "'no engine branch between them' means");
    }

    // A case asserting "the outcome rows are on the boss's own plan" against a plan this file
    // built could not fail: the fixture put the rows on, and the assertion read them back. That
    // claim is made instead where production decides it —
    // BossEncounterBuilderTests.Outcome_rows_that_are_siblings_of_the_same_script_are_accepted_
    // and_land_on_the_plan, which builds through the real BossEncounterBuilder.

    /// <summary>
    /// An outcome id the boss does not hold is refused, not dropped: the seam is handed one id per
    /// roll, so a silently ignored one makes the Dicelord roll a d6 with no faces — visible,
    /// deliberate, and doing nothing.
    /// </summary>
    [Fact]
    public void An_outcome_id_the_boss_does_not_hold_is_refused()
    {
        // Both rows name effects that are NOT on the plan, so whichever the draw picks is a miss.
        var typo = BossTestBench.RollOfFateP1() with
        {
            Outcomes = new[]
            {
                new RandomOutcomeEntry("BOSS_DICELORD_FATE_TYPO_A", 1.0),
                new RandomOutcomeEntry("BOSS_DICELORD_FATE_TYPO_B", 1.0),
            },
        };

        var thrown = Should.Throw<EffectContextException>(() => RealResolverFight(typo));

        thrown.Message.ShouldContain("BOSS_DICELORD_FATE_TYPO", Case.Sensitive);
        thrown.Message.ShouldContain(BossTestBench.Dicelord, Case.Sensitive);
    }

    /// <summary>
    /// The positive control for the case above: the same fight with the authored rows on the plan
    /// resolves rather than throwing, and the resolved effect reaches the status engine.
    /// </summary>
    /// <remarks>
    /// The winner is pinned as a literal at a pinned seed, and the pair of rows below is what makes
    /// it a draw rather than a constant. An assertion of the form "the applied id is one of the
    /// three" stood here and could not fail: an implementation that always fired the first row
    /// satisfied it exactly.
    /// </remarks>
    [Theory]
    [InlineData(DefaultSeed, BossTestBench.FateBossAtk, "unit 0.2902859852621159 → row 1")]
    [InlineData(5UL, BossTestBench.FateBothAspd, "unit 0.7877096435394318 → row 3")]
    public void The_real_resolver_fires_the_row_the_draw_picked(
        ulong battleSeed, string expected, string arithmetic)
    {
        var run = RealResolverFight(BossTestBench.RollOfFateP1(), battleSeed);

        run.Statuses.Applied.Count.ShouldBe(
            1, "one roll, one winner, and the winner is an APPLY_STATUS this suite can observe");

        run.Statuses.Applied[0].ShouldBe(expected, arithmetic);
    }

    // ════════════════════════════════════════════════════ 3 · determinism

    /// <summary>
    /// The boss engine adds a draw and two event kinds to the fight, so the fight has to replay:
    /// the same battle seed over the same roster produces byte-identical logs, the same
    /// <c>LogHash</c>, and the same drawn row.
    /// </summary>
    /// <remarks>
    /// <c>LogHash</c> is the determinism gate and anti-tamper check, so this is the assertion the
    /// whole feature's determinism claim reduces to. The floors below are what stop it passing on
    /// two empty logs — a fight that threw its hands up twice hashes identically too.
    /// </remarks>
    [Fact]
    public void The_same_battle_seed_replays_a_boss_fight_to_the_same_LogHash()
    {
        var first = RealResolverFight(BossTestBench.RollOfFateP1());
        var second = RealResolverFight(BossTestBench.RollOfFateP1());

        BossTestBench.PhaseChanges(first.Result.Log).Count.ShouldBe(
            1, "the floor: there IS a boss fight with a phase entry in it");
        first.Statuses.Applied.Count.ShouldBe(1, "and a roll that resolved");

        second.Result.LogHash.ShouldBe(first.Result.LogHash);
        second.Statuses.Applied.ShouldBe(first.Statuses.Applied, Case.Sensitive);
        second.Result.Log.Select(e => (e.Tick, e.Type, e.SourceId, e.TargetId, e.Value, e.DataId))
              .ShouldBe(first.Result.Log.Select(
                  e => (e.Tick, e.Type, e.SourceId, e.TargetId, e.Value, e.DataId)));
    }

    // ════════════════════════════════════════════════════ fixtures

    private static ActorPlan BossPlan(EffectDefinition roll) =>
        BossTestBench.Boss(
            BossTestBench.Dicelord,
            maxHp: 1000.0,
            BossTestBench.InPhase(BossTestBench.Dicelord, 1, roll),
            BossTestBench.InPhase(
                BossTestBench.Dicelord, 1, BossTestBench.FateOutcome(BossTestBench.FateBossAtk)),
            BossTestBench.InPhase(
                BossTestBench.Dicelord, 1, BossTestBench.FateOutcome(BossTestBench.FateHeroAtk)),
            BossTestBench.InPhase(
                BossTestBench.Dicelord, 1, BossTestBench.FateOutcome(BossTestBench.FateBothAspd)),
            BossTestBench.BuiltIn(BossTestBench.Dicelord, BossBuiltIns.Enrage));

    private static BossEncounter Encounter(EffectDefinition roll) => new()
    {
        BossId = BossTestBench.Dicelord,
        Plan = BossPlan(roll),
        FirstClear = false,
        Phase2HpFraction = 0.66,
        Phase3HpFraction = 0.33,
        PhaseOfInstance = new Dictionary<EffectInstanceId, int>
        {
            [BossBuiltIns.PhaseInstance(BossTestBench.Dicelord, 1, roll.Id)] = 1,
        },
        LeadSecondsOfInstance = new Dictionary<EffectInstanceId, double>(),

        // No mechanic authors a wind-up here, so nothing is announced.
        AnnouncingOfPhase = new Dictionary<int, IReadOnlyList<EffectInstanceId>>(),
    };

    /// <summary>
    /// A fight long enough for one firing of the roll — phase 1 is entered at the pre-tick, so an
    /// interval of 10 s (or 14 s) first fires at tick 200 (or 280).
    /// </summary>
    private static BossRun Fight(
        RecordingOutcomes outcomes, EffectDefinition roll, ulong battleSeed = DefaultSeed) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), BossPlan(roll) },
            new List<BossEncounter> { Encounter(roll) },
            new List<EffectInstanceId>
            {
                BossBuiltIns.PhaseInstance(BossTestBench.Dicelord, 1, roll.Id),
            },
            Array.Empty<(int, string, double)>(),
            outcomes: _ => outcomes,
            maxTicks: 300,
            battleSeed: battleSeed);

    /// <summary>The same fight wired to the <b>real</b> <see cref="BossOutcomes"/>.</summary>
    private static BossRun RealResolverFight(EffectDefinition roll, ulong battleSeed = DefaultSeed) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), BossPlan(roll) },
            new List<BossEncounter> { Encounter(roll) },
            new List<EffectInstanceId>
            {
                BossBuiltIns.PhaseInstance(BossTestBench.Dicelord, 1, roll.Id),
            },
            Array.Empty<(int, string, double)>(),
            outcomes: services => new BossOutcomes(services),
            maxTicks: 300,
            battleSeed: battleSeed);
}
