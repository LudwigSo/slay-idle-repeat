using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// The Ghost Duel, run through the same code path as a PvE fight. Every rule here is asserted
/// against a real <c>CombatSimulator.Simulate</c>, not a hand-built evaluation context.
/// </summary>
/// <remarks>
/// <see cref="Inverted"/> gives the defending side the lower indices, which no legitimate duel does.
/// It is malformed on purpose: on a conventional roster the ordering rule is unobservable, and an
/// assertion would pass with the rule deleted — see
/// <see cref="The_override_is_invisible_on_a_conventionally_indexed_duel"/>. That is also why this
/// suite sits on the internal <c>Simulate(BattlePlan)</c>: the public <c>SimulateDuel</c>
/// (<c>DuelEntryPointTests</c>) always builds the conventional roster, so it cannot express the one
/// shape on which initiative is visible.
/// </remarks>
public sealed class PvpDuelTests
{
    private const string Attacker = "HERO";
    private const string AttackerPet = "PET_0";

    /// <summary>The Ghost — a full hero + pets snapshot on <see cref="BattleSide.ENEMY"/>.</summary>
    private const string Defender = "HERO_DEFENDER";

    private const string DefenderPet = "GHOST_PET_0";

    private const double DuelSeconds = 60.0;
    private const int DuelTicks = 1200;

    /// <summary>
    /// A cap that is not a whole number of 1.0-ASPD cooldown cycles: a 1.0-ASPD actor's
    /// <c>attackCooldown</c> returns to exactly 0.0 every 20 ticks, the same reading as "never walked
    /// at all", so an identity assertion on the cooldown must avoid a cycle boundary.
    /// </summary>
    private const int NotAWholeCooldownCycle = 13;

    // ═══════════════════════════════════════════════════════════ initiative

    /// <summary>
    /// Within a tick, the attacker's side acts first (hero, then pet abilities), then the defender's
    /// side. Both actors swing in both shapes, so the swing list differs only in order — the PvE arm
    /// is the negative control.
    /// </summary>
    [Fact]
    public void The_attackers_side_swings_first_even_when_the_defender_holds_the_lower_index()
    {
        var inPvE = SwingOrder(DuelRules(3, isPvp: false));
        var inADuel = SwingOrder(DuelRules(3, isPvp: true));

        // Both actors swung in both shapes: the difference below is ORDER, never presence.
        inPvE.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
        inADuel.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);

        inPvE.ShouldBe(
            new[] { Defender, Attacker },
            "`05` §3.1's PvE order is the index order, and this roster's lower index is the defender's");

