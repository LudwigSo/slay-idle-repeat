using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Triggers;

/// <summary>The firing predicate of every one of the 23 trigger kinds, from a hand-cranked tick source.</summary>
/// <remarks>
/// Every assertion is on a <see cref="TriggerOutcome"/> rather than a boolean: eleven of the twelve
/// refusals mean "did not fire", and a <c>PK_FLURRY</c> held back by a spent <c>once</c> latch instead
/// of by its counter is a bug whose boolean test passes.
/// </remarks>
public sealed class TriggerFiringTests
{
    private const ulong BattleSeed = 0x5EED_1234_5678_9ABCUL;

    /// <summary>Every kind that takes no narrowing parameter fires on its own moment — the floor under every other test in this file.</summary>
    /// <remarks>
    /// <para>
    /// Derived from <c>TriggerCatalogue.All</c>, not hand-listed. A hand-written <c>InlineData</c>
    /// block tied to <c>TriggerKind</c> by nothing would lose a kind's firing coverage to a merge, or
    /// fail to gain it for a new kind, with every test in this file still green.
    /// </para>
    /// <para>
    /// The three exclusions are named once, in <see cref="KindsWithTheirOwnTest"/>, and each has a
    /// test of its own below or in <c>PeriodicAnchoringTests</c>.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnnarrowedKinds))]
    public void An_unnarrowed_trigger_fires_on_its_own_moment(TriggerKind kind)
    {
        var (registry, id) = Registered(new EffectTrigger { Kind = kind });

        registry.Evaluate(id, TriggerTestBattle.Moment(kind, tick: 4)).ShouldBe(TriggerOutcome.FIRES);
    }

    /// <summary>
    /// The three kinds that cannot be driven by a bare moment, each with its own test.
    /// </summary>
    /// <remarks>
    /// <c>PERIODIC</c> has no moment at all — it fires on a schedule, through
    /// <c>TriggerRegistry.PeriodicDue</c>, and <c>PeriodicAnchoringTests</c> owns it. The other two
    /// carry a constitutive parameter and are driven by
    /// <see cref="ON_PHASE_ENTER_fires_only_on_its_own_phase"/> and the four <c>ON_LOW_HP</c> tests.
    /// </remarks>
    internal static readonly TriggerKind[] KindsWithTheirOwnTest =
    {
        TriggerKind.PERIODIC,
        TriggerKind.ON_PHASE_ENTER,
        TriggerKind.ON_LOW_HP,
    };

    /// <summary>Every kind that a bare trigger can express, straight off the catalogue.</summary>
    public static TheoryData<TriggerKind> UnnarrowedKinds
    {
        get
        {
            var data = new TheoryData<TriggerKind>();

            foreach (var kind in TriggerCatalogue.All.Where(k => !KindsWithTheirOwnTest.Contains(k)))
            {
                data.Add(kind);
            }

            return data;
        }
    }

    /// <summary>The floor under the theory above: every one of the 23 kinds is either driven by it or named in <see cref="KindsWithTheirOwnTest"/>, and nothing else.</summary>
    /// <remarks>
    /// Without this the exclusion list is a place a kind can be parked to make a failure go away.
    /// Adding a name here is deliberate and has to come with the test that name promises.
    /// </remarks>
    [Fact]
    public void Every_one_of_the_23_kinds_is_driven_here_or_named_as_having_its_own_test()
    {
        UnnarrowedKinds.Count.ShouldBe(TriggerCatalogue.TriggerKindCount - KindsWithTheirOwnTest.Length);

        KindsWithTheirOwnTest.Length.ShouldBe(
            3,
            "PERIODIC has no moment; ON_PHASE_ENTER and ON_LOW_HP carry a constitutive parameter");

        UnnarrowedKinds.Cast<object[]>()
                       .Select(row => (TriggerKind)row[0])
                       .Concat(KindsWithTheirOwnTest)
                       .ShouldBe(TriggerCatalogue.All, ignoreOrder: true);
    }

