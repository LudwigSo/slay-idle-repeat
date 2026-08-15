using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>Every damaging mechanic gets a visible wind-up, emitted once, ahead of the firing it announces.</summary>
/// <remarks>
/// The lead is boss-script data, not an engine constant — different mechanics author different
/// lead times (e.g. 1.2s vs 1.5s).
/// </remarks>
public sealed class BossTelegraphTests
{
    private const string AllInInstance = "BOSS_DICELORD#P3#BOSS_DICELORD_P3_ALL_IN";

    // ════════════════════════════════════════════════════ 1 · the emission

    /// <remarks>
    /// Phase 3 is entered at tick 100, so All In's 8s period first fires at tick 260; a 1.5s lead
    /// (30 ticks) puts the wind-up at tick 230.
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

        // Asserted as index 1 (not 0) deliberately: NoDataId is also 0, so an index-0 assertion
        // couldn't distinguish "All In's index" from "no content id was written at all".
        CombatLog.NoDataId.ShouldBe((ushort)0, "which is what makes an index of 0 undiscriminating");

        announced.DataId.ShouldBe(
            (ushort)1,
            "the battle-local effect index of BOSS_DICELORD_P3_ALL_IN: the plan's ids sort " +
            "A_DICELORD_P1_ANTE, BOSS_DICELORD_P3_ALL_IN, SYS_ENRAGE, so All In is the second");
    }

    /// <summary>
    /// The floor under the index above: the battle-local effect table is built from the opening
    /// roster in ascending effect-id order, and All In really is at position 1 in it.
    /// </summary>
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

    [Fact]
    public void A_mechanic_of_a_phase_the_boss_is_not_in_announces_nothing()
    {
        var run = Fight();

        // Floors under the emptiness claim: the fight ran long enough that the wind-up would have
        // fired had the phase been entered, and the mechanic really is on the plan (not absent) —
        // otherwise an empty log would prove nothing.
        run.Result.Log.Count.ShouldBeGreaterThan(0, "there IS a fight");
        run.Driver.At(0, AllInInstance).IsRegistered.ShouldBeTrue(
            "the phase-3 mechanic is on the OPENING roster — it is de-anchored, not missing");

        BossTestBench.Telegraphs(run.Result.Log).ShouldBeEmpty(
            "the boss never left phase 1, so its phase-3 All In never scheduled a firing");
    }

    // ════════════════════════════════════════════════════ 2 · T1 — the band

    /// <summary>T1: a lead outside the 1.0-1.5s band is refused at encounter-build time.</summary>
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
    /// T1 also refuses a lead inside the band that isn't a whole tick: the simulation is
    /// fixed-tick, so 1.03s (20.6 ticks) points between two ticks and therefore at neither.
    /// </summary>
    [Fact]
    public void A_lead_that_is_not_a_whole_tick_is_refused_even_though_it_is_inside_the_band()
    {
        var thrown = Should.Throw<EffectContextException>(() => Build(1.03));

        thrown.Message.ShouldContain("T1", Case.Sensitive);
        thrown.Message.ShouldContain(
            "20.6", Case.Sensitive, "the arithmetic that shows why — 1.03 s x 20 ticks");
    }

    /// <summary>Positive control: both authored leads are accepted.</summary>
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
    /// T2: the period must exceed the lead, otherwise firing k+1's wind-up would be emitted before
    /// firing k landed.
    /// </summary>
    [Fact]
    public void A_lead_at_least_as_long_as_the_mechanics_own_period_is_refused()
    {
        var oneSecondPeriod = BossTestBench.AllIn() with
        {
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0 },
        };

        var thrown = Should.Throw<EffectContextException>(() => Build(1.5, oneSecondPeriod));

        // ShouldContain("T2") alone would also be satisfied by T1's message, since every refusal
        // here throws the same exception type over the same effect — so check both rule and target.
        thrown.Message.ShouldContain("T2", Case.Sensitive, "which rule fired");
        thrown.Message.ShouldContain("BOSS_DICELORD_P3_ALL_IN", Case.Sensitive, "which mechanic");
        thrown.Message.ShouldContain(BossTestBench.Dicelord, Case.Sensitive, "which boss");
        thrown.Message.ShouldNotContain(
            "T1",
            Case.Sensitive,
            "and NOT T1's — a 1.5 s lead is inside the band and a whole tick, so the only thing " +
            "wrong with it is that the mechanic's own 1 s period has nowhere to put it");
    }

    /// <summary>T3: a damaging PERIODIC whose period exceeds the longest legal lead must carry one.</summary>
    [Fact]
    public void A_damaging_periodic_with_no_lead_is_refused()
    {
        var thrown = Should.Throw<EffectContextException>(() => Build(lead: null));

        thrown.Message.ShouldContain("T3", Case.Sensitive);
        thrown.Message.ShouldContain("BOSS_DICELORD_P3_ALL_IN", Case.Sensitive);
    }

    /// <summary>
    /// T3's two exemptions, both structural: an ON_PHASE_ENTER mechanic can't be foreseen because
    /// entry is HP-driven, and a period at or below the longest lead has nowhere to put a wind-up (T2).
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

        // Floor: the mechanic really was built onto the encounter, otherwise the exemption below
        // would be vacuous.
        encounter.Plan.Effects.Select(h => h.Effect.Id).ShouldContain(mechanic.Id);

        encounter.LeadSecondsOfInstance.ShouldBeEmpty(why);
    }

    /// <summary>
    /// Negative control: an APPLY_STATUS on the same cadence is not a damaging mechanic, so it
    /// needs no wind-up — a DoT's damage lands on its own tick cadence, not at the mechanic's firing.
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
    /// <see cref="BossTelegraphs"/> reads <c>CombatLog</c>'s band constants rather than restating
    /// them, so the builder and <c>CombatLog.AppendTelegraph</c> cannot disagree about what's legal.
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

    /// <summary>The boss phase tick slot changes nothing in a fight with no boss: it's a no-op.</summary>
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

    // Moves All In off effect index 0, which CombatLog.NoDataId also uses (see
    // All_Ins_position_in_the_battles_effect_table_is_the_one_the_wind_up_names).
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

            // Derived, not hand-authored: a hand-written map could omit a lead the announce list
            // needs, making the telegraph these cases test never fire — a suite that can't fail.
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
