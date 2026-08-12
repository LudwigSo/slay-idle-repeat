using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1 / §11 — <em>"every damaging mechanic has a visible 1.0–1.5 s wind-up"</em>, emitted
/// ahead of the firing it announces, exactly once per firing.
/// </summary>
/// <remarks>
/// The lead is <b>boss-script data</b>: `17` authors 1.2 s for Thornmaw's Root (§2) and 1.5 s for
/// the Dicelord's All In (§9), so a single engine constant would be wrong for one of them and a `18`
/// §3 trigger key would put a presentation duration inside the firing model.
/// </remarks>
public sealed class BossTelegraphTests
{
    private const string AllInInstance = "BOSS_DICELORD#P3#BOSS_DICELORD_P3_ALL_IN";

    // ════════════════════════════════════════════════════ 1 · the emission

    /// <summary>
    /// 🔒 One <c>Telegraph</c> per firing, on the tick <c>firing − lead</c>, naming the mechanic's
    /// battle-local effect index and carrying the lead in <b>seconds</b>.
    /// </summary>
    /// <remarks>
    /// Phase 3 is entered at tick 100, so R8 anchors All In there and its 8 s period first fires at
    /// tick 260. A 1.5 s lead is 30 ticks, so the wind-up is emitted at tick 230.
    /// </remarks>
    [Fact]
    public void A_telegraphed_mechanic_emits_one_wind_up_per_firing_at_firing_minus_lead()
    {
        var result = Fight((Tick: 100, ActorId: BossTestBench.Dicelord, Fraction: 0.20)).Result;

        var telegraphs = BossTestBench.Telegraphs(result.Log);

        telegraphs.Count.ShouldBe(
            1, "one firing inside the fight, so exactly one wind-up — never two for one landing");

        var announced = telegraphs[0];

        announced.Tick.ShouldBe(230, "the firing is at 100 + 8 s x 20; the lead is 1.5 s x 20");
        announced.SourceId.ShouldBe(CombatActor.Enemy(0), "the winding-up actor");
        announced.TargetId.ShouldBe(CombatActor.Hero, "who it will hit");
        announced.Value.ShouldBe(1.5, "the wind-up in SECONDS — the replayer scales it by 05 §8's toggle");

        // 🔴 The index is asserted as 1, and the boss carries a mechanic that sorts BEFORE All In for
        //    exactly that reason. `CombatLog.NoDataId` is 0, so an assertion of 0 here could not tell
        //    "the effect index of All In" from "the emitter wrote no content id at all" — the two are
        //    the same byte (steering S1).
        CombatLog.NoDataId.ShouldBe((ushort)0, "which is what makes an index of 0 undiscriminating");

        announced.DataId.ShouldBe(
            (ushort)1,
            "the battle-local effect index of BOSS_DICELORD_P3_ALL_IN: the plan's ids sort " +
            "A_DICELORD_P1_ANTE, BOSS_DICELORD_P3_ALL_IN, SYS_ENRAGE, so All In is the second");
    }

    /// <summary>
    /// 🔒 The floor under the index above: the battle-local effect table is the one
    /// <c>BuildEffectIndex</c> makes from the <b>opening roster</b>, in ascending `18` §8 effect-id
    /// order, and All In really is at position 1 in it.
    /// </summary>
    /// <remarks>
    /// It is stated here rather than inside the emission case because the emission case is about
    /// <em>which tick</em>; this is about <em>which number</em>, and the two fail for different
    /// reasons. `05` §7's replayer rebuilds this same table from the same roster, which is why a
    /// phase-3 mechanic has to be on the opening plan at all.
    /// </remarks>
    [Fact]
    public void All_Ins_position_in_the_battles_effect_table_is_the_one_the_wind_up_names()
    {
        var ids = BossPlan().Effects
                            .Select(h => h.Effect.Id)
                            .Distinct(StringComparer.Ordinal)
                            .OrderBy(id => id, EffectOrder.IdComparer)
                            .ToArray();

        ids.ShouldBe(
            new[] { "A_DICELORD_P1_ANTE", "BOSS_DICELORD_P3_ALL_IN", BossBuiltIns.EnrageId },
            Case.Sensitive,
            "three effects, and All In is not first — so its index is not the byte that also means " +
            "'names no content'");

        Array.IndexOf(ids, "BOSS_DICELORD_P3_ALL_IN").ShouldBe(1);
    }

