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
/// 🔒 `05` §3.3 and `11` §4.3 — the Ghost Duel, run through the <b>same code path</b> as a PvE
/// fight. Every rule here is asserted against a real <c>CombatSimulator.Simulate</c>, not against a
/// hand-built evaluation context.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>WHY THIS SUITE EXISTS SEPARATELY FROM <c>PvpConditionTests</c></b> ═══
/// </para>
/// <para>
/// M2-05 pinned `05` §3.3's four <em>condition</em> rulings — <c>TARGET_IS_ELITE</c>/
/// <c>TARGET_IS_BOSS</c> false, <c>ENEMY_COUNT</c> 1, target-conditionals reading the opposing hero,
/// and `18` §9.3's <c>IS_PVP</c> skip — and pinned them well, with a deliberately mislabelled ghost
/// and a stray actor so that a roster-derived answer would fail. Every one of those assertions is
/// made against an <see cref="EffectEvaluationContext"/> <b>constructed by the test</b>. None of them
/// says that a running duel ever produces such a context. This suite closes that gap: it is the
/// simulator's half of the same rulings.
/// </para>
/// <para>
/// ═══ 🔴 <b>THE PROBE TECHNIQUE, AND WHY THE ROSTER IS DELIBERATELY MALFORMED</b> ═══
/// </para>
/// <para>
/// <see cref="Inverted"/> gives the <b>defending</b> side the lower `05` §3.1 indices. No legitimate
/// duel is built that way — <c>CombatActor</c> is explicit that the attacker's side takes the hero
/// block <c>0..3</c> and the defender's side starts at <c>FirstEnemy</c>. It is malformed on purpose,
/// for M2-05's reason and by its precedent: `05` §3.3 states initiative as a rule <b>about sides</b>,
/// not as a consequence of how the indices happen to be laid out, so a probe that could be satisfied
/// by a correctly-built roster would assert nothing.
/// </para>
/// <para>
/// 🔴 <b>And it is not a stylistic choice — on a conventional roster the rule is unobservable.</b>
/// Slot 4 walks non-pets only (`05` §3.2: pets never basic-attack), so a conventional duel's slot 4 is
/// exactly two actors, the attacking hero at index 0 and the defending hero at index 4 — already in
/// attacker-first order under plain index sorting. Changing the ordering rule there changes nothing
/// that any assertion can see. <see cref="The_override_is_invisible_on_a_conventionally_indexed_duel"/>
/// records that finding so it is not re-derived, and it is why every ordering assertion below runs on
/// <see cref="Inverted"/>.
/// </para>
/// </remarks>
public sealed class PvpDuelTests
{
    /// <summary>The attacking player's hero — `11` §5.1's <em>"only the attacker's rating"</em>.</summary>
    private const string Attacker = "HERO";

    /// <summary>The attacking player's pet.</summary>
    private const string AttackerPet = "PET_0";

    /// <summary>The Ghost — a full hero + pets snapshot on <see cref="BattleSide.ENEMY"/> (`05` §3.3).</summary>
    private const string Defender = "HERO_DEFENDER";

    /// <summary>The Ghost's pet.</summary>
    private const string DefenderPet = "GHOST_PET_0";

    /// <summary>`11` §4.3's duel cap in ticks — 60 s at `05` §3's 20 Hz.</summary>
    private const int DuelTicks = 1200;

    /// <summary>
    /// 🔴 A cap that is <b>not</b> a whole number of 1.0-ASPD cooldown cycles. `05` §3.1 slot 4 puts a
    /// 1.0-ASPD actor's <c>attackCooldown</c> back on exactly <c>0.0</c> every 20 ticks, which is the
    /// same reading as "never walked at all" — so an identity assertion on the cooldown is only an
    /// identity away from a cycle boundary.
    /// </summary>
    private const int NotAWholeCooldownCycle = 13;

    // ═══════════════════════════════════════════════════════════ initiative (`05` §3.3)

