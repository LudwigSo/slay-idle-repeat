using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Status;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔴 <b>R3</b> — `18` §6's <c>PHASE</c> scope, closed: <em>"ends when the boss exits the phase in
/// which the effect was applied"</em>, driven end to end through M2-10's real
/// <see cref="StatusTimeline"/> and <c>DurationEvaluator</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>DurationEvaluator</c> has implemented `18` §6's boundary since M2-06 and it is correct: it ends
/// the effect the moment <c>DurationProbe.CurrentPhase</c> exceeds
/// <c>EffectApplication.AppliedInPhase</c>. But <b>nothing filled either field</b> — no seam could
/// answer <em>"what phase is this fight in?"</em> — so both were <c>null</c> at every call site, and
/// §6's <em>"outside a boss fight it behaves as <c>BATTLE</c>"</em> fallback was taken <b>inside</b>
/// boss fights too. R3 makes every boss <c>AURA</c> <c>PHASE</c>-scoped, so the scope covered the
/// whole of the boss content set and did nothing at all, silently. M2-12 added
/// <c>IBossPhases.CurrentPhase</c> and routed it through <c>BattleServices.CurrentBossPhase</c> into
/// the two fields.
/// </para>
/// <para>
/// 🔴 <b>Which is why every case here carries a <c>BATTLE</c>-scoped twin, applied by the same call
/// at the same tick to the same actor.</b> Without it, "the <c>PHASE</c> status is gone after the
/// phase-3 entry" is equally consistent with <em>everything</em> having expired — a fight that ended,
/// a timer nobody set, a timeline that dropped its instances. The twin is the reading that
/// distinguishes the boundary from the apocalypse.
/// </para>
/// <para>
/// ⚠️ The phase source here is <see cref="ScriptedPhases"/> rather than
/// <c>BossPhaseController</c>, deliberately: the subject is `18` §6's <b>scope</b>, and the
/// controller is a Phase 1b stub. Coupling them would make this case red for M2-12's implementation
/// phase rather than green for the wiring it is about.
/// </para>
/// </remarks>
public sealed class BossPhaseScopeTests
{
    private const string Boss = "BOSS_THORNMAW";

    /// <summary>The <c>PHASE</c>-scoped subject — `18` §6's boss <c>AURA</c> shape.</summary>
    private const string PhaseScoped = "SUNDER";

    /// <summary>The <c>BATTLE</c>-scoped twin — the negative control. See the class remarks.</summary>
    private const string BattleScoped = "WEAKEN";

    /// <summary>
    /// 🔒 <b>Shape 1 — it does NOT end on a phase-2-internal tick.</b> A boundary that fired early
    /// would be indistinguishable from `18` §6 working, in a log where the aura simply stopped.
    /// </summary>
    [Fact]
    public void A_PHASE_scoped_status_applied_in_phase_2_survives_every_tick_inside_phase_2()
    {
        var run = Fight();

        run.PhaseAt(20).ShouldBe(2, "the floor: it really was applied in phase 2");
        run.PhaseAt(38).ShouldBe(2, "and the boss is still there 18 ticks later");

        run.StacksAt(38, PhaseScoped).ShouldBe(
            1,
            "18 §6 ends a PHASE scope at the EXIT of its phase and at nothing else — not on a tick " +
            "boundary, not on a cadence, not on a timer it does not carry");

        run.StacksAt(38, BattleScoped).ShouldBe(1, "and so, of course, does the BATTLE-scoped twin");
    }

    /// <summary>
    /// 🔴 <b>Shape 2 — it ends on entry to phase 3, and the twin does not.</b> That pair of readings
    /// is the whole of R3: one status ends because the boss changed phase, the other does not,
    /// inside one fight, from one application call.
    /// </summary>
    [Fact]
    public void A_PHASE_scoped_status_ends_on_entry_to_phase_3_while_its_BATTLE_scoped_twin_survives()
    {
        var run = Fight();

        run.PhaseAt(38).ShouldBe(2, "the control: still in phase 2 the tick before the burst");
        run.PhaseAt(60).ShouldBe(3, "and the exit really happened");

        run.StacksAt(38, PhaseScoped).ShouldBe(1, "alive on the last tick of its own phase");
        run.StacksAt(60, PhaseScoped).ShouldBe(
            0,
            "🔴 18 §6: 'ends when the boss exits the phase in which the effect was applied'. Before " +
            "IBossPhases.CurrentPhase existed this read 1 — the scope degraded to BATTLE and R3's " +
            "boss auras outlived every phase that granted them");

        run.StacksAt(60, BattleScoped).ShouldBe(
            1,
            "🔴 THE NEGATIVE CONTROL. Without it, the 0 above is equally consistent with the whole " +
            "timeline having emptied — which is what a broken probe, an ended battle or a dropped " +
            "instance would also look like");
    }

