using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Status;

/// <summary>The DoT/HoT cadence and stat-debuff rules, driven through a real fight.</summary>
/// <remarks>
/// Through <c>BattleSimulation</c> rather than the timeline directly, because half of what's being
/// tested is <em>which slot</em> the work happens in — "deals that tick first, then expires" is
/// bought entirely by slot 1 running before slot 2.
/// </remarks>
public sealed class StatusTimelineTests
{
    /// <summary>Reapplication adds a stack and never re-anchors the cadence.</summary>
    /// <remarks>
    /// Applied tick 7, reapplied tick 17: anchored at 7 the boundaries are 27/47/67, re-anchoring
    /// moves them to 37/57/77. At 0 and 20 both readings give 20/40/60 and the test could not fail.
    /// </remarks>
    [Fact]
    public void Reapplication_adds_a_stack_and_never_re_anchors_the_cadence()
    {
        var bench = Fight(new[]
        {
            A(7, "E_BURN", "BURN", 0.5, 10.0),
            A(17, "E_BURN", "BURN", 0.5, 10.0),
        });

        var ticks = bench.Pipeline.Dots.Select(d => d.Tick).ToArray();

        ticks.ShouldBe(new[] { 27, 47, 67, 87, 107, 127, 147, 167, 187 });

        // The negative control: the re-anchored reading's own boundaries are absent.
        ticks.ShouldNotContain(37);
        ticks.ShouldNotContain(57);
    }

    /// <summary>The per-tick amount is per-second potency times the stack count read at the moment the tick lands.</summary>
    /// <remarks>
    /// The second stack lands at tick 30 — after the first boundary, before the second — so the tick
    /// at 47 must be 10 rather than 5. Resolving the amount at application time, or caching it at the
    /// first boundary, deals 5 both times and is invisible in any fight where stacks do not change
    /// between application and boundary.
    /// </remarks>
    [Fact]
    public void The_stack_count_is_read_at_the_moment_the_tick_lands()
    {
        var bench = Fight(new[]
        {
            A(7, "E_BURN", "BURN", 0.5, 10.0),
            A(30, "E_BURN", "BURN", 0.5, 10.0),
        });

        var first = bench.Pipeline.Dots.Where(d => d.Tick == 27).ToArray();
        var second = bench.Pipeline.Dots.Where(d => d.Tick == 47).ToArray();

        first.Length.ShouldBe(1);
        second.Length.ShouldBe(1);

        first[0].Amount.ShouldBe(5.0);
        second[0].Amount.ShouldBe(10.0, "the second stack landed at tick 30, before this boundary");
    }

    /// <summary>A DoT expiring exactly on a cadence boundary deals that tick first, then expires.</summary>
    /// <remarks>
    /// The duration makes the two collide exactly at tick 27. Expiring in slot 1, or ordering expiry
    /// before the cadence, deals zero — a whole second of a one-second DoT.
    /// </remarks>
    [Fact]
    public void A_DoT_expiring_on_a_cadence_boundary_deals_that_tick_first_then_expires()
    {
        var bench = Fight(new[] { A(7, "E_BURN", "BURN", 0.5, 10.0, 1.0) });

        var dots = bench.Pipeline.Dots;

        dots.Count.ShouldBe(1, "one boundary falls inside a 1 s duration anchored at tick 7");
        dots[0].Tick.ShouldBe(27);
        dots[0].Amount.ShouldBe(5.0);

        // And it did expire — the second boundary must not arrive.
        dots.ShouldNotContain(d => d.Tick == 47);

        var expiries = bench.Result.Log.Where(e => e.Type == CombatEventType.StatusExpired).ToArray();
        expiries.Length.ShouldBe(1);
        expiries[0].Tick.ShouldBe(27, "slot 2 expires it on the same tick slot 1 ticked it");
    }

    /// <summary>A DoT tick is a damage event, not an attack, so it never routes through <c>ResolveAttack</c>.</summary>
    /// <remarks>
    /// Asserted on <em>which</em> pipeline member was called: checking only that the target lost HP
    /// would pass an implementation that dodged, crit, blocked and floored the tick.
    /// </remarks>
    [Fact]
    public void A_DoT_tick_routes_through_the_non_attack_damage_path_and_wards_absorb_it()
    {
        var bench = Fight(new[] { A(7, "E_BURN", "BURN", 0.5, 10.0) });

        bench.Pipeline.Dots.ShouldNotBeEmpty();
        bench.Pipeline.Dots.ShouldAllBe(d => !d.BypassesWards);
        bench.Pipeline.Heals.ShouldBeEmpty();
    }

