using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Status;

/// <summary>
/// 🔒 `05` §3.1's DoT/HoT cadence and `05` §5's twelve, driven through a real fight.
/// </summary>
/// <remarks>
/// Every case here goes through <c>BattleSimulation</c> rather than calling the timeline directly,
/// because half of what `05` §3.1 fixes is <em>which slot</em> the work happens in — the
/// "deals that tick first, then expires" rule is bought entirely by slot 1 running before slot 2, and
/// a unit test of the timeline in isolation cannot see that.
/// </remarks>
public sealed class StatusTimelineTests
{
    /// <summary>
    /// 🔴 `05` §3.1 — reapplication <em>"never re-anchors the cadence"</em>, probed at an anchor that
    /// can tell the two readings apart.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The <c>BURN</c> is applied on tick 7 and reapplied on tick 17.</b> That is the whole
    /// design of the case. Anchored at 7 the boundaries are 27, 47, 67; a re-anchoring implementation
    /// would move them to 37, 57, 77 on the second application. Had the status been applied at tick 0
    /// and reapplied at tick 20, both readings would give 20, 40, 60 and the test could not fail.
    /// The reapplication is deliberately <em>not</em> on a boundary either, so the two answers share
    /// no tick at all.
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

    /// <summary>
    /// 🔒 `05` §3.1 — <em>"per-tick amount = per-second potency × current stack count, <b>read at the
    /// moment the tick lands</b>"</em>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The second stack lands between two boundaries, which is what makes the reading time
    /// observable.</b> A <c>BURN</c> of 0.5 × 10 ATK is 5 per second per stack. Applied at tick 7 it
    /// ticks 5 at tick 27; reapplied at tick 30 — after the first boundary and before the second — it
    /// must tick 10 at tick 47. An implementation that resolved the amount at application time, or
    /// cached it at the first boundary, deals 5 both times and is invisible in any fight where the
    /// stacks never change between application and boundary, which is most of them.
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

    /// <summary>
    /// 🔒 `05` §3.1 slot 2 — <em>"a DoT expiring exactly on a cadence boundary deals that tick first,
    /// then expires"</em>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The duration is chosen so the two events collide exactly.</b> Applied at tick 7 with
    /// <c>D = 1.0 s</c>, the timer elapses at tick 27 and the first cadence boundary is tick 27. The
    /// rule says one tick of damage, then the expiry. An engine that expired in slot 1, or ordered
    /// expiry before the cadence, deals <b>zero</b> — a whole second of a one-second DoT, silently,
    /// on every DoT whose duration is a whole number of seconds, which is all of them in `05` §6.1a.
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

    /// <summary>
    /// 🔒 `05` §3.1 — a DoT tick is <em>"a damage event, not an attack"</em>, so it goes down the
    /// route `05` §4.2 gives that exemption list and never through <c>ResolveAttack</c>.
    /// </summary>
    /// <remarks>
    /// Asserted on <em>which member of the pipeline was called</em> (steering S2 — the identity, not
    /// the symptom). Checking only that the target lost HP would pass over an implementation that ran
    /// the full attack pipeline, which would dodge, crit, block and floor the tick.
    /// </remarks>
    [Fact]
    public void A_DoT_tick_routes_through_the_non_attack_damage_path_and_wards_absorb_it()
    {
        var bench = Fight(new[] { A(7, "E_BURN", "BURN", 0.5, 10.0) });

        bench.Pipeline.Dots.ShouldNotBeEmpty();
        bench.Pipeline.Dots.ShouldAllBe(d => !d.BypassesWards);
        bench.Pipeline.Heals.ShouldBeEmpty();
    }

    /// <summary>
    /// 🔒 `05` §3.1 — <em>"HoT ticks route through <c>Heal()</c> (§4.3), not §4"</em>, so
    /// <c>HEAL%</c> applies and <c>ON_HEAL</c> fires.
    /// </summary>
    /// <remarks>
    /// `05` §5's <c>REGEN</c> is <em>"heal X% Max HP per second"</em>, so a 0.1 <c>REGEN</c> on a
    /// 200 Max HP hero is 20 a second. The amount is asserted as well as the route: a HoT that reached
    /// <c>Heal</c> with the raw fraction would heal 0.1 HP a second and look like a routing success.
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