    /// <summary>And on nobody else's — a moment of another kind is <c>WRONG_KIND</c>, not silence.</summary>
    [Fact]
    public void A_trigger_does_not_fire_on_another_kinds_moment()
    {
        var (registry, id) = Registered(new EffectTrigger { Kind = TriggerKind.ON_HIT });

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_CRIT, 4))
                .ShouldBe(TriggerOutcome.WRONG_KIND);
    }

    /// <summary>An instance whose <c>PHASE</c> scope ended does not fire.</summary>
    [Fact]
    public void A_deactivated_instance_does_not_fire()
    {
        var (registry, id) = Registered(new EffectTrigger { Kind = TriggerKind.ON_HIT });

        registry.Deactivate(id);

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_HIT, 4))
                .ShouldBe(TriggerOutcome.NOT_ACTIVE);
    }

    // ------------------------------------------------------------------ onlyIfWon

    /// <summary><c>onlyIfWon</c>, and its absence — <c>CP_BLOOD_PRICE</c>'s drawback writes none and has to land on a loss.</summary>
    /// <remarks>
    /// The expected outcome is spelled with <c>nameof</c> rather than passed as a
    /// <see cref="TriggerOutcome"/>: the enum is <c>internal</c> to <c>SlayIdleRepeat.Core</c> and
    /// reaches this assembly through an <c>InternalsVisibleTo</c> grant, which is not enough to put
    /// it in a <c>public</c> theory's signature. <c>nameof</c> keeps the row compiler-checked against
    /// the member, so a renamed outcome is a build failure here too.
    /// </remarks>
    [Theory]
    [InlineData(null, true, nameof(TriggerOutcome.FIRES))]
    [InlineData(null, false, nameof(TriggerOutcome.FIRES))]
    [InlineData(true, true, nameof(TriggerOutcome.FIRES))]
    [InlineData(true, false, nameof(TriggerOutcome.ONLY_IF_WON_AND_LOST))]
    [InlineData(false, false, nameof(TriggerOutcome.FIRES))]
    public void ON_BATTLE_END_reads_onlyIfWon(bool? onlyIfWon, bool heroWon, string expected)
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_BATTLE_END, OnlyIfWon = onlyIfWon });

        var moment = new TriggerOccurrence
        {
            Kind = TriggerKind.ON_BATTLE_END,
            Tick = 100,
            HeroWon = heroWon,
        };

        registry.Evaluate(id, moment).ToString().ShouldBe(expected);
    }

    // ------------------------------------------------------------------ ON_PHASE_ENTER

    /// <summary>Thornmaw's phase-3 summon does not fire on phase 2's entry.</summary>
    [Theory]
    [InlineData(1, nameof(TriggerOutcome.PHASE_MISMATCH))]
    [InlineData(2, nameof(TriggerOutcome.PHASE_MISMATCH))]
    [InlineData(3, nameof(TriggerOutcome.FIRES))]
    public void ON_PHASE_ENTER_fires_only_on_its_own_phase(int entered, string expected)
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_PHASE_ENTER, Phase = 3 });

        var moment = new TriggerOccurrence
        {
            Kind = TriggerKind.ON_PHASE_ENTER,
            Tick = 240,
            Phase = entered,
        };

        registry.Evaluate(id, moment).ToString().ShouldBe(expected);
    }

    // ------------------------------------------------------------------ ON_LOW_HP

    /// <summary>Self HP crosses a threshold downward. A reading that is already below the threshold is not a crossing; the transition is.</summary>
    [Fact]
    public void ON_LOW_HP_fires_on_the_downward_crossing_and_not_on_the_readings_around_it()
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_LOW_HP, Threshold = 0.30 },
            holderHpFraction: 1.0);

        HpChange(registry, id, 0.80).ShouldBe(TriggerOutcome.THRESHOLD_NOT_CROSSED, "still above");
        HpChange(registry, id, 0.31).ShouldBe(TriggerOutcome.THRESHOLD_NOT_CROSSED, "still above");
        HpChange(registry, id, 0.30).ShouldBe(TriggerOutcome.FIRES, "at the threshold is across it");
        HpChange(registry, id, 0.20).ShouldBe(TriggerOutcome.THRESHOLD_NOT_CROSSED, "already below");
    }

    /// <summary>The threshold re-arms when HP goes back above it — entailed by the kind having a <c>once</c> parameter at all.</summary>
    /// <remarks>
    /// If a crossing could only ever happen once, <c>once</c> would say nothing. So the kind must be
    /// able to fire again, firing again requires crossing downward again, and crossing downward again
    /// requires having gone back up. Not an invented default — a consequence of the parameter's
    /// existence.
    /// </remarks>
    [Fact]
    public void ON_LOW_HP_re_arms_when_HP_goes_back_above_the_threshold()
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_LOW_HP, Threshold = 0.30 },
            holderHpFraction: 1.0);

        HpChange(registry, id, 0.25).ShouldBe(TriggerOutcome.FIRES);
        HpChange(registry, id, 0.55).ShouldBe(TriggerOutcome.THRESHOLD_NOT_CROSSED, "the heal re-arms it");
        HpChange(registry, id, 0.10).ShouldBe(TriggerOutcome.FIRES, "and it crosses again");
    }

    /// <summary>Rise Again, worked: <c>once: true</c> is what stops the Ossuary King rising twice.</summary>
    [Fact]
    public void Rise_Again_fires_once_and_a_second_crossing_is_ONCE_SPENT()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("BOSS#0/BOSS_OSSUARY_KING_P3_RISE_AGAIN");
        registry.Register(id, TriggerTestBattle.RiseAgain(), activationTick: 0, holderHpFraction: 0.33);

        HpChange(registry, id, 0.005).ShouldBe(TriggerOutcome.FIRES);

        // The revive refills the boss to 25%, and the fight brings it back down.
        HpChange(registry, id, 0.25).ShouldBe(TriggerOutcome.ONCE_SPENT);
        HpChange(registry, id, 0.004).ShouldBe(TriggerOutcome.ONCE_SPENT);
    }

    /// <summary>An <c>ON_LOW_HP</c> registered without the holder's HP reading is refused — a crossing needs the reading before the change as well as the one after.</summary>
    [Fact]
    public void An_ON_LOW_HP_without_a_starting_HP_reading_is_refused()
    {
        var registry = TriggerTestBattle.Registry();

        var failure = Should.Throw<EffectContextException>(() => registry.Register(
            TriggerTestBattle.Instance("HERO#0/PK_LAST_BREATH"),
            TriggerTestBattle.Effect(
                "PK_LAST_BREATH",
                new EffectTrigger { Kind = TriggerKind.ON_LOW_HP, Threshold = 0.3 }),
            activationTick: 0));

        failure.Token.ShouldBe(nameof(TriggerKind.ON_LOW_HP));
        failure.Message.ShouldContain("HP fraction", Case.Sensitive);
    }

    /// <summary>
    /// The armed flag is read from the registration reading, not assumed: an effect granted while the
    /// holder is already under its threshold does not fire on the next scratch.
    /// </summary>
    [Fact]
    public void An_ON_LOW_HP_granted_below_its_threshold_starts_disarmed()
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_LOW_HP, Threshold = 0.30 },
            holderHpFraction: 0.20);

        HpChange(registry, id, 0.19).ShouldBe(TriggerOutcome.THRESHOLD_NOT_CROSSED);
        HpChange(registry, id, 0.60).ShouldBe(TriggerOutcome.THRESHOLD_NOT_CROSSED, "now armed");
        HpChange(registry, id, 0.05).ShouldBe(TriggerOutcome.FIRES);
    }

    /// <summary>
    /// A re-granted <c>ON_LOW_HP</c> is re-armed from the holder's HP now, not left holding
    /// the flag its last grant ended on.
    /// </summary>
    /// <remarks>
    /// The armed flag is stateful and <see cref="TriggerRegistry.Evaluate"/> refuses an inactive
    /// instance before the crossing check ever runs — so an instance deactivated while armed, whose
    /// holder then fell below the threshold, would fire on the next scratch: a crossing that never
    /// happened. Exactly the case the constructor refuses to guess, arriving through the back door.
    /// </remarks>
    [Fact]
    public void A_re_granted_ON_LOW_HP_is_re_armed_from_the_HP_it_is_granted_at()
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_LOW_HP, Threshold = 0.30 },
            holderHpFraction: 0.66);

        // The phase ends while the instance is still armed; the boss then falls below the threshold
        // with nothing watching, and the effect is granted again.
        registry.Deactivate(id);
        registry.Activate(id, tick: 200, holderHpFraction: 0.20);

        HpChange(registry, id, 0.19).ShouldBe(
            TriggerOutcome.THRESHOLD_NOT_CROSSED,
            "the re-grant found the holder already below the threshold, so nothing was crossed");

        HpChange(registry, id, 0.55).ShouldBe(TriggerOutcome.THRESHOLD_NOT_CROSSED, "re-armed by the heal");
        HpChange(registry, id, 0.05).ShouldBe(TriggerOutcome.FIRES);
    }

    /// <summary>A re-grant of an <c>ON_LOW_HP</c> without an HP reading is refused, as registration is.</summary>
    [Fact]
    public void A_re_granted_ON_LOW_HP_without_an_HP_reading_is_refused()
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_LOW_HP, Threshold = 0.30 },
            holderHpFraction: 1.0);

        registry.Deactivate(id);

        Should.Throw<EffectContextException>(() => registry.Activate(id, tick: 200))
              .Message.ShouldContain("HP fraction", Case.Sensitive);
    }

    /// <summary>An HP fraction outside <c>0..1</c> — or a NaN — is refused rather than silently disarming.</summary>
    /// <remarks>
    /// A NaN is the dangerous one: every comparison against it is false, so
    /// <c>Round(fraction) &gt; Threshold</c> answers <c>false</c> and the instance starts quietly
    /// disarmed. <c>PK_UNBREAKABLE</c> would then never fire, in a fight nobody replays.
    /// </remarks>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void An_ON_LOW_HP_registered_at_an_impossible_HP_fraction_is_refused(double fraction)
    {
        var registry = TriggerTestBattle.Registry();

        Should.Throw<EffectContextException>(() => registry.Register(
                  TriggerTestBattle.Instance("HERO#0/PK_LAST_BREATH"),
                  TriggerTestBattle.Effect(
                      "PK_LAST_BREATH",
                      new EffectTrigger { Kind = TriggerKind.ON_LOW_HP, Threshold = 0.3 }),
                  activationTick: 0,
                  holderHpFraction: fraction))
              .Message.ShouldContain("HP fraction", Case.Sensitive);
    }

    // ------------------------------------------------------------------ once

    /// <summary><c>PK_UNBREAKABLE</c>'s <c>ON_LETHAL {once: true}</c>.</summary>
    [Fact]
    public void ON_LETHAL_with_once_fires_once_per_battle()
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_LETHAL, Once = true });

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_LETHAL, 10))
                .ShouldBe(TriggerOutcome.FIRES);
        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_LETHAL, 40))
                .ShouldBe(TriggerOutcome.ONCE_SPENT);

        registry[id].FireCount.ShouldBe(
            1,
            "05 §3.1's anti-loop bound on SURVIVE_LETHAL and REVIVE is an OP rule, and this is the " +
            "count M2-08 reads to apply it");
    }

    /// <summary>Without <c>once</c> it fires every time — the parameter is what bounds it.</summary>
    [Fact]
    public void ON_LETHAL_without_once_is_unbounded_by_18_3()
    {
        var (registry, id) = Registered(new EffectTrigger { Kind = TriggerKind.ON_LETHAL });

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_LETHAL, 10))
                .ShouldBe(TriggerOutcome.FIRES);
        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_LETHAL, 40))
                .ShouldBe(TriggerOutcome.FIRES);

        registry[id].FireCount.ShouldBe(2);
    }

    // ------------------------------------------------------------------ cooldown

    /// <summary>Gulgrot Spit's <c>ON_HIT_TAKEN (cd 6s)</c>: the cooldown is counted in ticks from the firing.</summary>
    [Fact]
    public void An_internal_cooldown_holds_the_trigger_for_its_span()
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_HIT_TAKEN, Cooldown = 6.0 });

        var fired = TriggerTestBattle.At(10.0);

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_HIT_TAKEN, fired))
                .ShouldBe(TriggerOutcome.FIRES);

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_HIT_TAKEN, TriggerTestBattle.At(15.95)))
                .ShouldBe(TriggerOutcome.COOLDOWN_ACTIVE);

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_HIT_TAKEN, TriggerTestBattle.At(16.0)))
                .ShouldBe(TriggerOutcome.FIRES, "6 s later, to the tick");
    }

    // ------------------------------------------------------------------ chance

    /// <summary><c>chance</c> is drawn from the battle's combat stream, never from an ambient source.</summary>
    [Fact]
    public void A_chance_of_one_always_fires_and_a_chance_of_zero_never_does()
    {
        var rng = EffectTestBattle.CombatRng(BattleSeed);

        var (always, alwaysId) = Registered(new EffectTrigger { Kind = TriggerKind.ON_HIT, Chance = 1.0 });
        var (never, neverId) = Registered(new EffectTrigger { Kind = TriggerKind.ON_HIT, Chance = 0.0 });

        always.Evaluate(alwaysId, TriggerTestBattle.Moment(TriggerKind.ON_HIT, 1), rng)
              .ShouldBe(TriggerOutcome.FIRES);
        never.Evaluate(neverId, TriggerTestBattle.Moment(TriggerKind.ON_HIT, 1), rng)
             .ShouldBe(TriggerOutcome.CHANCE_MISSED);
    }

    /// <summary>A trigger that carries a chance and is handed no stream fails loudly.</summary>
    [Fact]
    public void A_chance_without_a_draw_stream_is_refused()
    {
        var (registry, id) = Registered(new EffectTrigger { Kind = TriggerKind.ON_HIT, Chance = 0.5 });

        Should.Throw<EffectContextException>(
                  () => registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_HIT, 1)))
              .Message.ShouldContain("no draw stream", Case.Sensitive);
    }

    /// <summary>
    /// The draw is last. A trigger refused by a cheaper gate does not consume the battle's
    /// stream — otherwise a perk that never fires would still shift every later dodge, crit and
    /// <c>RANDOM_ENEMY</c> in the fight.
    /// </summary>
    [Fact]
    public void A_trigger_refused_by_a_cheaper_gate_does_not_consume_a_draw()
    {
        var rng = EffectTestBattle.CombatRng(BattleSeed);
        var start = rng.Position;

        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_ATTACK, EveryNth = 5, Chance = 0.5 });

        // Four attacks that the everyNth counter refuses before the chance is ever reached.
        for (var attack = 1; attack <= 4; attack++)
        {
            registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, attack), rng)
                    .ShouldBe(TriggerOutcome.EVERY_NTH_PENDING);
        }

        rng.Position.ShouldBe(start, "no draw was needed, so the stream did not move");

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, 5), rng);

        rng.Position.ShouldBe(start + 1, "the 5th attack reached the chance and drew exactly once");
    }

    // ------------------------------------------------------------------ run-layer filters

    /// <summary>The six run-layer kinds' filters — declared and unit-tested here, fired by the run controller.</summary>
    [Theory]
    [InlineData(TriggerKind.ON_TILE_RESOLVED, "TILE_DICE_FORGE", "TILE_DICE_FORGE", nameof(TriggerOutcome.FIRES))]
    [InlineData(TriggerKind.ON_TILE_RESOLVED, "TILE_DICE_FORGE", "TILE_SHRINE", nameof(TriggerOutcome.FILTER_MISMATCH))]
    [InlineData(TriggerKind.ON_TILE_RESOLVED, null, "TILE_SHRINE", nameof(TriggerOutcome.FIRES))]
    [InlineData(TriggerKind.ON_ROLL, "Star", "Star", nameof(TriggerOutcome.FIRES))]
    [InlineData(TriggerKind.ON_ROLL, "Star", "STAR", nameof(TriggerOutcome.FILTER_MISMATCH))]
    [InlineData(TriggerKind.ON_ROLL, "Star", "Pip", nameof(TriggerOutcome.FILTER_MISMATCH))]
    [InlineData(TriggerKind.ON_PERK_TAKEN, "OFFENSE", "OFFENSE", nameof(TriggerOutcome.FIRES))]
    [InlineData(TriggerKind.ON_PERK_TAKEN, "OFFENSE", "DEFENSE", nameof(TriggerOutcome.FILTER_MISMATCH))]
    public void A_run_layer_filter_is_matched_ordinally(
        TriggerKind kind, string? authored, string occurred, string expected)
    {
        var trigger = kind switch
        {
            TriggerKind.ON_TILE_RESOLVED => new EffectTrigger { Kind = kind, TileType = authored },
            TriggerKind.ON_ROLL => new EffectTrigger { Kind = kind, FaceKind = authored },
            _ => new EffectTrigger { Kind = kind, Category = authored },
        };

        var (registry, id) = Registered(trigger);

        var moment = new TriggerOccurrence
        {
            Kind = kind,
            Tick = 0,
            TileType = kind == TriggerKind.ON_TILE_RESOLVED ? occurred : null,
            FaceKind = kind == TriggerKind.ON_ROLL ? occurred : null,
            Category = kind == TriggerKind.ON_PERK_TAKEN ? occurred : null,
        };

        registry.Evaluate(id, moment).ToString().ShouldBe(expected);
    }

    /// <summary>
    /// A moment that carries no value where the trigger names one does <b>not</b> match: the run
    /// controller knows which tile resolved, and a moment that does not is a wiring gap rather than a
    /// wildcard.
    /// </summary>
    [Fact]
    public void A_named_filter_does_not_match_a_moment_that_names_nothing()
    {
        var (registry, id) = Registered(
            new EffectTrigger { Kind = TriggerKind.ON_TILE_RESOLVED, TileType = "TILE_DICE_FORGE" });

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_TILE_RESOLVED, 0))
                .ShouldBe(TriggerOutcome.FILTER_MISMATCH);
    }

    // ------------------------------------------------------------------ registry guards

    /// <summary>An unregistered id is refused rather than answered "does not fire".</summary>
    [Fact]
    public void An_unregistered_instance_is_refused()
    {
        var registry = TriggerTestBattle.Registry();

        Should.Throw<EffectContextException>(() => registry.Evaluate(
                  TriggerTestBattle.Instance("HERO#0/NOTHING"),
                  TriggerTestBattle.Moment(TriggerKind.ON_HIT, 0)))
              .Message.ShouldContain("no effect instance is registered", Case.Sensitive);
    }

    /// <summary>An effect with no trigger has nothing for this layer to fire.</summary>
    [Fact]
    public void An_effect_with_no_trigger_is_refused()
    {
        var registry = TriggerTestBattle.Registry();

        var glassHeart = new EffectDefinition
        {
            Id = "CP_GLASS_HEART_MULT",
            Op = EffectOp.STAT_MULT,
            Stat = StatSelector.AllCombat,
            Value = 2.0,
        };

        Should.Throw<EffectContextException>(
                  () => registry.Register(TriggerTestBattle.Instance("HERO#0/CP_GLASS_HEART"), glassHeart, 0))
              .Message.ShouldContain("carries no trigger", Case.Sensitive);
    }

    /// <summary>An instance id that is empty or blank is refused — it would share one counter.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_instance_id_is_refused(string value)
    {
        Should.Throw<ArgumentException>(() => EffectInstanceId.Of(value));
    }

    /// <summary>And the registry refuses one too — <c>EffectInstanceId.Of</c>'s guard is a convenience, not a guarantee.</summary>
    /// <remarks>
    /// A <c>record struct</c>'s positional constructor is public and <c>default</c> carries a null
    /// value, so both reach <c>Register</c> around <c>Of</c>. <c>default</c> is a perfectly good
    /// dictionary key, so an unnamed instance would register cleanly and share one counter with every
    /// other unnamed instance — the per-instance rule failing in the direction that looks like it
    /// works.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_instance_id_that_names_no_holding_is_refused_at_registration(string? value)
    {
        var registry = TriggerTestBattle.Registry();
        var id = value is null ? default : new EffectInstanceId(value);

        var failure = Should.Throw<EffectContextException>(() => registry.Register(
            id,
            TriggerTestBattle.Effect("PK_X", new EffectTrigger { Kind = TriggerKind.ON_HIT }),
            activationTick: 0));

        failure.Token.ShouldBe("PK_X");
        failure.Message.ShouldContain("names no holding", Case.Sensitive);
    }

    /// <summary>A <c>cooldown</c> that is not a whole number of ticks is refused at registration, not at the first firing.</summary>
    /// <remarks>
    /// The schema types <c>cooldown</c> as any non-negative number, so this is the only thing that
    /// catches it — an earlier draft converted it inside the firing path, so
    /// <c>{"kind":"ON_DODGE","cooldown":0.03}</c> passed the content build, passed registration, and
    /// threw on the first successful dodge of a live fight.
    /// </remarks>
    [Fact]
    public void A_fractional_cooldown_is_refused_when_the_effect_is_registered()
    {
        var registry = TriggerTestBattle.Registry();

        var failure = Should.Throw<EffectContextException>(() => registry.Register(
            TriggerTestBattle.Instance("BOSS#0/BOSS_RIMEHOLD_P2_SHATTERBACK"),
            TriggerTestBattle.Effect(
                "BOSS_RIMEHOLD_P2_SHATTERBACK",
                new EffectTrigger { Kind = TriggerKind.ON_HIT_TAKEN, Cooldown = 0.03 }),
            activationTick: 0));

        failure.Message.ShouldContain("fixed-tick", Case.Sensitive);
        failure.Message.ShouldContain("cooldown", Case.Sensitive);
    }

    /// <summary>An <c>ON_LOW_HP</c> moment. The tick advances, as the real loop does.</summary>
    private static TriggerOutcome HpChange(TriggerRegistry registry, EffectInstanceId id, double fraction)
    {
        var moment = new TriggerOccurrence
        {
            Kind = TriggerKind.ON_LOW_HP,
            Tick = registry[id].FireCount + 1,
            HpFraction = fraction,
        };

        return registry.Evaluate(id, moment);
    }

    /// <summary>A registry holding one instance of a trivial effect with the given trigger.</summary>
    private static (TriggerRegistry Registry, EffectInstanceId Id) Registered(
        EffectTrigger trigger,
        double? holderHpFraction = null)
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance($"HERO#0/TEST_{trigger.Kind}");

        registry.Register(
            id,
            TriggerTestBattle.Effect($"TEST_{trigger.Kind}", trigger),
            activationTick: 0,
            holderHpFraction);

        return (registry, id);
    }
}
