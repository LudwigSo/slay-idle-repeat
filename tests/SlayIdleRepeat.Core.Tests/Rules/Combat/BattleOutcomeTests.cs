using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
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

        // 🔒 Floored before the ShouldAllBe, which passes on an empty collection: at maxTicks 1 a
        // loop that emitted nothing at all would satisfy "every event is below the cap" while
        // proving nothing about where the cap falls.
        result.Log.ShouldNotBeEmpty();
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

        // ⚠️ Built through the factory since M2-14. `IsPvp` stopped being a stored flag: `11` §4.3
        // makes an underdog mandatory in a duel and `05` §3 gives PvE none, so the two were one fact
        // and IsPvp is now DERIVED from ExactTieWinner. The hand-rolled `(1200, false, IsPvp: true)`
        // this line used to carry — a duel with 11 §4.3's tie rule silently off — is no longer a
        // representable value. See PvpDuelTests for the tie rule itself.
        var duel = CombatRules.Duel(pvpMaxFightSeconds: 60.0, lowerRatedSide: BattleSide.ENEMY);

        duel.MaxTicks.ShouldBe(1200);
        duel.OnKillTriggersFire.ShouldBeFalse();
        duel.IsPvp.ShouldBeTrue();
        duel.ExactTieWinner.ShouldBe(BattleSide.ENEMY);
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
            () => new CombatRules(maxTicks, OnKillTriggersFire: true).Validated());

    /// <summary>
    /// 🔒 A <b>despawned</b> summon has left the fight and has no stake in the timeout — `18` §2.4's
    /// <em>"despawned ≠ killed"</em>.
    /// </summary>
    /// <remarks>
    /// A killed actor sits at <c>0/max</c> and correctly drags its side's fraction down. A despawned
    /// one keeps its HP, because <c>CLEAR_SUMMONS</c> is not a death — so counting it would prop the
    /// side up at <c>max/max</c> and hand the timeout to whoever cleared their own summons. Here the
    /// hero is at 0.50 and the enemy side's real remaining is 0.30; the despawned shard at full
    /// health would lift the pack to 0.65 and steal the win.
    /// </remarks>
    [Fact]
    public void A_despawned_summon_does_not_count_toward_the_timeout_fraction()
    {
        BattleServices? services = null;

        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 100, aspd: 0.001)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 100, aspd: 0.001)),
            },
            s =>
            {
                services = s;

                return BattleSeams.Strict with
                {
                    Attack = new RecordingAttackPipeline(s, damage: 0.0),
                    Timeline = new DespawnedShard(s, heroHp: 50, enemyHp: 30, shardHp: 100),
                };
            },
            rules: new CombatRules(MaxTicks: 40, OnKillTriggersFire: true)));

        services.ShouldNotBeNull();

        // The shard is really there, really despawned, and really still at full health.
        var shard = services.Actors.Single(a => a.IsSummon);
        shard.Despawned.ShouldBeTrue();
        shard.CurrentHp.ShouldBe(100.0);

        result.DurationTicks.ShouldBe(40);
        result.HeroWon.ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 A roster with no hero, or nothing killable to fight, is refused — both would otherwise
    /// produce a valid one-tick log rather than an error.
    /// </summary>
    [Fact]
    public void A_roster_missing_a_side_is_refused_rather_than_decided_on_tick_0()
    {
        Should.Throw<ArgumentException>(() => CombatSimulator.Simulate(BattleTestBench.Plan(new[]
            {
                BattleTestBench.Pet(0),
                BattleTestBench.Enemy(0),
            })))
            .Message.ShouldContain("one-tick loss");

        Should.Throw<ArgumentException>(() => CombatSimulator.Simulate(BattleTestBench.Plan(new[]
            {
                BattleTestBench.Hero(),
                BattleTestBench.Pet(0) with
                {
                    Id = "ENEMY_PET", Index = 9, LogId = 20, Side = BattleSide.ENEMY,
                },
            })))
            .Message.ShouldContain("one-tick win");

        // 🔒 `05` §3.3's duel satisfies both clauses: the defending hero is on the ENEMY side, so it
        // is the killable enemy rather than a second hero-side hero.
        Should.NotThrow(() => BattleTestBench.Plan(new[]
            {
                BattleTestBench.Hero(),
                BattleTestBench.Hero() with
                {
                    Id = "HERO_DEFENDER",
                    Index = CombatActor.FirstEnemy,
                    LogId = CombatActor.FirstEnemy,
                    Side = BattleSide.ENEMY,
                },
            })
            .Validated());
    }

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
            rules: new CombatRules(maxTicks, OnKillTriggersFire: true)));
    }
}

/// <summary>
/// Sets the standings once and admits a summon that is immediately despawned — `18` §2.4's
/// <c>CLEAR_SUMMONS</c>, which leaves the actor at full health and out of the fight.
/// </summary>
internal sealed class DespawnedShard : IStatusTimeline
{
    private readonly BattleServices _services;
    private readonly double _heroHp;
    private readonly double _enemyHp;
    private readonly double _shardHp;

    private bool _done;

    internal DespawnedShard(BattleServices services, double heroHp, double enemyHp, double shardHp)
    {
        _services = services;
        _heroHp = heroHp;
        _enemyHp = enemyHp;
        _shardHp = shardHp;
    }

    /// <inheritdoc />
    public void AdvanceTimers(BattleActor actor, int tick)
    {
        if (tick != 0 || _done)
        {
            return;
        }

        if (actor.Id == "HERO")
        {
            actor.SetCurrentHp(_heroHp);

            return;
        }

        if (actor.Id != "ENEMY_0")
        {
            return;
        }

        _done = true;
        actor.SetCurrentHp(_enemyHp);

        var shard = _services.AdmitSummon(
            BattleTestBench.Enemy(50, BattleTestBench.Stats(maxHp: _shardHp, aspd: 0.001)) with
            {
                Id = "SHARD",
                Index = -1,
                LogId = 1,
                OwnerId = actor.Id,
            });

        shard.Despawn();
    }

    /// <inheritdoc />
    public void ExpireDue(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    public bool CanAct(BattleActor actor) => true;

    /// <inheritdoc />
    public int StacksOn(BattleActor actor, string statusId) => 0;

    /// <inheritdoc />
    public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) => [];
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

    /// <inheritdoc />
    public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) => [];
}