    /// <summary>HoT ticks route through <c>Heal()</c>, so <c>HEAL%</c> applies and <c>ON_HEAL</c> fires.</summary>
    /// <remarks>
    /// The amount is asserted as well as the route: a HoT reaching <c>Heal</c> with the raw fraction
    /// would heal 0.1 HP a second and look like a routing success.
    /// </remarks>
    [Fact]
    public void A_HoT_tick_routes_through_Heal_and_never_through_the_damage_path()
    {
        var bench = Fight(new[] { A(7, "E_REGEN", "REGEN", 0.1, 10.0) }, heroMaxHp: 200.0, targetSelf: true);

        var heals = bench.Pipeline.Heals.Where(h => h.Tick == 27).ToArray();

        heals.Length.ShouldBe(1);
        heals[0].Amount.ShouldBe(20.0);
        heals[0].Target.ShouldBe("HERO");

        bench.Pipeline.Dots.ShouldBeEmpty("a HoT is not a damage event");
    }

    /// <summary><c>POISON</c> is X% of target Max HP, <c>BURN</c> X% of attacker ATK.</summary>
    /// <remarks>
    /// Same authored X, same applier, same target, different numbers — so this tests the basis rather
    /// than the arithmetic. One basis for both agrees with one row and fails the other.
    /// </remarks>
    [Fact]
    public void BURN_reads_the_appliers_ATK_and_POISON_reads_the_targets_Max_HP()
    {
        var burn = Fight(new[] { A(7, "E_BURN", "BURN", 0.5, 40.0) }, targetMaxHp: 500.0);
        var poison = Fight(new[] { A(7, "E_POISON", "POISON", 0.5, 40.0) }, targetMaxHp: 500.0);

        burn.Pipeline.Dots[0].Amount.ShouldBe(20.0, "0.5 x the applier's 40 ATK");
        poison.Pipeline.Dots[0].Amount.ShouldBe(250.0, "0.5 x the target's 500 Max HP");
    }

    /// <summary><c>BLEED</c>'s tick is the flat amount times (1 + target's missing-HP fraction).</summary>
    /// <remarks>
    /// At full health the fraction is 0 and the tick is the flat amount, which ignoring the term also
    /// produces — the wounded row is the discriminating one. The coefficient is read from the
    /// catalogue, so the test states the rule and the data states the number.
    /// </remarks>
    [Fact]
    public void A_BLEED_tick_scales_with_the_targets_missing_HP()
    {
        var healthy = Fight(new[] { A(7, "E_BLEED", "BLEED", 0.5, 20.0) }, targetMaxHp: 100.0);
        var wounded = Fight(new[] { A(7, "E_BLEED", "BLEED", 0.5, 20.0) }, targetMaxHp: 100.0, targetHp: 25.0);

        healthy.Pipeline.Dots[0].Amount.ShouldBe(10.0, "0.5 x 20 ATK, at full health");

        // 75% missing, coefficient 1.0 => x1.75.
        wounded.Pipeline.Dots[0].Amount.ShouldBe(17.5);
    }

    /// <summary>Stat debuffs reach the stat aggregation — a <c>FREEZE</c> really is -50% ASPD.</summary>
    /// <remarks>
    /// Asserted on the actor's aggregated ASPD, not the timeline's bookkeeping: a status recorded
    /// correctly and never collected by step 1 does nothing, and every internals test still passes.
    /// </remarks>
    [Fact]
    public void A_FREEZE_halves_the_targets_ASPD_through_the_18_section_8_aggregation()
    {
        var bench = Fight(new[] { A(7, "E_FREEZE", "FREEZE", 0.0, 10.0) }, targetAspd: 2.0);

        var enemy = bench.Simulation.Actors.Single(a => a.Id == "ENEMY_0");

        enemy.Stats[StatId.ASPD].ShouldBe(1.0, "05 §5's FREEZE is -50% ASPD");
    }