    /// <summary>
    /// 🔒 `05` §3.3 — <em>"Within a tick: the <b>attacker's side acts first</b> (hero, then pet
    /// abilities), then the defender's side."</em> Slot 4's half.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The assertion distinguishes "acted first" from "acted at all", and it has to.</b> M2-08's
    /// initiative test could not fail because a pet placed in the order still never swings, so
    /// asserting on the swing <em>list</em> asserted the symptom (steering S2). Here both actors swing
    /// in both shapes — the distinct-attacker count below pins that — so the swing list is identical
    /// as a <em>set</em> and differs only in <b>order</b>. Nothing but the ordering rule can move it.
    /// </para>
    /// <para>
    /// 🔒 <b>Two shapes over one roster, with the PvE arm as the negative control.</b> The only
    /// difference between the arms is <c>CombatRules.IsPvp</c>: same actors, same indices, same seed,
    /// same cap. A tick cap of 3 rather than `11` §4.3's 1200 is deliberate — it isolates the ordering
    /// switch from the duration switch, so this test cannot pass because of the cap.
    /// </para>
    /// </remarks>
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
    /// 🔒 `05` §3.3's <em>"(hero, then pet abilities)"</em> — slot 5's half of the same rule. The
    /// attacker's side's pet abilities advance before the defender's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Errata, recorded rather than resolved.</b> `05` §3.1's eight-slot order is 🔒 and puts
    /// <b>every</b> basic attack in slot 4 and <b>every</b> pet ability in slot 5; `05` §3.3's
    /// parenthetical reads as though one side's hero and pets both act before the other side's, which
    /// would interleave the two slots. The reading implemented is the one that leaves <b>both</b>
    /// locked statements true: §3.1 keeps the slots, and §3.3 orders the <em>sides</em> within each
    /// slot — attacker's hero before defender's hero in slot 4, attacker's pets before defender's pets
    /// in slot 5. Interleaving the slots would override a 🔒 order with a parenthetical.
    /// </para>
    /// <para>
    /// The claim is observable for the same reason as slot 4's and by the same probe: on a
    /// conventional roster the two orders coincide, so <see cref="Inverted"/> is what makes the rule
    /// visible at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_attackers_side_pet_abilities_advance_before_the_defenders()
    {
        var inPvE = PetOrder(DuelRules(3, isPvp: false));
        var inADuel = PetOrder(DuelRules(3, isPvp: true));

        inPvE.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
        inADuel.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);