    /// <summary>
    /// 🔒 `05` §5's <c>POISON</c> is <em>"X% of <b>target</b> Max HP per second"</em> and
    /// <c>BURN</c> is <em>"X% of <b>attacker</b> ATK"</em> — two different bases, and the difference
    /// is observable.
    /// </summary>
    /// <remarks>
    /// The same authored X, the same applier and the same target produce different numbers, which is
    /// what makes this a test of the basis rather than of the arithmetic. An engine that used one
    /// basis for both would agree with one of these two rows and disagree with the other.
    /// </remarks>
    [Fact]
    public void BURN_reads_the_appliers_ATK_and_POISON_reads_the_targets_Max_HP()
    {
        var burn = Fight(new[] { A(7, "E_BURN", "BURN", 0.5, 40.0) }, targetMaxHp: 500.0);
        var poison = Fight(new[] { A(7, "E_POISON", "POISON", 0.5, 40.0) }, targetMaxHp: 500.0);

        burn.Pipeline.Dots[0].Amount.ShouldBe(20.0, "0.5 x the applier's 40 ATK");
        poison.Pipeline.Dots[0].Amount.ShouldBe(250.0, "0.5 x the target's 500 Max HP");
    }

    /// <summary>
    /// 📐 `05` §5 — <c>BLEED</c>'s tick is <em>"that amount × (1 + target's missing-HP
    /// fraction)"</em>, and the scaling term is the one 📐 the section carries.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>Probed at two health levels, and the second is what discriminates.</b> At full health the
    /// missing-HP fraction is 0 and the tick is the flat amount — which an implementation that
    /// ignored the term entirely also produces. The wounded case is the one that separates them. The
    /// coefficient is read from the catalogue rather than written as 1.0 here, so the test states
    /// the rule and the data states the number.
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

    /// <summary>
    /// 🔒 `05` §5's stat debuffs reach `18` §8's aggregation — a <c>FREEZE</c> really is −50% ASPD.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The assertion is on the actor's aggregated ASPD, not on the timeline's own bookkeeping.</b>
    /// A status recorded correctly and never collected by `18` §8 step 1 is a status that does
    /// nothing, and every test of the timeline's internals would still pass. `05` §5 states
    /// <c>FREEZE</c> as a literal −50%, so the number is the catalogue's and the effect's own value
    /// is not consulted — which the second assertion pins.
    /// </remarks>
    [Fact]
    public void A_FREEZE_halves_the_targets_ASPD_through_the_18_section_8_aggregation()
    {
        var bench = Fight(new[] { A(7, "E_FREEZE", "FREEZE", 0.0, 10.0) }, targetAspd: 2.0);

        var enemy = bench.Simulation.Actors.Single(a => a.Id == "ENEMY_0");

        enemy.Stats[StatId.ASPD].ShouldBe(1.0, "05 §5's FREEZE is -50% ASPD");
    }

    /// <summary>
    /// 🔒 A stat debuff stops applying when it expires — the aggregation is not a one-way write.
    /// </summary>
    /// <remarks>
    /// The negative control for the rule above, and the one an implementation writing into
    /// <c>CombatFlowState</c>'s percent buckets would fail: those carry no duration, so a
    /// <c>FREEZE</c> put there would halve the actor's ASPD for the rest of the fight.
    /// </remarks>
    [Fact]
    public void A_stat_debuff_stops_applying_once_it_expires()
    {
        var bench = Fight(new[] { A(7, "E_FREEZE", "FREEZE", 0.0, 10.0, 1.0) }, targetAspd: 2.0, ticks: 60);

        var enemy = bench.Simulation.Actors.Single(a => a.Id == "ENEMY_0");

        enemy.Stats[StatId.ASPD].ShouldBe(2.0, "the FREEZE expired at tick 27");
    }

    /// <summary>
    /// 🔒 A stat debuff that expires and is applied again debuffs again — the aggregation reaches it
    /// the second time too.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The path the fast-out in <c>StatModifiers</c> could break, and the one neither of the two
    /// tests above covers.</b> That early-out reads a per-actor count of live stat-modifying statuses
    /// rather than walking the set, because <c>RefreshStats</c> asks for every actor on every one of
    /// 1800 ticks. A count that failed to decrement on expiry would keep aggregating a dead
    /// <c>FREEZE</c>; one that failed to increment on a re-application, or that went negative and
    /// stuck, would silently stop aggregating a live one — and the apply case and the expiry case
    /// each pass on their own either way. This is apply → expire → apply, asserted at all three
    /// points.
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

    /// <summary>
    /// 🔒 `05` §5 — <c>SUNDER</c> <em>"stacks to 5"</em>, and the aggregate is the sum of the stacks.
    /// </summary>
    /// <remarks>
    /// Six applications, a ceiling of five: `18` §6 drops the surplus rather than evicting, so the
    /// debuff is 5 × −5% and not 6 × −5%. Both the reached ceiling and the arithmetic are asserted,
    /// because an implementation that ignored the ceiling produces −30% and one that never stacked
    /// produces −5%, and only one number is right.
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
    /// 🔒 `05` §5's <c>WARD</c> is deferred wholly to §4.1 — applying it grants a ward segment and
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