        inADuel.ShouldBe(
            new[] { Attacker, Defender },
            "`05` §3.3 — the attacker's side acts first, whatever the indices say");
    }

    /// <summary>
    /// The attacker's side's pet abilities advance before the defender's — sides are ordered within
    /// each existing slot rather than interleaving hero and pet slots across sides.
    /// </summary>
    [Fact]
    public void The_attackers_side_pet_abilities_advance_before_the_defenders()
    {
        var inPvE = PetOrder(DuelRules(3, isPvp: false));
        var inADuel = PetOrder(DuelRules(3, isPvp: true));

        inPvE.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
        inADuel.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);

        inPvE.ShouldBe(
            new[] { DefenderPet, AttackerPet },
            customMessage: "`05` §3.1's slot 5 is the roster's own order, and this roster's lower index " +
                "is the defender's pet");

        inADuel.ShouldBe(
            new[] { AttackerPet, DefenderPet },
            customMessage: "`05` §3.3 — the attacker's side's pet abilities go first, whatever the " +
                "indices say");
    }

    /// <summary>
    /// On a conventionally indexed duel roster, the initiative rule and the index order produce the
    /// same sequence, so the override is invisible there — the reason <see cref="Inverted"/> exists.
    /// </summary>
    [Fact]
    public void The_override_is_invisible_on_a_conventionally_indexed_duel()
    {
        var inPvE = SwingOrder(DuelRules(3, isPvp: false), Conventional());
        var inADuel = SwingOrder(DuelRules(3, isPvp: true), Conventional());

        inPvE.ShouldBe(new[] { Attacker, Defender });
        inADuel.ShouldBe(inPvE);
    }

    // ═══════════════════════════════════════════════════════════ the draw order

    /// <summary>
    /// Initiative decides which side draws first: the dodge/crit/block draws each swing takes mean
    /// the two <see cref="CombatRules"/> shapes consume the combat stream in a different order.
    /// Literal draw indices rather than "they differ", since asserting inequality alone would be
    /// satisfied by any change, including a non-deterministic one.
    /// </summary>
    [Fact]
    public void The_draw_order_is_pinned_under_both_CombatRules_shapes()
    {
        DrawOrder(DuelRules(1, isPvp: false)).ShouldBe(
            new[] { (Defender, 0UL), (Attacker, 3UL) },
            "PvE: the lower index draws first, and each swing spends `05` §4's three draws");

        DrawOrder(DuelRules(1, isPvp: true)).ShouldBe(
            new[] { (Attacker, 0UL), (Defender, 3UL) },
            "`05` §3.3: the attacker's side draws first");
    }

    /// <summary>
    /// The two shapes produce different <c>LogHash</c>es over the same roster and seed. Why
    /// initiative is not cosmetic: a server re-running an honest duel under the PvE order would
    /// compute a hash the client never produced, and the tamper check would flag the player.
    /// </summary>
    [Fact]
    public void The_two_shapes_do_not_produce_the_same_LogHash()
    {
        var pve = Fight(DuelRules(3, isPvp: false));
        var duel = Fight(DuelRules(3, isPvp: true));

        pve.Log.Count.ShouldBeGreaterThan(0);
        duel.Log.Count.ShouldBe(pve.Log.Count, "the same events happen, in a different order");

        duel.LogHash.ShouldNotBe(
            pve.LogHash,
            "`11` §6 compares LogHash to detect tampering; an initiative change the hash cannot see " +
            "would let a client re-order the fight for free");
    }

    // ═══════════════════════════════════════════════════════════ ON_KILL

    /// <summary>
    /// <c>ON_KILL</c> triggers never fire in duels — the only death in a duel ends the fight. A 2×2
    /// because the rule is enforced twice — <c>CombatRules.OnKillTriggersFire</c> gates the loop, and
    /// <c>TriggerInstance.Evaluate</c> refuses an <c>ON_KILL</c> carrying <c>IsPvp</c> — so a single
    /// duel-vs-PvE probe would pass with either deleted.
    /// </summary>
    [Theory]
    [InlineData(true, false, true)]    // PvE — the control: the trigger really does fire.
    [InlineData(false, true, false)]   // The duel, as it is really configured.
    [InlineData(true, true, false)]    // IsPvp alone — TriggerInstance's guard.
    [InlineData(false, false, false)]  // OnKillTriggersFire alone — the loop's guard.
    public void ON_KILL_never_fires_in_a_duel(bool onKillTriggersFire, bool isPvp, bool expectedToFire)
    {
        var fired = new List<string>();

        var onKill = new HeldEffect(
            new EffectDefinition
            {
                Id = "ON_KILL_MARK",
                Op = EffectOp.APPLY_STATUS,
                StatusId = "RAGE",
                Value = 0.1,
                Target = EffectTarget.SELF,
                Trigger = new EffectTrigger { Kind = TriggerKind.ON_KILL },
            },
            EffectInstanceId.Of("HERO#ON_KILL_MARK"));

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                AttackingHero(0, BattleTestBench.Stats(maxHp: 10_000), onKill),
                DefendingHero(CombatActor.FirstEnemy, BattleTestBench.Stats(maxHp: 10)),
            },
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, damage: 10.0),
                Statuses = new CapturingStatusEngine(fired),
            },
            rules: new CombatRules(
                MaxTicks: 5, onKillTriggersFire,
                ExactTieWinner: isPvp ? BattleSide.ENEMY : null)));

        if (expectedToFire)
        {
            fired.ShouldHaveSingleItem("the control arm proves the kill really happened")
                .ShouldBe("ON_KILL_MARK");

            return;
        }

        fired.ShouldBeEmpty("`05` §3.3 — the only death in a duel ends the fight");
    }

    // ═══════════════════════════════════════════════════════════ duration

    /// <summary>A duel nobody can win stops at tick 1200, where the same standoff in PvE runs to 1800.</summary>
    [Fact]
    public void A_duel_that_nobody_can_win_stops_at_1200_ticks_and_a_PvE_fight_at_1800()
    {
        // The standings are identical, so nothing but the cap can move the duration.
        Standoff(Underdog(BattleSide.ENEMY), heroHp: 50, defenderHp: 100)
            .DurationTicks.ShouldBe(DuelTicks);

        Standoff(CombatRules.PvE, heroHp: 50, defenderHp: 100)
            .DurationTicks.ShouldBe(CombatLog.MaxTicks, "05 §3's 90 s, which the duel overrides");
    }

    // ═══════════════════════════════════════════════════════════ the tie

    /// <summary>
    /// On timeout the higher remaining HP fraction wins; on an exact tie the lower-rated player does.
    /// The standings tie on the fraction, not the HP: 40/100 against 80/200. Two negative controls,
    /// one per direction, since the underdog rule breaks ties but does not hand the fight outright.
    /// </summary>
    [Fact]
    public void On_an_exact_tie_at_the_timeout_the_lower_rated_player_wins()
    {
        // The attacker is the lower-rated player: the underdog bias hands it the tie.
        Standoff(Underdog(BattleSide.HERO), heroHp: 40, defenderHp: 80)
            .HeroWon.ShouldBeTrue();

        // The Ghost is the lower-rated player: the same tie goes the other way.
        Standoff(Underdog(BattleSide.ENEMY), heroHp: 40, defenderHp: 80)
            .HeroWon.ShouldBeFalse();

        // Negative control, Ghost as underdog: the attacker is ahead and still wins.
        Standoff(Underdog(BattleSide.ENEMY), heroHp: 50, defenderHp: 80)
            .HeroWon.ShouldBeTrue("0.50 beats 0.40 — the underdog bias breaks ties, it does not win them");

        // Negative control, attacker as underdog: the attacker is behind and still loses.
        Standoff(Underdog(BattleSide.HERO), heroHp: 30, defenderHp: 80)
            .HeroWon.ShouldBeFalse("0.30 loses to 0.40 — naming a side the underdog does not win it the fight");
    }

    /// <summary>
    /// PvE is untouched by the tie-winner field: the one way to get it wrong that no duel test would
    /// catch is to give it a default that changes PvE, so this is asserted here as well as there.
    /// </summary>
    [Fact]
    public void A_PvE_timeout_tie_is_still_not_a_clear() =>
        Standoff(CombatRules.PvE, heroHp: 40, defenderHp: 80).HeroWon.ShouldBeFalse();

    /// <summary>
    /// The slight attacker edge as an outcome, not just a log order: when both heroes can one-shot
    /// each other, the side that swings first wins — and under attacker-first initiative, two heroes
    /// never trade fatal blows in the same tick.
    /// </summary>
    [Fact]
    public void The_attackers_first_swing_wins_a_race_to_one_shot()
    {
        OneShotRace(DuelRules(3, isPvp: true))
            .HeroWon.ShouldBeTrue("`05` §3.3 — the attacker's side acts first, so it lands the kill");

        OneShotRace(DuelRules(3, isPvp: false))
            .HeroWon.ShouldBeFalse("index order on this roster puts the defender first, and it wins");
    }

    /// <summary>
    /// A mutual death is an attacker loss, whoever the underdog is — the one path where the attacker
    /// edge reverses, since the underdog bias is scoped to the timeout, not a mutual kill.
    /// </summary>
    [Fact]
    public void A_mutual_death_in_a_duel_is_an_attacker_loss()
    {
        foreach (var underdog in new[] { BattleSide.HERO, BattleSide.ENEMY })
        {
            var result = Standoff(Underdog(underdog), heroHp: 0, defenderHp: 0);

            result.HeroWon.ShouldBeFalse($"a mutual death is a defeat with {underdog} named the underdog");
            result.HeroHpRemaining.ShouldBe(0.0);
        }
    }

    // ═══════════════════════════════════════════════════════════ the run queue

    /// <summary>
    /// In a duel the <c>RunEffectQueued</c> queue is discarded — a duel has no run to apply anything
    /// to. The PvE arm is the floor: it proves the ops reach the queue at all, so the duel arm's
    /// emptiness is a discard rather than an effect that never fired.
    /// </summary>
    [Fact]
    public void A_duel_discards_the_run_effect_queue()
    {
        EffectOps.IsRunAndBoard(EffectOp.MOVE_NODES).ShouldBeTrue("18 §2.5");

        var pve = QueueFight(DuelRules(3, isPvp: false));
        var duel = QueueFight(DuelRules(3, isPvp: true));

        Queued(pve).ShouldBe(
            new[] { "SCRAMBLE_AT_START", "SCRAMBLE_AT_END" },
            customMessage: "18 §2.5 — a combat trigger carrying a run op is emitted, never resolved");

        Queued(duel).ShouldBeEmpty("18 §2.5 — in a duel the queue is discarded");

        duel.LogHash.ShouldNotBe(pve.LogHash, "a discarded event is an event the hash must not carry");
    }

    // ═══════════════════════════════════════════════════════════ targeting and IS_PVP

    /// <summary>
    /// Each hero targets only the opposing hero; pets are untargetable and unkillable on both sides.
    /// In PvE this cannot fail (there is no opposing hero for a pet to target), so a duel is the only
    /// shape where a wrongly admitted pet would actually swing.
    /// </summary>
    [Fact]
    public void Each_hero_targets_only_the_opposing_hero_and_neither_sides_pets_are_ever_touched()
    {
        RecordingAttackPipeline? pipeline = null;
        BattleServices? services = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            Inverted(),
            s =>
            {
                services = s;
                pipeline = new RecordingAttackPipeline(s, damage: 1.0);

                return BattleSeams.Strict with { Attack = pipeline };
            },
            rules: DuelRules(NotAWholeCooldownCycle, isPvp: true)));

        pipeline.ShouldNotBeNull();
        services.ShouldNotBeNull();

        pipeline.Swings.Count.ShouldBeGreaterThan(0);

        pipeline.Swings.Select(s => s.Attacker).Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ShouldBe(new[] { Attacker, Defender });

        pipeline.Swings.Where(s => s.Attacker == Attacker)
            .ShouldAllBe(s => s.Defender == Defender);
        pipeline.Swings.Where(s => s.Attacker == Defender)
            .ShouldAllBe(s => s.Defender == Attacker);

        var pets = services.Actors.Where(a => a.Kind == EffectActorKind.PET).ToArray();
        pets.Select(p => p.Id).ShouldBe(new[] { DefenderPet, AttackerPet });
        pets.ShouldAllBe(p => p.AttackCooldown == 0.0);
    }

    /// <summary>
    /// Clauses with no duel meaning are skipped via <c>IS_PVP</c>, never converted, live in a running
    /// duel through aggregation. The PvE arm is the floor: without it, an effect that never applied in
    /// either shape would read as a successful skip.
    /// </summary>
    [Fact]
    public void The_IS_PVP_skip_is_live_in_a_running_duel_and_not_only_in_the_evaluator()
    {
        var outOfDuels = new HeldEffect(new EffectDefinition
        {
            Id = "AFFIX_WITH_NO_DUEL_MEANING",
            Op = EffectOp.STAT_ADD_FLAT,
            Stat = StatSelector.Of(StatId.MAX_HP),
            Value = 500.0,
            Target = EffectTarget.SELF,
            Condition = EffectCondition.Not(EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.IS_PVP,
                Comparator = ConditionComparator.EQ,
                Flag = true,
            })),
        });

        AttackerMaxHp(DuelRules(3, isPvp: false), outOfDuels)
            .ShouldBe(600.0, "18 §8 step 2 — the affix is active outside a duel");

        AttackerMaxHp(DuelRules(3, isPvp: true), outOfDuels)
            .ShouldBe(100.0, "18 §9.3 — skipped in a duel, never converted");
    }

    // ═══════════════════════════════════════════════════════════ fixtures

    /// <summary>
    /// A fight's bounds with everything except <see cref="CombatRules.IsPvp"/> held constant, so a
    /// two-shape probe isolates that one switch. The cap is deliberately not the duel's real 1200, so
    /// an ordering claim cannot pass because of the duration switch instead.
    /// </summary>
    private static CombatRules DuelRules(int maxTicks, bool isPvp) =>
        new(maxTicks, OnKillTriggersFire: false, ExactTieWinner: isPvp ? BattleSide.ENEMY : null);

    /// <summary>
    /// The malformed roster — the defending side holds the low indices. See the type remarks for why
    /// no ordering claim is observable without it.
    /// </summary>
    private static IReadOnlyList<ActorPlan> Inverted() =>
        new[]
        {
            DefendingHero(0, BattleTestBench.Stats(maxHp: 10_000)),
            DefendingPet(1),
            AttackingHero(2, BattleTestBench.Stats(maxHp: 10_000)),
            AttackingPet(3),
        };

    /// <summary>The roster a real duel is built with — <c>CombatActor</c>'s layout.</summary>
    private static IReadOnlyList<ActorPlan> Conventional() =>
        new[]
        {
            AttackingHero(0, BattleTestBench.Stats(maxHp: 10_000)),
            AttackingPet(1),
            DefendingHero(CombatActor.FirstEnemy, BattleTestBench.Stats(maxHp: 10_000)),
            DefendingPet(CombatActor.FirstEnemy + 1),
        };

    private static ActorPlan AttackingHero(
        int index, ActorStats? stats = null, params HeldEffect[] effects) =>
        BattleTestBench.Hero(stats, 1, effects) with { Index = index };

    private static ActorPlan AttackingPet(int index) => BattleTestBench.Pet(0) with { Index = index };

    /// <summary>
    /// The Ghost. A full hero on <see cref="BattleSide.ENEMY"/>, which is what makes it the roster's
    /// killable enemy rather than a second hero-side hero.
    /// </summary>
    private static ActorPlan DefendingHero(
        int index, ActorStats? stats = null, params HeldEffect[] effects) =>
        BattleTestBench.Hero(stats, 1, effects) with
        {
            Id = Defender,
            Index = index,
            LogId = CombatActor.FirstEnemy,
            Side = BattleSide.ENEMY,
        };

    private static ActorPlan DefendingPet(int index) =>
        BattleTestBench.Pet(0) with
        {
            Id = DefenderPet,
            Index = index,
            LogId = CombatActor.FirstEnemy + 1,
            Side = BattleSide.ENEMY,
        };

    private static SimulationResult Fight(CombatRules rules, IReadOnlyList<ActorPlan>? roster = null) =>
        CombatSimulator.Simulate(BattleTestBench.Plan(
            roster ?? Inverted(),
            services => BattleSeams.Strict with { Attack = new RecordingAttackPipeline(services, 1.0) },
            rules));

    /// <summary>Who swung, in slot 4's call order, on the first tick.</summary>
    private static string[] SwingOrder(CombatRules rules, IReadOnlyList<ActorPlan>? roster = null)
    {
        RecordingAttackPipeline? pipeline = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            roster ?? Inverted(),
            services =>
            {
                pipeline = new RecordingAttackPipeline(services, damage: 1.0);

                return BattleSeams.Strict with { Attack = pipeline };
            },
            rules));

        pipeline.ShouldNotBeNull();

        return pipeline.Swings.Where(s => s.Tick == 0).Select(s => s.Attacker).ToArray();
    }

    /// <summary>Which pets slot 5 advanced, in call order, on the first tick.</summary>
    private static string[] PetOrder(CombatRules rules)
    {
        var pets = new RecordingPets();

        CombatSimulator.Simulate(BattleTestBench.Plan(
            Inverted(),
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, damage: 1.0),
                Pets = pets,
            },
            rules));

        return pets.Calls
            .Where(c => c.EndsWith("@0", StringComparison.Ordinal))
            .Select(c => c.Split(':')[1].Split('@')[0])
            .ToArray();
    }

    /// <summary>Each swing's attacker and the stream position its first draw took.</summary>
    private static (string Attacker, ulong FirstDraw)[] DrawOrder(CombatRules rules)
    {
        DrawingAttackPipeline? pipeline = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            Inverted(),
            services =>
            {
                pipeline = new DrawingAttackPipeline(services);

                return BattleSeams.Strict with { Attack = pipeline };
            },
            rules));

        pipeline.ShouldNotBeNull();

        return pipeline.Draws.ToArray();
    }

    /// <summary>Duel bounds naming one side as the lower-rated player.</summary>
    private static CombatRules Underdog(BattleSide lowerRated) =>
        CombatRules.Duel(DuelSeconds, lowerRated);

    /// <summary>
    /// Both heroes on <see cref="Inverted"/>, each able to one-shot the other — so the acting order
    /// alone decides the fight.
    /// </summary>
    private static SimulationResult OneShotRace(CombatRules rules) =>
        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                DefendingHero(0, BattleTestBench.Stats(maxHp: 10)),
                AttackingHero(2, BattleTestBench.Stats(maxHp: 10)),
            },
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, damage: 10.0),
            },
            rules));

    /// <summary>
    /// A duel nobody can hurt anybody in, with the standings set once. The standings are read back
    /// before the outcome is returned: with bars 100 and 200, a timeline that set nothing would leave
    /// both sides at 1.0 — still an exact tie — and the fixture would silently never have run.
    /// </summary>
    private static SimulationResult Standoff(CombatRules rules, double heroHp, double defenderHp)
    {
        BattleServices? services = null;

        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                AttackingHero(0, BattleTestBench.Stats(maxHp: 100, aspd: 0.001)),
                DefendingHero(CombatActor.FirstEnemy, BattleTestBench.Stats(maxHp: 200, aspd: 0.001)),
            },
            s =>
            {
                services = s;

                return BattleSeams.Strict with
                {
                    Attack = new RecordingAttackPipeline(s, damage: 0.0),
                    Timeline = new DuelStandings(heroHp, defenderHp),
                };
            },
            rules));

        services.ShouldNotBeNull();

        services.Actors.Single(a => a.Id == Attacker).CurrentHp.ShouldBe(heroHp);
        services.Actors.Single(a => a.Id == Defender).CurrentHp.ShouldBe(defenderHp);

        return result;
    }

    /// <summary>A fight whose two combat triggers each carry a run op.</summary>
    private static SimulationResult QueueFight(CombatRules rules) =>
        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                AttackingHero(
                    0,
                    BattleTestBench.Stats(maxHp: 10_000),
                    Scramble("SCRAMBLE_AT_START", TriggerKind.ON_BATTLE_START),
                    Scramble("SCRAMBLE_AT_END", TriggerKind.ON_BATTLE_END)),
                DefendingHero(CombatActor.FirstEnemy, BattleTestBench.Stats(maxHp: 10_000)),
            },
            services => BattleSeams.Strict with { Attack = new RecordingAttackPipeline(services, 1.0) },
            rules));

    /// <summary>The sanctioned shape: a combat trigger carrying a run/board op.</summary>
    private static HeldEffect Scramble(string id, TriggerKind kind) =>
        new(new EffectDefinition
        {
            Id = id,
            Op = EffectOp.MOVE_NODES,
            Trigger = new EffectTrigger { Kind = kind },
        });

    /// <summary>
    /// The effect ids behind a fight's <c>RunEffectQueued</c> entries, read out of the log through the
    /// battle-local index <c>BattleSimulation</c> assigns them.
    /// </summary>
    private static string[] Queued(SimulationResult result)
    {
        // The battle's effect table is every authored id in the opening roster, distinct, in ascending
        // ordinal order — which for these two ids is alphabetical.
        var table = new[] { "SCRAMBLE_AT_END", "SCRAMBLE_AT_START" };

        return result.Log
            .Where(e => e.Type == CombatEventType.RunEffectQueued)
            .Select(e => table[e.DataId])
            .ToArray();
    }

    /// <summary>The attacker's aggregated Max HP after a one-tick fight.</summary>
    private static double AttackerMaxHp(CombatRules rules, HeldEffect held)
    {
        BattleServices? services = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                AttackingHero(0, BattleTestBench.Stats(maxHp: 100), held),
                DefendingHero(CombatActor.FirstEnemy, BattleTestBench.Stats(maxHp: 10_000)),
            },
            s =>
            {
                services = s;

                return BattleSeams.Strict with { Attack = new RecordingAttackPipeline(s, damage: 1.0) };
            },
            rules));

        services.ShouldNotBeNull();

        return services.Actors.Single(a => a.Id == Attacker).Stats[StatId.MAX_HP];
    }
}