    /// <summary>
    /// 🔒 And the other side of <em>"the phase it was applied in"</em>: a status applied <b>in phase
    /// 3</b> is untouched by later phase-3 ticks, because phase 3 is never exited (`05` §3.1).
    /// </summary>
    /// <remarks>
    /// It is the case that separates <em>"ends when the phase changes"</em> from <em>"ends when the
    /// phase differs from the one this fight started in"</em> — an implementation that compared
    /// against phase 1 rather than against the application's own phase would end this one too.
    /// </remarks>
    [Fact]
    public void A_PHASE_scoped_status_applied_in_phase_3_is_not_ended_by_phase_3_continuing()
    {
        var run = Fight(applyAt: 50);

        run.PhaseAt(50).ShouldBe(3, "the floor: it was applied in phase 3");
        run.StacksAt(70, PhaseScoped).ShouldBe(
            1, "05 §3.1: there is no phase 4, so phase 3 is never exited and its scope never ends");

        run.StacksAt(70, BattleScoped).ShouldBe(1, "and the twin agrees, as it must here");
    }

    /// <summary>
    /// 🔒 `18` §6's fallback, unchanged: <b>outside</b> a boss fight a <c>PHASE</c> scope behaves as
    /// <c>BATTLE</c>. The same script with a plain enemy in the boss's place ends nothing.
    /// </summary>
    /// <remarks>
    /// 🔴 The floor under everything above. If <c>CurrentBossPhase</c> answered a phase for
    /// <em>every</em> fight, the two cases above would pass for the wrong reason and every non-boss
    /// battle in the game would silently acquire a phase boundary.
    /// </remarks>
    [Fact]
    public void Outside_a_boss_fight_a_PHASE_scoped_status_behaves_as_BATTLE()
    {
        var run = Fight(boss: false);

        run.PhaseAt(60).ShouldBeNull("there is no boss, so there is no phase to be in");

        run.StacksAt(60, PhaseScoped).ShouldBe(
            1, "18 §6: 'outside a boss fight it behaves as BATTLE'");
        run.StacksAt(60, BattleScoped).ShouldBe(1);
    }

    // ════════════════════════════════════════════════════ fixtures