    /// <summary>A stat debuff stops applying once it expires — the aggregation is not a one-way write.</summary>
    /// <remarks>
    /// The control an implementation writing into <c>CombatFlowState</c>'s percent buckets would fail:
    /// those carry no duration, so a <c>FREEZE</c> there halves ASPD for the rest of the fight.
    /// </remarks>
    [Fact]
    public void A_stat_debuff_stops_applying_once_it_expires()
    {
        var bench = Fight(new[] { A(7, "E_FREEZE", "FREEZE", 0.0, 10.0, 1.0) }, targetAspd: 2.0, ticks: 60);

        var enemy = bench.Simulation.Actors.Single(a => a.Id == "ENEMY_0");

        enemy.Stats[StatId.ASPD].ShouldBe(2.0, "the FREEZE expired at tick 27");
    }

    /// <summary>A stat debuff that expires and is applied again debuffs again.</summary>
    /// <remarks>
    /// The path <c>StatModifiers</c>' fast-out could break, and which neither test above covers: it
    /// reads a per-actor count rather than walking the set. A count that failed to decrement on expiry
    /// keeps aggregating a dead <c>FREEZE</c>; one that failed to increment silently stops aggregating
    /// a live one — and apply-only and expire-only cases each pass either way. Apply → expire → apply,
    /// asserted at all three points.
    /// </remarks>
    [Fact]
    public void A_stat_debuff_reapplied_after_it_expired_debuffs_again()
    {
        var bench = Fight(
            new[]
            {
                A(7, "E_FREEZE", "FREEZE", 0.0, 10.0, 1.0),
                A(60, "E_FREEZE", "FREEZE", 0.0, 10.0, 1.0),
            },
            targetAspd: 2.0,
            ticks: 75);

        var enemy = bench.Simulation.Actors.Single(a => a.Id == "ENEMY_0");

        // Applied at 7, expired at 27, applied again at 60 and still live at tick 74.
        enemy.Stats[StatId.ASPD].ShouldBe(1.0);

        var events = bench.Result.Log
            .Where(e => e.Type is CombatEventType.StatusApplied or CombatEventType.StatusExpired)
            .Select(e => (e.Tick, e.Type))
            .ToArray();

        events.ShouldBe(new[]
        {
            (7, CombatEventType.StatusApplied),
            (27, CombatEventType.StatusExpired),
            (60, CombatEventType.StatusApplied),
        });
    }

    /// <summary><c>SUNDER</c> stacks to 5, and the aggregate is the sum of the stacks.</summary>
    /// <remarks>
    /// Six applications, ceiling of five: the surplus is dropped rather than evicting an existing
    /// stack. Ignoring the ceiling gives -30%, never stacking gives -5%, and only 5 x -5% is right.
    /// </remarks>
    [Fact]
    public void SUNDER_stacks_to_five_and_the_surplus_application_is_dropped()
    {
        var applications = Enumerable.Range(0, 6)
            .Select(i => A(5 + i, "E_SUNDER", "SUNDER", -0.05, 10.0))
            .ToArray();

        var bench = Fight(applications);
        var enemy = bench.Simulation.Actors.Single(a => a.Id == "ENEMY_0");

        bench.Timeline.StacksOn(enemy, "SUNDER").ShouldBe(5);

        // DEF 100, five stacks of -5% => -25%.
        enemy.Stats[StatId.DEF].ShouldBe(75.0);
    }

    /// <summary>
    /// <c>WARD</c> is deferred wholly to the ward pool — applying it grants a ward segment and
    /// creates no second pool here.
    /// </summary>
    [Fact]
    public void Applying_WARD_grants_a_ward_segment_rather_than_a_timeline_instance()
    {
        var bench = Fight(new[] { A(7, "E_WARD", "WARD", 50.0, 10.0) });
        var enemy = bench.Simulation.Actors.Single(a => a.Id == "ENEMY_0");

        bench.Pipeline.Wards.Count.ShouldBe(1);
        bench.Pipeline.Wards[0].Amount.ShouldBe(50.0);

        bench.Timeline.StacksOn(enemy, "WARD").ShouldBe(0,
            "05 §4.1's pool is M2-09's and this timeline keeps no second copy of it");
    }

