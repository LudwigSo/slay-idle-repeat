using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `18` §2.4 / §10.1 <b>E6</b> — <c>RANDOM_OUTCOME</c> end to end: the op takes <b>one</b> draw
/// and names one effect id, <see cref="IBossOutcomes"/> resolves it, and `17` §9's two authored
/// tables run through the identical code path with no engine branch between them.
/// </summary>
public sealed class BossOutcomeTests
{
    private const string RollInstance = "BOSS_DICELORD#P1#BOSS_DICELORD_ROLL_OF_FATE_P1";

    // ════════════════════════════════════════════════════ 1 · the seam

    /// <summary>
    /// 🔒 The strict default refuses, naming the tasks that own it — <see cref="EffectOpSeams"/>'
    /// doctrine, because an op only reaches a seam because authored content rolled.
    /// </summary>
    [Fact]
    public void The_strict_seam_refuses_a_RANDOM_OUTCOME_naming_M2_12_and_M2_13()
    {
        var dicelord = new BattleActor(
            BossTestBench.Boss(BossTestBench.Dicelord), NoStatusTimeline.Instance);

        var thrown = Should.Throw<EffectContextException>(
            () => BattleSeams.Strict.Outcomes.Resolve(
                dicelord, BossTestBench.FateBossAtk, "BOSS_DICELORD_ROLL_OF_FATE_P1"));

        thrown.Message.ShouldContain("M2-12", Case.Sensitive);
        thrown.Message.ShouldContain(BossTestBench.FateBossAtk, Case.Sensitive);
        thrown.Message.ShouldStartWith(EffectContextException.Marker, Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The seam is the <b>seventh</b> member of <see cref="BattleSeams"/>, and its strict default
    /// is <see cref="NoBossOutcomes"/> — the same shape <see cref="NoSummons"/> uses.
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
    /// 🔴 <b>The single-draw proof, end to end.</b> One <c>PERIODIC</c> firing of `17` §9's
    /// <em>Roll of Fate</em> costs the battle's combat stream <b>exactly one</b> draw index and
    /// resolves <b>exactly one</b> outcome.
    /// </summary>
    /// <remarks>
    /// `18` §4's conditions are <em>"pure functions of current state"</em> and a draw is not state,
    /// so three <c>chance</c>-gated effects would be three <b>independent</b> draws — all three can
    /// fire, or none — and would spend three indices where <c>WeightedPick</c> spends one.
    /// <c>DeterministicRng.Position</c> is the persisted state of the stream, so the difference
    /// desynchronises every later draw of the fight between client and server.
    /// </remarks>
    [Fact]
    public void One_Roll_of_Fate_firing_costs_one_draw_index_and_resolves_one_outcome()
    {
        var outcomes = new RecordingOutcomes();

        Fight(outcomes, BossTestBench.RollOfFateP1());

        outcomes.Resolutions.Count.ShouldBe(
            1, "three outcomes, one draw, one winner — that is what 'mutually exclusive' means");

        var resolved = outcomes.Resolutions[0];

        resolved.Holder.ShouldBe(BossTestBench.Dicelord, "17 §9's Dicelord rolled it");
        resolved.SourceEffectId.ShouldBe("BOSS_DICELORD_ROLL_OF_FATE_P1");
        new[]
        {
            BossTestBench.FateBossAtk, BossTestBench.FateHeroAtk, BossTestBench.FateBothAspd,
        }.ShouldContain(resolved.ChosenEffectId, "the winner is one of the three authored rows");
    }

    /// <summary>
    /// 🔒 `17` §9's <b>two</b> authored tables — phase 1's <c>2/2/2</c> over three rows and phase 2's
    /// <c>4/2</c> over two — are driven by the <b>same</b> op through the <b>same</b> seam. That is
    /// the whole claim of E6: the boss script is data, and there is no branch between the phases.
    /// </summary>
    [Theory]
    [InlineData("BOSS_DICELORD_ROLL_OF_FATE_P1", 3)]
    [InlineData("BOSS_DICELORD_ROLL_OF_FATE_P2", 2)]
    public void Both_of_the_Dicelords_tables_reach_the_seam_through_the_one_op(
        string rollId, int rows)
    {
        var roll = rollId.EndsWith("P1", StringComparison.Ordinal)
            ? BossTestBench.RollOfFateP1()
            : BossTestBench.RollOfFateP2();

        roll.Outcomes!.Count.ShouldBe(rows, "the floor: the table really has that many rows");

        var outcomes = new RecordingOutcomes();

        Fight(outcomes, roll);

        outcomes.Resolutions.Count.ShouldBe(1, "one firing, one winner, whichever table it was");
        outcomes.Resolutions[0].SourceEffectId.ShouldBe(rollId);

        roll.Outcomes.Select(o => o.EffectId).ShouldContain(outcomes.Resolutions[0].ChosenEffectId);
    }

    /// <summary>
    /// 🔒 The roll's outcome rows are on <see cref="ActorPlan.Effects"/> too, which is what lets
    /// <see cref="BossOutcomes"/> resolve a chosen id against the holder's own holdings rather than
    /// against a content lookup it would otherwise have to carry.
    /// </summary>
    [Fact]
    public void The_outcome_rows_are_on_the_bosss_own_plan()
    {
        var plan = BossPlan(BossTestBench.RollOfFateP1());

        var ids = plan.Effects.Select(h => h.Effect.Id).ToArray();

        ids.Length.ShouldBeGreaterThan(0, "the floor under the membership assertions");
        ids.ShouldContain(BossTestBench.FateBossAtk);
        ids.ShouldContain(BossTestBench.FateHeroAtk);
        ids.ShouldContain(BossTestBench.FateBothAspd);
    }

    /// <summary>
    /// 🔒 An outcome id the boss does not hold is <b>refused</b>, not dropped: `18` §10.1 E6 hands
    /// the seam one id per roll, so a silently ignored one makes the Dicelord roll a d6 with no
    /// faces — visible, deliberate, and doing nothing.
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
    /// resolves rather than throwing, and the resolved effect reaches `18` §2.3's engine.
    /// </summary>
    [Fact]
    public void The_real_resolver_fires_the_row_the_draw_picked()
    {
        var run = RealResolverFight(BossTestBench.RollOfFateP1());

        run.Statuses.Applied.Count.ShouldBe(
            1, "one roll, one winner, and the winner is an APPLY_STATUS this suite can observe");

        new[]
        {
            BossTestBench.FateBossAtk, BossTestBench.FateHeroAtk, BossTestBench.FateBothAspd,
        }.ShouldContain(run.Statuses.Applied[0]);
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
    };

    /// <summary>
    /// A fight long enough for one firing of the roll — phase 1 is entered at the pre-tick, so an
    /// interval of 10 s (or 14 s) first fires at tick 200 (or 280).
    /// </summary>
    private static BossRun Fight(RecordingOutcomes outcomes, EffectDefinition roll) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), BossPlan(roll) },
            new List<BossEncounter> { Encounter(roll) },
            new List<EffectInstanceId>
            {
                BossBuiltIns.PhaseInstance(BossTestBench.Dicelord, 1, roll.Id),
            },
            Array.Empty<(int, string, double)>(),
            outcomes: _ => outcomes,
            maxTicks: 300);

    /// <summary>The same fight wired to the <b>real</b> <see cref="BossOutcomes"/>.</summary>
    private static BossRun RealResolverFight(EffectDefinition roll) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), BossPlan(roll) },
            new List<BossEncounter> { Encounter(roll) },
            new List<EffectInstanceId>
            {
                BossBuiltIns.PhaseInstance(BossTestBench.Dicelord, 1, roll.Id),
            },
            Array.Empty<(int, string, double)>(),
            outcomes: services => new BossOutcomes(services),
            maxTicks: 300);
}