    private static PhaseScopeRun Fight(int applyAt = 20, bool boss = true)
    {
        PhaseScopeProbe? probe = null;

        var enemy = boss
            ? BossTestBench.Boss(Boss, maxHp: 1000.0)
            : BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 1000.0, aspd: 0.001));

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[] { BossTestBench.Hero(), enemy },
            services =>
            {
                var pipeline = new RecordingStatusPipeline(services);
                var timeline = new StatusTimeline(services, pipeline, StatusFixtures.Catalogue());
                var phases = new ScriptedPhases();

                probe = new PhaseScopeProbe(services, timeline, applyAt);

                return BattleSeams.Strict with
                {
                    Attack = pipeline,
                    Statuses = timeline,
                    Timeline = probe,
                    Phases = phases,
                };
            },
            rules: BossTestBench.Rules(maxTicks: 80)));

        return new PhaseScopeRun(probe!);
    }

    private sealed record PhaseScopeRun(PhaseScopeProbe Probe)
    {
        internal int? PhaseAt(int tick) => Probe.Sample(tick).Phase;

        internal int StacksAt(int tick, string statusId) =>
            string.Equals(statusId, PhaseScoped, StringComparison.Ordinal)
                ? Probe.Sample(tick).PhaseScopedStacks
                : Probe.Sample(tick).BattleScopedStacks;
    }

    /// <summary>
    /// 🔒 `17` §1's three bands, as a phase <b>source</b> and nothing else: it reads HP on
    /// <c>AfterHpDecrease</c> and never reverts. It is not a second <c>BossPhaseController</c> — it
    /// registers nothing, activates nothing and logs nothing, because the subject of this file is
    /// `18` §6's scope rather than `05` §3.1's transition.
    /// </summary>
    private sealed class ScriptedPhases : IBossPhases
    {
        private readonly Dictionary<string, int> _phase = new(StringComparer.Ordinal);

        public void EnterInitialPhase(BattleActor actor, int tick) => _phase[actor.Id] = 1;

        public void AfterHpDecrease(BattleActor actor, int tick)
        {
            if (!actor.IsBoss)
            {
                return;
            }

            // 🔒 Literals, not BossPhaseRules: that method is a Phase 1b stub, and a scope test that
            //    took its thresholds from the code under construction would be measuring nothing.
            var reading = actor.HpFraction <= 0.33 ? 3 : actor.HpFraction <= 0.66 ? 2 : 1;

            // `05` §3.1 — phases never revert.
            _phase[actor.Id] = Math.Max(_phase.GetValueOrDefault(actor.Id, 1), reading);
        }

        public void AdvanceTick(BattleActor actor, int tick)
        {
        }

        public int? CurrentPhase(BattleActor actor) =>
            _phase.TryGetValue(actor.Id, out var phase) ? phase : null;
    }

    /// <summary>
    /// Slot 1's seat: it drives the enemy's HP down the two `17` §1 thresholds, applies the two
    /// statuses once, and samples what M2-10's timeline holds at the <b>top</b> of every tick.
    /// </summary>
    /// <remarks>
    /// The sample is taken before the tick's own script runs, so a reading at tick <c>t</c> is the
    /// state slot 2 of tick <c>t − 1</c> left behind — which is where <c>ExpireDue</c> ends a
    /// duration.
    /// </remarks>
    private sealed class PhaseScopeProbe : IStatusTimeline
    {
        private readonly BattleServices _services;
        private readonly StatusTimeline _inner;
        private readonly int _applyAt;
        private readonly List<PhaseScopeSample> _samples = new();

        private int _sampledTick = -1;

        internal PhaseScopeProbe(BattleServices services, StatusTimeline inner, int applyAt)
        {
            _services = services;
            _inner = inner;
            _applyAt = applyAt;
        }

        internal PhaseScopeSample Sample(int tick) => _samples.Single(s => s.Tick == tick);

        public void AdvanceTimers(BattleActor actor, int tick)
        {
            if (_sampledTick != tick)
            {
                _sampledTick = tick;
                Record(tick);
                Script(tick);
            }

            _inner.AdvanceTimers(actor, tick);
        }

        public void ExpireDue(BattleActor actor, int tick) => _inner.ExpireDue(actor, tick);

        public bool CanAct(BattleActor actor) => _inner.CanAct(actor);

        public int StacksOn(BattleActor actor, string statusId) => _inner.StacksOn(actor, statusId);

        public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) =>
            _inner.StatModifiers(actor);

        private BattleActor Enemy => _services.Actors.Single(a => a.Side == BattleSide.ENEMY);

        private void Record(int tick) =>
            _samples.Add(new PhaseScopeSample(
                tick,
                _services.CurrentBossPhase,
                _inner.StacksOn(Enemy, PhaseScoped),
                _inner.StacksOn(Enemy, BattleScoped)));

        private void Script(int tick)
        {
            var enemy = Enemy;

            // The two HP steps: into phase 2 at tick 10, and into phase 3 at tick 40.
            if (tick is 10 or 40)
            {
                enemy.SetCurrentHp((tick == 10 ? 0.50 : 0.20) * enemy.MaxHp);
                _services.AfterHpDecrease(enemy);
            }

            if (tick != _applyAt)
            {
                return;
            }

            // 🔒 ONE call site for both, so the two differ in exactly one thing: the scope.
            _inner.Apply(
                enemy, enemy, PhaseScoped, 0.05,
                new EffectDuration { Scope = DurationScope.PHASE }, stacking: null, "BOSS_AURA_PHASE");

            _inner.Apply(
                enemy, enemy, BattleScoped, 0.05,
                new EffectDuration { Scope = DurationScope.BATTLE }, stacking: null, "BOSS_AURA_BATTLE");
        }
    }

    /// <summary>One reading of the fight, at the top of one tick.</summary>
    /// <param name="Tick">The tick.</param>
    /// <param name="Phase"><c>BattleServices.CurrentBossPhase</c> — <c>null</c> outside a boss fight.</param>
    /// <param name="PhaseScopedStacks">Stacks of the <c>PHASE</c>-scoped status on the enemy.</param>
    /// <param name="BattleScopedStacks">Stacks of its <c>BATTLE</c>-scoped twin.</param>
    private readonly record struct PhaseScopeSample(
        int Tick, int? Phase, int PhaseScopedStacks, int BattleScopedStacks);
}