/// <summary>
/// Sets both duellists' HP once, on tick 0's slot 1 — a duel already in progress, so the timeout
/// decides on exactly the standings the test named.
/// </summary>
/// <remarks>
/// Not <c>WoundedAtStart</c>, and not a duplicate of it: that fixture keys on the PvE id scheme, and
/// a duel's defending side is a hero with a hero's id.
/// </remarks>
internal sealed class DuelStandings : IStatusTimeline
{
    private readonly double _attackerHp;
    private readonly double _defenderHp;

    /// <inheritdoc />
    /// <remarks>
    /// Empty is correct here and not a stub: these fixtures set HP directly to stage the tie cases
    /// and carry no statuses at all.
    /// </remarks>
    public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) => [];

    internal DuelStandings(double attackerHp, double defenderHp)
    {
        _attackerHp = attackerHp;
        _defenderHp = defenderHp;
    }

    /// <inheritdoc />
    public void AdvanceTimers(BattleActor actor, int tick)
    {
        if (tick != 0)
        {
            return;
        }

        if (actor.Side == BattleSide.HERO && actor.Kind == EffectActorKind.HERO)
        {
            actor.SetCurrentHp(_attackerHp);
        }
        else if (actor.Side == BattleSide.ENEMY && actor.Kind == EffectActorKind.HERO)
        {
            actor.SetCurrentHp(_defenderHp);
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

/// <summary>
/// <see cref="RecordingAttackPipeline"/> with the three draws (dodge, crit, block) in front of it, so
/// swing order becomes visible as a position in the combat stream.
/// </summary>
/// <remarks>
/// A decorator, not a second damage engine: it stands in for the real pipeline only in the one
/// respect that matters here — that a swing consumes draws, so which side swings first decides which
/// side's draws come first.
/// </remarks>
internal sealed class DrawingAttackPipeline : IAttackPipeline
{
    private const int DrawsPerSwing = 3;

    private readonly BattleServices _services;
    private readonly RecordingAttackPipeline _inner;

    internal DrawingAttackPipeline(BattleServices services)
    {
        _services = services;
        _inner = new RecordingAttackPipeline(services, damage: 1.0);
    }

    /// <summary>Each swing's attacker and the stream position its first draw took, in call order.</summary>
    internal List<(string Attacker, ulong FirstDraw)> Draws { get; } = new();

    /// <inheritdoc />
    public AttackResolution ResolveAttack(
        IEffectActorView attacker, IEffectActorView defender, double attackMultiplier, string sourceEffectId)
    {
        Draws.Add((attacker.Id, _services.Rng.Position));

        for (var i = 0; i < DrawsPerSwing; i++)
        {
            _services.Rng.NextDouble();
        }

        return _inner.ResolveAttack(attacker, defender, attackMultiplier, sourceEffectId);
    }

    /// <inheritdoc />
    public void DealTrueDamage(IEffectActorView target, double amount, string sourceEffectId) =>
        _inner.DealTrueDamage(target, amount, sourceEffectId);

    /// <inheritdoc />
    public void DealMaxHpPctDamage(
        IEffectActorView target, double amount, bool bypassesWards, string sourceEffectId) =>
        _inner.DealMaxHpPctDamage(target, amount, bypassesWards, sourceEffectId);

    /// <inheritdoc />
    public void Heal(IEffectActorView target, double amount, string sourceEffectId) =>
        _inner.Heal(target, amount, sourceEffectId);

    /// <inheritdoc />
    public void GrantWard(
        IEffectActorView target, double amount, double? sourceCapPct, string sourceEffectId) =>
        _inner.GrantWard(target, amount, sourceCapPct, sourceEffectId);

    /// <inheritdoc />
    public void AddThorns(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId) =>
        _inner.AddThorns(target, fraction, duration, sourceEffectId);
}