    /// <summary>
    /// 🔒 The wind-up strictly precedes its own landing, which is the property that makes it a
    /// wind-up at all rather than a second event at the same moment.
    /// </summary>
    [Fact]
    public void The_wind_up_precedes_the_hit_it_announces()
    {
        var result = Fight((Tick: 100, ActorId: BossTestBench.Dicelord, Fraction: 0.20)).Result;

        var telegraphs = BossTestBench.Telegraphs(result.Log);
        var hits = result.Log.Where(e => e.Type == CombatEventType.Hit && e.Tick > 100).ToArray();

        telegraphs.Count.ShouldBe(1, "the floor under both assertions below");
        hits.Length.ShouldBe(1, "and the floor under the landing");

        telegraphs[0].Tick.ShouldBeLessThan(hits[0].Tick);
        (hits[0].Tick - telegraphs[0].Tick).ShouldBe(30, "1.5 s at 20 ticks/second");
    }

    /// <summary>
    /// 🔒 A phase-2 mechanic emits nothing while the boss is in phase 1 — the wind-up pass only ever
    /// looks at the <b>current</b> phase's active instances.
    /// </summary>
    [Fact]
    public void A_mechanic_of_a_phase_the_boss_is_not_in_announces_nothing()
    {
        var run = Fight();

        // 🔒 The floors under an emptiness claim (steering S3): the fight ran long enough that the
        //    wind-up WOULD have been emitted had the phase been entered, and the mechanic really is
        //    on the plan rather than absent — an empty log and an absent effect would both satisfy
        //    the assertion below while proving nothing.
        run.Result.Log.Count.ShouldBeGreaterThan(0, "there IS a fight");
        run.Driver.At(0, AllInInstance).IsRegistered.ShouldBeTrue(
            "the phase-3 mechanic is on the OPENING roster — it is de-anchored, not missing");

        BossTestBench.Telegraphs(run.Result.Log).ShouldBeEmpty(
            "the boss never left phase 1, so its phase-3 All In never scheduled a firing");
    }

    // ════════════════════════════════════════════════════ 2 · T1 — the band

    /// <summary>
    /// 🔒 <b>T1</b> — a lead outside `17` §1's 1.0–1.5 s band is refused <b>at encounter-build
    /// time</b>, by the builder's own message rather than by
    /// <c>CombatLog.AppendTelegraph</c> mid-fight.
    /// </summary>
    [Theory]
    [InlineData(0.9, "too short to read")]
    [InlineData(1.6, "too long to read as a wind-up")]
    public void A_lead_outside_the_1_0_to_1_5_second_band_is_refused_by_the_builder(
        double lead, string why)
    {
        var thrown = Should.Throw<EffectContextException>(() => Build(lead));

        thrown.Message.ShouldContain("T1", Case.Sensitive, $"which rule fired — {why}");
        thrown.Message.ShouldContain(BossTestBench.Dicelord, Case.Sensitive, "which boss");
        thrown.Message.ShouldContain("BOSS_DICELORD_P3_ALL_IN", Case.Sensitive, "which mechanic");
        thrown.Message.ShouldContain("3", Case.Sensitive, "which phase");
    }

    /// <summary>
    /// 🔒 <b>T1's second shape</b> — a lead inside the band that is <b>not a whole tick</b>.
    /// `05` §3's simulation is fixed-tick, so 1.03 s is 20.6 ticks: it points between two ticks and
    /// therefore at neither.
    /// </summary>
    [Fact]
    public void A_lead_that_is_not_a_whole_tick_is_refused_even_though_it_is_inside_the_band()
    {
        var thrown = Should.Throw<EffectContextException>(() => Build(1.03));

        thrown.Message.ShouldContain("T1", Case.Sensitive);
        thrown.Message.ShouldContain(
            "20.6", Case.Sensitive, "the arithmetic that shows why — 1.03 s x 20 ticks");
    }