        inPvE.ShouldBe(new[] { DefenderPet, AttackerPet });
        inADuel.ShouldBe(new[] { AttackerPet, DefenderPet });
    }

    /// <summary>
    /// 🔴 The finding this suite's probe technique rests on: on a <b>conventionally</b> indexed duel
    /// roster, `05` §3.3's initiative rule and `05` §3.1's index order produce the <b>same</b>
    /// sequence, so the override is invisible.
    /// </summary>
    /// <remarks>
    /// Recorded as a test rather than as a comment because it is the reason every other ordering
    /// assertion here uses <see cref="Inverted"/>. A reader who "simplifies" those tests onto a
    /// well-formed roster would produce two assertions that pass identically under the rule and
    /// without it — a pair of tests that cannot fail (steering S1). This one fails the moment the
    /// coincidence stops holding, which is the only way to notice that it was a coincidence.
    /// </remarks>
    [Fact]
    public void The_override_is_invisible_on_a_conventionally_indexed_duel()
    {
        var inPvE = SwingOrder(DuelRules(3, isPvp: false), Conventional());
        var inADuel = SwingOrder(DuelRules(3, isPvp: true), Conventional());

        inPvE.ShouldBe(new[] { Attacker, Defender });
        inADuel.ShouldBe(inPvE);
    }

    // ═══════════════════════════════════════════════════════════ the draw order (`11` §6)

    /// <summary>
    /// 🔒 `11` §6 — the tamper check compares <c>LogHash</c> client-vs-server, and initiative decides
    /// <b>which side draws first</b>: `05` §4's steps 1, 4 and 5 (dodge, crit, block) each draw, so the
    /// two <see cref="CombatRules"/> shapes consume the combat stream in a different order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pinned under both shapes, as literal draw indices rather than as "they differ": a rule that only
    /// asserted inequality would be satisfied by any change at all, including one that made the duel's
    /// sequence non-deterministic. `14` §8.1's stream is <c>Hash64(battleSeed, "combat", i)</c>, so the
    /// index a swing draws at <b>is</b> the value it gets.
    /// </para>
    /// <para>
    /// ⚠️ <b><see cref="DrawingAttackPipeline"/> stands in for M2-09, and says so.</b> `05` §4's real
    /// pipeline is not on this branch; what is asserted is the <em>position</em> in the stream at which
    /// each attacker's swing begins, which is a property of slot 4's order and not of the damage
    /// engine. Three draws per swing is `05` §4's own count.
    /// </para>
    /// </remarks>
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
    /// 🔒 The consequence `11` §6 actually checks: the two shapes produce <b>different</b>
    /// <c>LogHash</c>es over the same roster and the same <c>battleSeed</c>.
    /// </summary>
    /// <remarks>
    /// This is why initiative is not a cosmetic ordering. A server that re-ran an honest duel under
    /// `05` §3.1's PvE order would compute a hash the client never produced, and `11` §6 would discard
    /// the result and increment the player's flag — the anti-cheat firing on the anti-cheat.
    /// </remarks>
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

    // ═══════════════════════════════════════════════════════════ ON_KILL (`05` §3.3)

    /// <summary>
    /// 🔒 `05` §3.3 — <c>ON_KILL</c> triggers <em>"never fire in duels. The only death in a duel ends
    /// the fight."</em>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>A 2×2 over the two independent guards, not one probe over their conjunction.</b> The rule
    /// is enforced twice — <c>CombatRules.OnKillTriggersFire</c> gates the loop's firing, and
    /// <c>TriggerInstance.Evaluate</c> refuses an <c>ON_KILL</c> occurrence carrying <c>IsPvp</c>
    /// before it reaches the run-scoped counter (so a duel kill cannot advance <c>PK_MIDAS</c>). A
    /// single duel-vs-PvE probe would pass with <b>either</b> guard deleted. Each off-diagonal cell
    /// below is the other guard alone, so each cell names one guard (steering S2).
    /// </para>
    /// <para>
    /// The observable is the effect that <c>ON_KILL</c> carries reaching <c>IStatusEngine</c> — the one
    /// seam this suite can watch without standing in for M2-09 or M2-10.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, false, true)]    // PvE — the control: the trigger really does fire.
    [InlineData(false, true, false)]   // The duel, as `05` §3.3 configures it.
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
            rules: new CombatRules(MaxTicks: 5, onKillTriggersFire, isPvp)));

        if (expectedToFire)
        {
            fired.ShouldHaveSingleItem("the control arm proves the kill really happened")
                .ShouldBe("ON_KILL_MARK");

            return;
        }

        fired.ShouldBeEmpty("`05` §3.3 — the only death in a duel ends the fight");
    }

    // ═══════════════════════════════════════════════════════════ duration (`11` §4.3)

    /// <summary>
    /// 🔒 `11` §4.3 — <em>"Duration cap: <b>60 s of simulated time</b> … set as
    /// <c>pvpMaxFightSeconds</c> in <c>data/combat_caps.json</c>."</em> The 1200 is <b>derived</b> from
    /// the authored 60, never written down beside it.
    /// </summary>
    /// <remarks>
    /// 🔒 The seconds come from the content document itself rather than from a literal in this file,
    /// so a duel is capped at whatever the data says. Two numbers stating one cap is how the simulator
    /// and the tuner come to disagree about how long a duel is — and `11` §6 re-runs the fight, so a
    /// disagreement is a discarded honest result rather than a balance nudge.
    /// </remarks>
    [Fact]
    public void The_duel_cap_is_the_authored_pvpMaxFightSeconds_turned_into_ticks()
    {
        var authored = CombatCaps.Read(StatFixtures.CombatCapsSnapshot()).PvpMaxFightSeconds;
        authored.ShouldBe(60.0, "11 §4.3 — and the derivation below is only as good as this");

        var duel = CombatRules.Duel(authored, lowerRatedSide: BattleSide.ENEMY);

        duel.MaxTicks.ShouldBe(DuelTicks);
        duel.HorizonSeconds.ShouldBe(60.0);
        duel.OnKillTriggersFire.ShouldBeFalse("05 §3.3");
        duel.IsPvp.ShouldBeTrue("18 §4's IS_PVP");
        duel.ExactTieWinner.ShouldBe(BattleSide.ENEMY, "11 §4.3 — the lower-rated player takes a tie");

        // 🔒 CombatLog.MaxTicks stays the LOG's addressable range: a duel simply does not reach it.
        CombatLog.MaxTicks.ShouldBe(1800);
        Should.NotThrow(() => duel.Validated());
    }

    /// <summary>
    /// 🔒 The cap as the loop enforces it: a duel nobody can win stops at tick 1200, where the same
    /// standoff in PvE runs to `05` §3's 1800.
    /// </summary>
    [Fact]
    public void A_duel_that_nobody_can_win_stops_at_1200_ticks_and_a_PvE_fight_at_1800()
    {
        var duel = Standoff(CombatRules.Duel(60.0, BattleSide.ENEMY), heroHp: 50, defenderHp: 100);
        var pve = Standoff(CombatRules.PvE, heroHp: 50, defenderHp: 100);

        duel.DurationTicks.ShouldBe(DuelTicks);
        duel.Log.Count.ShouldBeGreaterThan(0);
        duel.Log.ShouldAllBe(e => e.Tick < DuelTicks);

        pve.DurationTicks.ShouldBe(CombatLog.MaxTicks);
    }

    // ═══════════════════════════════════════════════════════════ the tie (`11` §4.3)

    /// <summary>
    /// 🔒 `11` §4.3 — <em>"On timeout, the side with the higher remaining HP fraction wins. On an exact
    /// tie, the <b>lower-rated player wins</b> (a small underdog bias that prevents stagnation at the
    /// top)."</em>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The standings are an exact tie on the <em>fraction</em> and not on the HP</b>: the
    /// attacker is at <c>40/100</c> and the Ghost at <c>80/200</c>. Both are 0.40, and the Ghost holds
    /// twice the absolute HP — so a rule that compared HP would not tie here at all, and this test
    /// would be asserting something else.
    /// </para>
    /// <para>
    /// 🔒 <b>The third arm is the negative control and it is the load-bearing one.</b> `11` §4.3's
    /// underdog rule breaks <em>ties</em>; it does not hand the fight to the lower-rated player. The
    /// arm below names the Ghost as the underdog while the attacker is <b>ahead</b> on fraction, and
    /// the attacker still wins. Without it, "the lower-rated player wins" would pass just as happily
    /// if implemented as "the lower-rated player always wins".
    /// </para>
    /// </remarks>
    [Fact]
    public void On_an_exact_tie_at_the_timeout_the_lower_rated_player_wins()
    {
        // The attacker is the lower-rated player: the underdog bias hands it the tie.
        Standoff(CombatRules.Duel(60.0, lowerRatedSide: BattleSide.HERO), heroHp: 40, defenderHp: 80)
            .HeroWon.ShouldBeTrue();

        // The Ghost is the lower-rated player: the same tie goes the other way.
        Standoff(CombatRules.Duel(60.0, lowerRatedSide: BattleSide.ENEMY), heroHp: 40, defenderHp: 80)
            .HeroWon.ShouldBeFalse();

        // 🔒 The negative control: a tie rule that decides a non-tie is not a tie rule.
        Standoff(CombatRules.Duel(60.0, lowerRatedSide: BattleSide.ENEMY), heroHp: 50, defenderHp: 80)
            .HeroWon.ShouldBeTrue("0.50 beats 0.40 — the underdog bias breaks ties, it does not win them");
    }

    /// <summary>
    /// ⚠️ PvE is untouched: `05` §3 authors no tie rule, and <c>BattleOutcomeTests</c> records the
    /// errata that an exact tie is a loss for the hero because a 90 s standoff cleared nothing.
    /// </summary>
    /// <remarks>
    /// Asserted here as well as there because `11` §4.3's rule arrives as a <b>new field</b> on
    /// <see cref="CombatRules"/>, and the one way to get it wrong that no duel test would catch is to
    /// give it a default that changes PvE. <c>CombatRules.PvE</c> names no tie winner, so the
    /// comparison stays strict.
    /// </remarks>
    [Fact]
    public void A_PvE_timeout_tie_is_still_not_a_clear()
    {
        CombatRules.PvE.ExactTieWinner.ShouldBeNull();

        Standoff(CombatRules.PvE, heroHp: 40, defenderHp: 80).HeroWon.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════ the run queue (`18` §2.5)

    /// <summary>
    /// 🔒 `18` §2.5 — in a duel the <c>RunEffectQueued</c> queue is <b>discarded</b>, consistent with
    /// §9.3's <c>IS_PVP</c> skipping. A duel has no run to apply anything to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Both trigger moments, because they are two different constructions of the occurrence.</b>
    /// <c>BattleSimulation</c> builds a <c>TriggerOccurrence</c> in three places — the
    /// <c>ON_BATTLE_START</c> sweep, the <c>ON_BATTLE_END</c> sweep, and the shared factory everything
    /// else uses — and each sets <c>IsPvp</c> itself. An <c>IsPvp</c> that failed to travel on one of
    /// them would queue a run effect out of a duel silently, and `11` §6 would then see a client and a
    /// server disagreeing about a log neither of them tampered with.
    /// </para>
    /// <para>
    /// The PvE arm is the floor: it proves the ops really are `18` §2.5 ops and really do reach the
    /// queue, so the duel arm's emptiness is a discard rather than an effect that never fired.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_duel_discards_the_run_effect_queue()
    {
        EffectOps.IsRunAndBoard(EffectOp.MODIFY_DIE_FACE).ShouldBeTrue("18 §2.5");

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
    /// 🔒 `05` §3.3 — <em>"Each hero targets <b>only the opposing hero</b>. Pets are untargetable and
    /// unkillable on both sides."</em> Verified in a running duel; the mechanism is M2-05's
    /// <c>BattleRoster</c> and is not restated here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The pet assertion is on the cooldown, on M2-08's precedent — and in a duel it is
    /// <em>doubly</em> needed.</b> In PvE, "no pet appears among the attackers" cannot fail: a pet
    /// placed in slot 4's order still never swings, because `05` §3.2's enemy-side selection finds no
    /// opposing hero for it. <b>A duel has one.</b> A pet wrongly admitted to slot 4 would therefore
    /// really swing here — which makes the swing list a live assertion for once, so both are made.
    /// Slot 4b decrements <c>attackCooldown</c> for <b>every</b> actor it walks, fired or not, so a
    /// pet still sitting at pre-tick 0a's <c>0</c> is proof it was never walked.
    /// </para>
    /// <para>
    /// 🔴 <b>The cap is 13 ticks and it must not be a multiple of 20.</b> A 1.0-ASPD actor's cooldown
    /// returns to exactly <c>0.0</c> every 20 ticks (`05` §3.1 slot 4: fire, set <c>1.0/ASPD</c>,
    /// subtract <c>TICK</c>), so at a cycle boundary a pet that <em>was</em> walked reads <c>0.0</c>
    /// too and the identity assertion silently becomes a coincidence. This was found by deliberately
    /// admitting pets to the order and watching the test pass (steering S1); at 13 ticks the walked
    /// reading is <c>0.35</c> and the assertion bites.
    /// </para>
    /// </remarks>
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
    /// 🔒 `18` §9.3 / `05` §3.3 — <em>"gear affixes and perk clauses with no duel meaning are
    /// <b>skipped</b> via the <c>IS_PVP</c> condition, never converted."</em> Live in a running duel,
    /// through `18` §8's aggregation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M2-05 pinned the evaluator's answer; what this adds is that a real fight builds a context
    /// carrying <c>IsPvp</c>, so the skip actually happens to a stat. The effect is a
    /// <c>STAT_ADD_FLAT</c> rather than a gold affix because a gold affix has no combat reading to
    /// observe — the mechanism under test is the skip, not the affix (steering S6: `08` §3's affix
    /// catalogue and `06`'s perk ids are M3/M4's, and none is invented here).
    /// </para>
    /// <para>
    /// The PvE arm is the floor: without it, an effect that never applied in <em>either</em> shape
    /// would read as a successful skip.
    /// </para>
    /// </remarks>
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

    /// <summary>`05` §3.3's bounds at a chosen cap, so a test can isolate one switch at a time.</summary>
    private static CombatRules Rules(int maxTicks, bool isPvp) =>
        new(maxTicks, OnKillTriggersFire: !isPvp, isPvp);

    /// <summary>
    /// The same, named for what the ordering tests use it for: a duel's rules minus `11` §4.3's cap.
    /// </summary>
    private static CombatRules DuelRules(int maxTicks, bool isPvp) => Rules(maxTicks, isPvp);

    /// <summary>
    /// 🔴 The malformed roster — the <b>defending</b> side holds `05` §3.1 indices 0 and 1. See the
    /// type remarks for why no ordering claim is observable without it.
    /// </summary>
    private static IReadOnlyList<ActorPlan> Inverted() =>
        new[]
        {
            DefendingHero(0, BattleTestBench.Stats(maxHp: 10_000)),
            DefendingPet(1),
            AttackingHero(2, BattleTestBench.Stats(maxHp: 10_000)),
            AttackingPet(3),
        };

    /// <summary>The roster a real duel is built with — <c>CombatActor</c>'s layout (`05` §3.3).</summary>
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
    /// 🔒 The Ghost. A full hero — <c>Kind</c> <c>HERO</c> — on <see cref="BattleSide.ENEMY"/>, which is
    /// what makes it the roster's killable enemy rather than a second hero-side hero
    /// (<c>BattlePlan</c>).
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

    /// <summary>Each swing's attacker and the stream position its first `05` §4 draw took.</summary>
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

    /// <summary>
    /// A duel nobody can hurt anybody in, with the standings set once — so the timeout decides it on
    /// exactly the fractions named here.
    /// </summary>
    private static SimulationResult Standoff(CombatRules rules, double heroHp, double defenderHp) =>
        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                AttackingHero(0, BattleTestBench.Stats(maxHp: 100, aspd: 0.001)),
                DefendingHero(CombatActor.FirstEnemy, BattleTestBench.Stats(maxHp: 200, aspd: 0.001)),
            },
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, damage: 0.0),
                Timeline = new DuelStandings(heroHp, defenderHp),
            },
            rules));

    /// <summary>A fight whose two combat triggers each carry a `18` §2.5 run op.</summary>
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

    /// <summary>`17` §9's Dicelord Scramble shape — the sanctioned combat trigger carrying a run op.</summary>
    private static HeldEffect Scramble(string id, TriggerKind kind) =>
        new(new EffectDefinition
        {
            Id = id,
            Op = EffectOp.MODIFY_DIE_FACE,
            Trigger = new EffectTrigger { Kind = kind },
        });

    /// <summary>
    /// The effect ids behind a fight's <c>RunEffectQueued</c> entries, read out of the log through the
    /// battle-local index <c>BattleSimulation</c> assigns them (`18` §8's ascending id order).
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
/// ⚠️ Not <c>WoundedAtStart</c>, and not a duplicate of it: that fixture keys on the PvE id scheme
/// (<c>HERO</c> plus <c>ENEMY_i</c>), and a duel's defending side is a <b>hero</b> with a hero's id.
/// Widening it to take an id map would put a duel concept into `05` §3's fixture; two small doubles
/// with one job each is the cheaper split.
/// </remarks>
internal sealed class DuelStandings : IStatusTimeline
{
    private readonly double _attackerHp;
    private readonly double _defenderHp;

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
/// <see cref="RecordingAttackPipeline"/> with `05` §4's three draws in front of it — steps 1 (dodge),
/// 4 (crit) and 5 (block) — so slot 4's order becomes visible as a position in the combat stream.
/// </summary>
/// <remarks>
/// ⚠️ <b>A decorator, not a second damage engine.</b> It stands in for M2-09 only in the one respect
/// `11` §6 cares about here: that a swing <em>consumes draws</em>, and therefore that which side
/// swings first decides which side's draws come first. Everything else is delegated.
/// </remarks>
internal sealed class DrawingAttackPipeline : IAttackPipeline
{
    /// <summary>`05` §4's three drawing steps: dodge (1), crit (4) and block (5).</summary>
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