    /// <summary>One instance per statusId per target: two appliers share one instance and one cadence.</summary>
    /// <remarks>
    /// Two distinct effect ids apply <c>BURN</c> ten ticks apart. Separate instances would give two
    /// anchors and two boundary series; the section says one, anchored on the first.
    /// </remarks>
    [Fact]
    public void Two_appliers_of_one_status_share_one_instance_and_one_anchor()
    {
        var bench = Fight(new[]
        {
            A(7, "E_ONE", "BURN", 0.5, 10.0),
            A(17, "E_TWO", "BURN", 0.5, 10.0),
        });

        bench.Pipeline.Dots.Select(d => d.Tick)
            .ShouldBe(new[] { 27, 47, 67, 87, 107, 127, 147, 167, 187 });
    }

    /// <summary>One phase check and one <c>ON_LOW_HP</c> per DoT HP change, and no more.</summary>
    /// <remarks>
    /// Exactly one, not at least one: <c>ON_LOW_HP</c> is a crossing, so a doubled observation is a
    /// doubled firing.
    /// </remarks>
    [Fact]
    public void Each_DoT_HP_change_runs_the_phase_check_and_ON_LOW_HP_exactly_once()
    {
        var bench = Fight(new[] { A(7, "E_BURN", "BURN", 0.5, 10.0) }, ticks: 60);

        bench.Pipeline.Dots.Count.ShouldBe(2, "boundaries at ticks 27 and 47");

        // Counted on the phase controller, not the pipeline double — a pipeline-side counter would
        // be blind to a timeline that calls AfterHpDecrease twice for one HP change.
        bench.Phases.HpDecreaseCalls.ShouldBe(2);
    }

    /// <summary>A DoT tick emits <c>StatusTick</c> naming the status, so the replayer can tell it from a hit.</summary>
    [Fact]
    public void A_cadence_tick_emits_StatusTick_naming_the_status()
    {
        var bench = Fight(new[] { A(7, "E_BURN", "BURN", 0.5, 10.0) }, ticks: 40);

        var ticks = bench.Result.Log.Where(e => e.Type == CombatEventType.StatusTick).ToArray();

        ticks.Length.ShouldBe(1);
        ticks[0].Tick.ShouldBe(27);
        ticks[0].Value.ShouldBe(5.0);
        ticks[0].DataId.ShouldBe(StatusLogId.Of("BURN"));
    }

    /// <summary><c>STUN</c> gates swings through the tick loop's "and not stunned" check.</summary>
    /// <remarks>
    /// Asserted on the swing output, not on <c>CanAct</c> at the last tick — that only states the
    /// stun ended, and an engine never consulting the gate would still pass it.
    /// </remarks>
    [Fact]
    public void A_stunned_actor_does_not_swing_while_the_stun_lasts()
    {
        var bench = Fight(new[] { A(0, "E_STUN", "STUN", 0.0, 10.0, 4.0) }, ticks: 60);

        var swings = bench.Pipeline.Swings
            .Where(s => s.Attacker == "ENEMY_0")
            .Select(s => s.Tick)
            .ToArray();

        swings.ShouldBe(new[] { 30, 50 }, "the capped stun holds ticks 0..29");

        var enemy = bench.Simulation.Actors.Single(a => a.Id == "ENEMY_0");
        bench.Timeline.CanAct(enemy).ShouldBeTrue("the capped 1.5 s stun ended at tick 29");
    }

    // ══════════════════════════════════════════════════════════════════ the bench

    private sealed record Bench(
        SimulationResult Result, BattleSimulation Simulation, StatusTimeline Timeline,
        RecordingStatusPipeline Pipeline, RecordingStatusPipeline.CountingPhases Phases);

    /// <summary>One scripted application: what lands, on whom, when, and for how long.</summary>
    private sealed record Applied(
        int Tick, string EffectId, string StatusId, double Value, double ApplierAtk, double? Seconds = null);

    private static Applied A(
        int tick, string effectId, string statusId, double value, double applierAtk, double? seconds = null) =>
        new(tick, effectId, statusId, value, applierAtk, seconds);