    /// <summary>
    /// 🔒 `05` §3.1 — <em>"one instance per <c>statusId</c> per target"</em>: two appliers of one
    /// status share one instance and one cadence.
    /// </summary>
    /// <remarks>
    /// Two distinct effect ids apply <c>BURN</c> ten ticks apart. If each opened its own instance
    /// there would be two anchors and two boundary series; the section says there is one, anchored on
    /// the first.
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

    /// <summary>
    /// 🔒 The three obligations `IStatusTimeline` handed over: one phase check and one
    /// <c>ON_LOW_HP</c> per DoT HP change, and no more.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The assertion is <em>exactly one</em>, not <em>at least one</em>.</b> <c>ON_LOW_HP</c> is
    /// a crossing, so a doubled observation is a doubled firing — and the obvious way to satisfy the
    /// contract's literal wording ("M2-10 routes the phase check") is to call
    /// <c>AfterHpDecrease</c> from the tick <em>as well as</em> letting `05` §4 step 9 do it, which
    /// is exactly two. An "at least one" assertion would pass over that.
    /// </remarks>
    [Fact]
    public void Each_DoT_HP_change_runs_the_phase_check_and_ON_LOW_HP_exactly_once()
    {
        var bench = Fight(new[] { A(7, "E_BURN", "BURN", 0.5, 10.0) }, ticks: 60);

        bench.Pipeline.Dots.Count.ShouldBe(2, "boundaries at ticks 27 and 47");

        // 🔴 Counted on the PHASE CONTROLLER, not on the pipeline double — review found a
        // pipeline-side counter blind to the exact defect this test exists for. A timeline calling
        // _services.AfterHpDecrease in addition to routing through `05` §4 step 9 leaves Dots.Count
        // and any pipeline counter at 2, and doubles only this.
        bench.Phases.HpDecreaseCalls.ShouldBe(2);
    }

    /// <summary>
    /// 🔒 `05` §7 — a DoT tick emits <c>StatusTick</c> naming the status, so the replayer can tell it
    /// from a hit.
    /// </summary>
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

    /// <summary>
    /// 🔒 `05` §5's <c>STUN</c>, through the tick loop: slot 4a's <em>"and not stunned"</em>.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The first version asserted only <c>CanAct</c> at the fight's last tick, which is a
    /// statement that the stun ENDED — review showed that an engine which never consults the gate,
    /// and one which never applies the status at all, both passed it.</b> The claim is about slot 4,
    /// so the assertion is now on slot 4's output. A 1.0-ASPD enemy stunned at tick 0 for the capped
    /// 1.5 s loses its tick-0 and tick-20 swings and resumes at tick 30; deleting
    /// <c>&amp;&amp; _seams.Timeline.CanAct(attacker)</c> from <c>BattleSimulation</c> reddens this.
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

    /// <summary>
    /// Runs a fight in which a scripted list of applications lands at named ticks.
    /// </summary>
    /// <remarks>
    /// The applications are driven by a <see cref="ScriptedApplications"/> timeline decorator rather
    /// than by authored <c>PERIODIC</c> effects, for the reason <c>BattleTestBench</c> states about
    /// its own doubles: what is under test is the cadence, and routing every case through the trigger
    /// engine would make a trigger defect look like a cadence defect.
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
                    0, BattleTestBench.Stats(maxHp: targetMaxHp, atk: 0.0, aspd: targetAspd, def: targetDef)),
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

        if (targetHp is { } hp)
        {
            simulation.Actors.Single(a => a.Id == "ENEMY_0").SetCurrentHp(hp);
        }

        var result = simulation.Run();

        return new Bench(result, simulation, timeline!, pipeline!, phases!);
    }

    /// <summary>
    /// A timeline that is the real one, plus a script that applies statuses at named ticks.
    /// </summary>
    /// <remarks>
    /// 🔒 It <b>delegates every member</b> rather than reimplementing any: the subject under test is
    /// <see cref="StatusTimeline"/>, and a decorator that answered <c>CanAct</c> or <c>StacksOn</c>
    /// itself would be testing the decorator. The script runs before the delegated
    /// <c>AdvanceTimers</c>, which is where an applying effect would land — slot 3 and slot 4 both sit
    /// after slot 1, so applying earlier than the real engine can only make the cadence harder to get
    /// right, never easier.
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

            // The applier's ATK is what `05` §5's BURN and BLEED read, so it is set on the applier
            // rather than passed alongside — the seam reads the actor, and a test that handed the
            // number in separately would not exercise that.
            // Conductor edit at merge: M2-09 changed SetStats to take the whole AggregatedStats
            // rather than an ActorStats, to satisfy M2-07's ward-cap obligation — `05` §4.1 caps the
            // pool on Max HP "as it stood after `18` §8 step 7", so an actor that kept only Final
            // would cap every CP_GLASS_HEART ward at 1 HP silently. Only ATK moves here, and ATK
            // takes no multiplier in this fixture, so the post-step-7 Max HP is carried unchanged.
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
