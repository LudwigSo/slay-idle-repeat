using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 M2-13's authored data driven through a real fight — the two claims that cannot be made from the
/// script alone: a phase block ends when its phase does, and `17` §9's <em>Roll of Fate</em> is one
/// draw.
/// </summary>
/// <remarks>
/// Every fight here is built from the shipped <c>content/bosses/bosses.json</c>, read off disk
/// through <see cref="BossCatalogue"/> (see <see cref="ShippedBosses"/>), so what is under test is
/// the shipped content and not a fixture shaped to agree with it.
/// </remarks>
public sealed class AuthoredBossFightTests
{
    /// <summary>
    /// 🔒 `18` §6 / R3 — a phase block's mechanic is live inside its phase and deactivated at the
    /// exit, and the built-in enrage is reached by neither transition.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b><c>SYS_ENRAGE</c> is the negative control, and the case is worthless without it.</b>
    /// "Bog Air is inactive after the phase-3 entry" is equally consistent with the fight having
    /// ended, the registry having dropped everything, or the sampler reading nothing at all. The
    /// built-in is registered by the same pass, sampled by the same sampler, on the same ticks — and
    /// it must still be <b>active</b>, because `17` §11 implements the enrage once for every boss and
    /// <see cref="BossEncounter.PhaseOfInstance"/> deliberately does not contain it.
    /// </para>
    /// <para>
    /// 🔴 The reading is <c>IsActive</c> rather than "did it fire": <c>TriggerRegistry.Activate</c> is
    /// a no-op on a live instance, so a transition that forgot to deactivate produces a fight in
    /// which everything still fires and an "it fired" assertion passes either way.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_authored_phase_mechanic_is_deactivated_at_its_phase_exit_and_the_enrage_is_not()
    {
        var gulgrot = ShippedBosses.Catalogue.Of("BOSS_GULGROT");
        var encounter = BossEncounterBuilder.Build(
            BossTestBench.Request(gulgrot.Script, gulgrot.Effects));

        // 17 §3's Bog Air — the AURA whose duration R3 makes PHASE-scoped.
        var bogAir = BossBuiltIns.PhaseInstance(gulgrot.Script.Id, 2, "BOSS_GULGROT_P2_BOG_AIR");
        var enrage = BossBuiltIns.BuiltInInstance(gulgrot.Script.Id, BossBuiltIns.EnrageId);

        var run = BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), encounter.Plan },
            new List<BossEncounter> { encounter },
            new List<EffectInstanceId> { bogAir, enrage },
            new List<(int, string, double)>
            {
                (10, gulgrot.Script.Id, 0.60),  // into phase 2
                (30, gulgrot.Script.Id, 0.20),  // into phase 3
            });

        bool ActiveAt(int tick, EffectInstanceId instance) =>
            run.Driver.Samples.Single(s => s.Tick == tick && s.Instance == instance.Value).IsActive;

        ActiveAt(20, bogAir).ShouldBeTrue(
            "the floor: the phase-2 block really was activated at the phase-2 entry (R8's anchor)");
        ActiveAt(20, enrage).ShouldBeTrue("SYS_ENRAGE is active from pre-tick 0a for the whole fight");

        ActiveAt(40, bogAir).ShouldBeFalse(
            "18 §6 — the phase-2 block ends when the boss exits phase 2, which is what makes a boss " +
            "AURA a PHASE scope rather than a 999-second BATTLE one");
        ActiveAt(40, enrage).ShouldBeTrue(
            "17 §11 implements the enrage ONCE for every boss, so no phase transition may reach it — " +
            "one that did would re-anchor its R8 clock and move the enrage from 70 s of battle to " +
            "70 s after 66% HP");
    }

    /// <summary>
    /// 🔒 `18` §10.1 E6 — <em>"exactly one draw index per roll"</em>: the authored Dicelord's Roll of
    /// Fate resolves one outcome per firing, not three independent ones.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The count is the whole point.</b> `18` §10.1 E6 records that three <c>chance</c>-gated
    /// effects would be three <em>independent</em> draws — all three can fire, or none — which is
    /// neither mutual exclusion nor one d6. So the assertion is not "an outcome fired" but "the
    /// number of outcomes resolved equals the number of rolls", and every resolved id is one of the
    /// three rows the authored table declares.
    /// </remarks>
    [Fact]
    public void The_authored_Roll_of_Fate_resolves_exactly_one_outcome_per_roll()
    {
        var dicelord = ShippedBosses.Catalogue.Of("BOSS_DICELORD");
        var encounter = BossEncounterBuilder.Build(
            BossTestBench.Request(dicelord.Script, dicelord.Effects));

        var roll = dicelord.Effects["BOSS_DICELORD_P1_ROLL_OF_FATE"];
        var rows = roll.Outcomes!.Select(r => r.EffectId).ToArray();
        var interval = roll.Trigger!.Interval!.Value;

        var recorder = new AuthoredOutcomeRecorder();

        // 17 §9's phase 1 lasts the whole fight here: no HP script, so the boss never leaves it and
        // the only thing that can resolve an outcome is the phase-1 Roll of Fate.
        //
        // 🔴 700 ticks, not the bench's default 200, and the S3 floor below is what said so: the
        // authored period is 10 s, which is 200 ticks, so a 200-tick fight rolls the die exactly
        // ZERO times and every assertion about "one draw per roll" would have been vacuously true
        // over an empty list. 35 s gives three rolls.
        var maxTicks = 700;

        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), encounter.Plan },
            new List<BossEncounter> { encounter },
            new List<EffectInstanceId>(),
            new List<(int, string, double)>(),
            outcomes: _ => recorder,
            maxTicks: maxTicks);

        // R8: an absent startDelay is one interval, so the schedule is anchor + k x interval for
        // k = 1, 2, ... within the fight's bound.
        var expected = (maxTicks - 1) / (int)(interval * BossTestBench.TicksPerSecond);

        expected.ShouldBeGreaterThanOrEqualTo(1, "the floor: the fight has to be long enough to roll");

        recorder.Resolved.Count.ShouldBe(
            expected,
            "18 §10.1 E6 — ONE draw per roll. Three chance-gated effects would be three independent " +
            "draws, which is neither mutual exclusion nor one d6");

        foreach (var resolved in recorder.Resolved)
        {
            rows.ShouldContain(resolved, "every resolved id is a row of the authored table");
        }
    }

    /// <summary>
    /// Counts what <c>RANDOM_OUTCOME</c> handed the outcome seam. It records rather than resolves:
    /// the subject is <b>how many</b> ids one roll produces, and firing them would drag `05` §4 into
    /// a case about `14` §8.0's draw count.
    /// </summary>
    /// <remarks>
    /// ⚠️ Named <c>AuthoredOutcomeRecorder</c> rather than <c>RecordingOutcomes</c>: the bench already
    /// declares a namespace-scope <c>RecordingOutcomes</c>, and a nested type of the same name in the
    /// same namespace compiles while shadowing it — which is a trap for the next reader rather than a
    /// defect for this one.
    /// </remarks>
    private sealed class AuthoredOutcomeRecorder : IBossOutcomes
    {
        internal List<string> Resolved { get; } = new();

        public void Resolve(BattleActor holder, string chosenEffectId, string sourceEffectId) =>
            Resolved.Add(chosenEffectId);
    }
}
