using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §3 — how a fight ends: a cleared side, a downed hero, or the timeout, on which
/// <em>"the side with the higher <b>remaining HP fraction</b> wins"</em>.
/// </summary>
public sealed class BattleOutcomeTests
{
    /// <summary>
    /// 🔒 The timeout rule, with the hero ahead on fraction and <b>behind on absolute HP</b> — which
    /// is what makes it a fraction rule rather than an HP rule.
    /// </summary>
    [Fact]
    public void On_the_timeout_the_higher_remaining_HP_FRACTION_wins_not_the_higher_HP()
    {
        // Nobody can hurt anybody: the fight runs the full cap and the standings decide it.
        // Hero: 60/100 = 0.60 of its bar, 60 HP absolute.
        // Enemy: 300/1000 = 0.30 of its bar, 300 HP absolute — five times the hero's HP.
        var result = Standoff(heroMaxHp: 100, heroHp: 60, enemyMaxHp: 1000, enemyHp: 300);

        result.HeroWon.ShouldBeTrue();
        result.DurationTicks.ShouldBe(CombatLog.MaxTicks);
        result.HeroHpRemaining.ShouldBe(60.0);
    }

    /// <summary>The other side of the same rule.</summary>
    [Fact]
    public void The_enemy_side_takes_the_timeout_when_its_fraction_is_higher()
    {
        var result = Standoff(heroMaxHp: 1000, heroHp: 300, enemyMaxHp: 100, enemyHp: 60);

        result.HeroWon.ShouldBeFalse();
        result.DurationTicks.ShouldBe(CombatLog.MaxTicks);
    }

    /// <summary>
    /// 🔒 The fraction is the <b>side's</b>, summed over its killable actors — not the hero's against
    /// one enemy's.
    /// </summary>
    /// <remarks>
    /// The hero is at 0.50. Enemy 0 is at 1.00 and enemy 1 is at 0.02, so the pack is
    /// <c>(100 + 2) / (100 + 100) = 0.51</c> and takes it. Comparing the hero against the
    /// <em>weakest</em> enemy would give the hero the win, and against the <em>strongest</em> the
    /// same answer as the sum — so the case discriminates all three readings.
    /// </remarks>
    [Fact]
    public void The_timeout_fraction_is_summed_over_the_whole_side()
    {
        var result = Standoff(
            heroMaxHp: 100,
            heroHp: 50,
            enemies: new[] { (100.0, 100.0), (100.0, 2.0) });

        result.HeroWon.ShouldBeFalse();
    }

    /// <summary>
    /// ⚠️ An exact tie is a loss for the hero — errata, because `05` §3 authors no PvE tie rule and
    /// `05` §9 defines <c>ParPower</c> by <em>clear rate</em>. A 90 s standoff cleared nothing.
    /// </summary>
    [Fact]
    public void An_exact_tie_on_the_timeout_is_not_a_clear()
    {
        var result = Standoff(heroMaxHp: 100, heroHp: 40, enemyMaxHp: 200, enemyHp: 80);

        result.HeroWon.ShouldBeFalse();
    }