    /// <summary>Runs a fight in which a scripted list of applications lands at named ticks.</summary>
    /// <remarks>
    /// Driven by a <see cref="ScriptedApplications"/> decorator rather than authored <c>PERIODIC</c>
    /// effects: the subject is the cadence, and routing through the trigger engine would make a
    /// trigger defect look like a cadence defect.
    /// </remarks>
    private static Bench Fight(
        Applied[] applications,
        double heroMaxHp = 100_000.0,
        double targetMaxHp = 100_000.0,
        double? targetHp = null,
        double targetAspd = 1.0,
        double targetDef = 100.0,
        int ticks = 200,
        bool targetSelf = false)
    {
        StatusTimeline? timeline = null;
        RecordingStatusPipeline? pipeline = null;
        RecordingStatusPipeline.CountingPhases? phases = null;
        BattleSimulation? simulation = null;

        var plan = BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: heroMaxHp, atk: 0.0)),
                BattleTestBench.Enemy(
                    0,
                    BattleTestBench.Stats(maxHp: targetMaxHp, atk: 0.0, aspd: targetAspd, def: targetDef))
                    with { StartingHp = targetHp },
            },
            services =>
            {
                pipeline = new RecordingStatusPipeline(services);
                phases = new RecordingStatusPipeline.CountingPhases();
                timeline = new StatusTimeline(services, pipeline, StatusFixtures.Catalogue());

                return BattleSeams.Strict with
                {
                    Attack = pipeline,
                    Statuses = timeline,
                    Phases = phases,
                    Timeline = new ScriptedApplications(
                        timeline, applications, targetSelf, () => simulation!),
                };
            },
            rules: new CombatRules(ticks, OnKillTriggersFire: true));

        simulation = new BattleSimulation(plan);

        var result = simulation.Run();

        return new Bench(result, simulation, timeline!, pipeline!, phases!);
    }

    /// <summary>A timeline that is the real one, plus a script applying statuses at named ticks.</summary>
    /// <remarks>
    /// It delegates every member: the subject is <see cref="StatusTimeline"/>, and a decorator
    /// answering <c>CanAct</c> or <c>StacksOn</c> itself would test the decorator.
    /// </remarks>
    private sealed class ScriptedApplications : IStatusTimeline
    {
        private readonly StatusTimeline _inner;
        private readonly Applied[] _script;
        private readonly bool _targetSelf;
        private readonly Func<BattleSimulation> _simulation;

        internal ScriptedApplications(
            StatusTimeline inner, Applied[] script, bool targetSelf, Func<BattleSimulation> simulation)
        {
            _inner = inner;
            _script = script;
            _targetSelf = targetSelf;
            _simulation = simulation;
        }

        public void AdvanceTimers(BattleActor actor, int tick)
        {
            if (actor.Index == 0)
            {
                foreach (var step in _script.Where(s => s.Tick == tick))
                {
                    Apply(step);
                }
            }

            _inner.AdvanceTimers(actor, tick);
        }

        public void ExpireDue(BattleActor actor, int tick) => _inner.ExpireDue(actor, tick);

        public bool CanAct(BattleActor actor) => _inner.CanAct(actor);

        public int StacksOn(BattleActor actor, string statusId) => _inner.StacksOn(actor, statusId);

        public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) =>
            _inner.StatModifiers(actor);

        private void Apply(Applied step)
        {
            var actors = _simulation().Actors;
            var hero = actors.Single(a => a.Id == "HERO");
            var enemy = actors.Single(a => a.Id == "ENEMY_0");

            // The applier's ATK is what BURN and BLEED read, so it is set on the applier rather than
            // passed alongside — the seam reads the actor, and a test that handed the number in
            // separately would not exercise that. SetStats takes the whole AggregatedStats (not just
            // ActorStats) because the ward cap is computed from post-aggregation Max HP; an actor
            // that kept only Final would cap wards silently wrong. Only ATK changes here and it
            // takes no multiplier in this fixture, so Max HP carries through unchanged.
            hero.SetStats(hero.Aggregated with { Final = hero.Stats.With(StatId.ATK, step.ApplierAtk) });

            _inner.Apply(
                hero,
                _targetSelf ? hero : enemy,
                step.StatusId,
                step.Value,
                new EffectDuration { Scope = DurationScope.BATTLE, Seconds = step.Seconds ?? 60.0 },
                stacking: null,
                step.EffectId);
        }
    }
}