    /// <summary>The positive control: `17`'s two authored leads are both accepted.</summary>
    [Theory]
    [InlineData(1.2, "17 §2 — Thornmaw's Root")]
    [InlineData(1.5, "17 §9 — the Dicelord's All In")]
    public void The_leads_17_actually_authors_are_accepted(double lead, string source)
    {
        var encounter = Build(lead);

        encounter.LeadSecondsOfInstance[EffectInstanceId.Of(AllInInstance)].ShouldBe(lead, source);
    }

    // ════════════════════════════════════════════════════ 3 · T2 and T3

    /// <summary>
    /// 🔒 <b>T2</b> — the period must <b>exceed</b> the lead. Otherwise firing <c>k+1</c>'s wind-up
    /// would be emitted before firing <c>k</c> landed, and two wind-ups would be indistinguishable in
    /// a log that <em>is</em> the replay.
    /// </summary>
    [Fact]
    public void A_lead_at_least_as_long_as_the_mechanics_own_period_is_refused()
    {
        var oneSecondPeriod = BossTestBench.AllIn() with
        {
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0 },
        };

        var thrown = Should.Throw<EffectContextException>(() => Build(1.5, oneSecondPeriod));

        // 🔒 Steering S2 — WHICH rule, and over which mechanic. `ShouldContain("T2")` alone stood
        //    here and would have been satisfied by T1's message just as well, since every refusal in
        //    this builder is the same exception type and T1 fires on the same effect.
        thrown.Message.ShouldContain("T2", Case.Sensitive, "which rule fired");
        thrown.Message.ShouldContain("BOSS_DICELORD_P3_ALL_IN", Case.Sensitive, "which mechanic");
        thrown.Message.ShouldContain(BossTestBench.Dicelord, Case.Sensitive, "which boss");
        thrown.Message.ShouldNotContain(
            "T1",
            Case.Sensitive,
            "and NOT T1's — a 1.5 s lead is inside the band and a whole tick, so the only thing " +
            "wrong with it is that the mechanic's own 1 s period has nowhere to put it");
    }

    /// <summary>
    /// 🔒 <b>T3</b> — a damaging <c>PERIODIC</c> whose period exceeds the longest legal lead
    /// <b>must</b> carry one. `17` §1 is not advice: <em>"every damaging mechanic has a visible
    /// wind-up"</em>, and `17` §11 makes it a deliverable.
    /// </summary>
    [Fact]
    public void A_damaging_periodic_with_no_lead_is_refused()
    {
        var thrown = Should.Throw<EffectContextException>(() => Build(lead: null));

        thrown.Message.ShouldContain("T3", Case.Sensitive);
        thrown.Message.ShouldContain("BOSS_DICELORD_P3_ALL_IN", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <b>T3's two exemptions</b>, both structural rather than discretionary: an
    /// <c>ON_PHASE_ENTER</c> mechanic cannot be foreseen 1.2 s out because the entry is HP-driven,
    /// and a period at or below the longest lead has nowhere to put a wind-up (T2) — `17` §8's
    /// Sporequeen Rot drains once a second.
    /// </summary>
    [Theory]
    [InlineData(true, 1.0, "17 §8 — a 1 Hz aura is exempt by T2")]
    [InlineData(false, 20.0, "17 §9 — an ON_PHASE_ENTER burst is exempt: the entry is HP-driven")]
    public void A_damaging_mechanic_that_cannot_carry_a_wind_up_is_exempt(
        bool periodic, double interval, string why)
    {
        var mechanic = BossTestBench.AllIn() with
        {
            Trigger = periodic
                ? new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = interval }
                : new EffectTrigger { Kind = TriggerKind.ON_PHASE_ENTER, Phase = 3 },
        };

        var encounter = Build(lead: null, mechanic);

        // 🔒 The floor: the mechanic really was built onto the encounter. An exemption asserted over
        //    a boss that carries no mechanics at all is an exemption for nothing (steering S3).
        encounter.Plan.Effects.Select(h => h.Effect.Id).ShouldContain(mechanic.Id);

        encounter.LeadSecondsOfInstance.ShouldBeEmpty(why);
    }

    /// <summary>
    /// 🔒 The negative control for T3's op set: an <c>APPLY_STATUS</c> on the same cadence is not a
    /// damaging mechanic, so it needs no wind-up — a DoT's damage lands on `05` §3.1 slot 1's
    /// cadence rather than at the mechanic's own firing, and a lead would point at the wrong moment.
    /// </summary>
    [Fact]
    public void A_non_damaging_periodic_needs_no_wind_up()
    {
        var encounter = Build(lead: null, BossTestBench.Root());

        encounter.Plan.Effects.Select(h => h.Effect.Id).ShouldContain(
            "BOSS_THORNMAW_P2_ROOT", "the floor: the mechanic IS on the encounter");

        encounter.LeadSecondsOfInstance.ShouldBeEmpty();

        BossTelegraphs.DamagingOps.Count.ShouldBe(3, "the floor: DAMAGE, DAMAGE_TRUE, DAMAGE_MAXHP_PCT");
        BossTelegraphs.DamagingOps.ShouldNotContain(EffectOp.APPLY_STATUS);
    }

    // ════════════════════════════════════════════════════ 4 · the band's own numbers

    /// <summary>
    /// 🔒 The band is `17` §1's and is stated once: <see cref="BossTelegraphs"/> reads
    /// <c>CombatLog</c>'s constants rather than restating them, so the builder and
    /// <c>CombatLog.AppendTelegraph</c> cannot disagree about what is legal.
    /// </summary>
    [Fact]
    public void The_band_is_the_same_1_0_to_1_5_seconds_the_log_enforces()
    {
        BossTelegraphs.MinLeadSeconds.ShouldBe(1.0);
        BossTelegraphs.MaxLeadSeconds.ShouldBe(1.5);
        BossTelegraphs.MinLeadSeconds.ShouldBe(CombatLog.MinTelegraphSeconds);
        BossTelegraphs.MaxLeadSeconds.ShouldBe(CombatLog.MaxTelegraphSeconds);
    }

    /// <summary>The legal leads are exactly the whole ticks in the band — 20 to 30.</summary>
    [Theory]
    [InlineData(1.00, 20)]
    [InlineData(1.20, 24)]
    [InlineData(1.50, 30)]
    public void A_legal_lead_is_a_whole_number_of_ticks_between_20_and_30(double lead, int ticks) =>
        BossTelegraphs.LeadTicks(lead).ShouldBe(ticks);

    // ════════════════════════════════════════════════════ 5 · the new tick slot is inert without a boss

    /// <summary>
    /// 🔒 The slot M2-12 added to the tick loop changes <b>nothing</b> in a fight with no boss:
    /// <c>NoBossPhases.AdvanceTick</c> is a no-op, and the two logs hash identically.
    /// </summary>
    /// <remarks>
    /// 🔒 The <c>LogHash</c> comparison is the assertion that matters. `14` §8.2 runs the same hash
    /// on x64 and ARM64 as the determinism gate and `11` §6 compares it as an anti-tamper check —
    /// so <em>"a new slot perturbs no existing fight"</em> is exactly a statement about this number.
    /// </remarks>
    [Fact]
    public void A_fight_with_no_boss_is_untouched_by_the_new_tick_slot()
    {
        RecordingPhases? phases = null;

        var strict = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[] { BossTestBench.Hero(), BossTestBench.Minion(0) },
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, damage: 2000.0),
            },
            rules: BossTestBench.Rules(maxTicks: 40)));

        var observed = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[] { BossTestBench.Hero(), BossTestBench.Minion(0) },
            services =>
            {
                phases = new RecordingPhases(services);

                return BattleSeams.Strict with
                {
                    Attack = new RecordingAttackPipeline(services, damage: 2000.0),
                    Phases = phases,
                };
            },
            rules: BossTestBench.Rules(maxTicks: 40)));

        observed.LogHash.ShouldBe(strict.LogHash, "the slot emits nothing without a boss");
        strict.Log.ShouldNotBeEmpty("the floor: there IS a fight to compare");
        BossTestBench.Telegraphs(strict.Log).ShouldBeEmpty();

        phases.ShouldNotBeNull();
        phases.Ticks.ShouldBe(
            new[] { "2a:HERO@0", "2a:ENEMY_0@0" },
            Case.Sensitive,
            "once per actor per tick, in `05` §3.1 actor order — and this fight lasts one tick");
    }

    // ════════════════════════════════════════════════════ fixtures

    /// <summary>
    /// 🔒 The phase-1 <c>Ante</c> is here to move All In off effect index 0, which is the byte
    /// <c>CombatLog.NoDataId</c> also uses — see
    /// <see cref="All_Ins_position_in_the_battles_effect_table_is_the_one_the_wind_up_names"/>.
    /// </summary>
    private const string AnteEffect = "A_DICELORD_P1_ANTE";

    private static ActorPlan BossPlan() =>
        BossTestBench.Boss(
            BossTestBench.Dicelord,
            maxHp: 1000.0,
            BossTestBench.InPhase(
                BossTestBench.Dicelord, 1, BossTestBench.OnPhaseEnter(AnteEffect, 1)),
            BossTestBench.InPhase(BossTestBench.Dicelord, 3, BossTestBench.AllIn()),
            BossTestBench.BuiltIn(BossTestBench.Dicelord, BossBuiltIns.Enrage));

    private static BossEncounter Encounter()
    {
        var phaseOfInstance = new Dictionary<EffectInstanceId, int>
        {
            [BossBuiltIns.PhaseInstance(BossTestBench.Dicelord, 1, AnteEffect)] = 1,
            [EffectInstanceId.Of(AllInInstance)] = 3,
        };

        var leadSecondsOfInstance = new Dictionary<EffectInstanceId, double>
        {
            [EffectInstanceId.Of(AllInInstance)] = 1.5,
        };

        return new BossEncounter
        {
            BossId = BossTestBench.Dicelord,
            Plan = BossPlan(),
            FirstClear = false,
            Phase2HpFraction = 0.66,
            Phase3HpFraction = 0.33,
            PhaseOfInstance = phaseOfInstance,
            LeadSecondsOfInstance = leadSecondsOfInstance,

            // 🔒 Derived, never restated: a fixture that hand-wrote both maps could author a lead
            // the announce list did not carry, and the telegraph these cases exist to prove would
            // simply never be emitted — a suite that cannot fail. Same derivation as Build's.
            AnnouncingOfPhase = BossEncounterBuilder.AnnouncingByPhase(
                phaseOfInstance, leadSecondsOfInstance),
        };
    }

    private static BossRun Fight(params (int Tick, string ActorId, double Fraction)[] script) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), BossPlan() },
            new List<BossEncounter> { Encounter() },
            new List<EffectInstanceId> { EffectInstanceId.Of(AllInInstance) },
            script,
            maxTicks: 400);

    /// <summary>The Dicelord's phase-3 block, built through the real builder at a given lead.</summary>
    private static BossEncounter Build(double? lead, EffectDefinition? mechanic = null)
    {
        var effect = mechanic ?? BossTestBench.AllIn();

        var script = BossTestBench.Script(
            BossTestBench.Dicelord,
            BossTestBench.Block(1),
            BossTestBench.Block(2),
            BossTestBench.Block(3, new BossMechanic(effect.Id, lead)));

        return BossEncounterBuilder.Build(
            BossTestBench.Request(script, BossTestBench.Lookup(effect)));
    }
}