    /// <summary>A cleared enemy side is a win however little HP the hero has left.</summary>
    [Fact]
    public void Clearing_the_enemy_side_is_a_win()
    {
        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 25)),
                BattleTestBench.Enemy(1, BattleTestBench.Stats(maxHp: 25)),
            },
            services => BattleSeams.Strict with { Attack = new RecordingAttackPipeline(services, 10.0) }));

        result.HeroWon.ShouldBeTrue();
        result.Log.Count(e => e.Type == CombatEventType.ActorDeath).ShouldBe(2);
    }

    /// <summary>A downed hero is a loss however much the enemy side has left.</summary>
    [Fact]
    public void A_downed_hero_is_a_loss()
    {
        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10, aspd: 0.001)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10_000)),
            },
            services => BattleSeams.Strict with { Attack = new RecordingAttackPipeline(services, 10.0) }));

        result.HeroWon.ShouldBeFalse();
        result.HeroHpRemaining.ShouldBe(0.0);
    }

    /// <summary>
    /// 🔒 The cap is a <b>parameter</b>. `05` §3's PvE fight is 1800 ticks and `05` §3.3's duel is
    /// 1200 (<c>pvpMaxFightSeconds</c>, `11` §4.3) — which M2-06 recorded as unenforced anywhere and
    /// M2-14 inherits.
    /// </summary>
    [Theory]
    [InlineData(1800)]
    [InlineData(1200)]
    [InlineData(1)]
    public void The_tick_cap_is_a_parameter_and_the_fight_stops_at_it(int maxTicks)
    {
        var result = Standoff(100, 100, 100, 100, maxTicks: maxTicks);

        result.DurationTicks.ShouldBe(maxTicks);
        result.Log.ShouldAllBe(e => e.Tick < maxTicks);
    }

    /// <summary>`05` §3.3's duel bounds, stated as the record the loop reads them from.</summary>
    [Fact]
    public void The_duel_bounds_are_expressible_without_touching_the_loop()
    {
        CombatRules.PvE.MaxTicks.ShouldBe(1800);
        CombatRules.PvE.OnKillTriggersFire.ShouldBeTrue();
        CombatRules.PvE.IsPvp.ShouldBeFalse();
        CombatRules.PvE.HorizonSeconds.ShouldBe(90.0);

        var duel = new CombatRules(MaxTicks: 1200, OnKillTriggersFire: false, IsPvp: true);

        duel.HorizonSeconds.ShouldBe(60.0);
        Should.NotThrow(() => duel.Validated());
    }

    /// <summary>A cap outside the log's addressable range is refused rather than truncated.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1801)]
    public void A_cap_outside_the_logs_range_is_refused(int maxTicks) =>
        Should.Throw<ArgumentOutOfRangeException>(
            () => new CombatRules(maxTicks, OnKillTriggersFire: true, IsPvp: false).Validated());

    private static SimulationResult Standoff(
        double heroMaxHp, double heroHp, double enemyMaxHp, double enemyHp, int maxTicks = CombatLog.MaxTicks) =>
        Standoff(heroMaxHp, heroHp, new[] { (enemyMaxHp, enemyHp) }, maxTicks);

    /// <summary>
    /// A fight nobody can win: every actor's ASPD is low enough that nothing swings before the cap,
    /// so the standings at the timeout are exactly the ones set up here.
    /// </summary>
    private static SimulationResult Standoff(
        double heroMaxHp,
        double heroHp,
        (double MaxHp, double Hp)[] enemies,
        int maxTicks = CombatLog.MaxTicks)
    {
        var actors = new List<ActorPlan>
        {
            BattleTestBench.Hero(BattleTestBench.Stats(maxHp: heroMaxHp, aspd: 0.001)),
        };

        for (var i = 0; i < enemies.Length; i++)
        {
            actors.Add(BattleTestBench.Enemy(i, BattleTestBench.Stats(maxHp: enemies[i].MaxHp, aspd: 0.001)));
        }

        return CombatSimulator.Simulate(BattleTestBench.Plan(
            actors,
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, damage: 0.0),
                Timeline = new WoundedAtStart(heroHp, enemies.Select(e => e.Hp).ToArray()),
            },
            rules: new CombatRules(maxTicks, OnKillTriggersFire: true, IsPvp: false)));
    }
}

/// <summary>Sets the standings once, on tick 0's slot 1 — a fight already in progress.</summary>
internal sealed class WoundedAtStart : IStatusTimeline
{
    private readonly double _heroHp;
    private readonly double[] _enemyHp;

    internal WoundedAtStart(double heroHp, double[] enemyHp)
    {
        _heroHp = heroHp;
        _enemyHp = enemyHp;
    }

    /// <inheritdoc />
    public void AdvanceTimers(BattleActor actor, int tick)
    {
        if (tick != 0)
        {
            return;
        }

        if (actor.Id == "HERO")
        {
            actor.SetCurrentHp(_heroHp);

            return;
        }

        for (var i = 0; i < _enemyHp.Length; i++)
        {
            if (actor.Id == $"ENEMY_{i}")
            {
                actor.SetCurrentHp(_enemyHp[i]);
            }
        }
    }

    /// <inheritdoc />
    public void ExpireDue(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    public bool CanAct(BattleActor actor) => true;

    /// <inheritdoc />
    public int StacksOn(BattleActor actor, string statusId) => 0;
}
