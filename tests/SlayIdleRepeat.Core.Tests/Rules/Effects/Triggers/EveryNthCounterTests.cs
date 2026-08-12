using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Triggers;

/// <summary>
/// 🔒 `18` §3's <c>everyNth</c> sentence, which carries three separate facts:
/// <em>"counters live on the <b>effect instance</b>: <c>ON_ATTACK</c> counters reset at battle
/// start; <c>ON_KILL</c> counters <b>persist across battles for the run</b> (<c>PK_MIDAS</c>'s 'every
/// 6th enemy killed'). Never fires in PvP (`05` §3.3)."</em>
/// </summary>
/// <remarks>
/// One test per fact, plus the two that are easy to get subtly wrong: the counter counts
/// <em>occurrences</em> rather than firings, and a duel kill does not count at all.
/// </remarks>
public sealed class EveryNthCounterTests
{
    /// <summary>`18` §7.3 — <c>PK_FLURRY</c> fires on the 5th, 10th, 15th attack and no other.</summary>
    [Fact]
    public void An_everyNth_counter_fires_on_every_Nth_occurrence()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("HERO#0/perk-slot-1/PK_FLURRY_T1");
        registry.Register(id, TriggerTestBattle.Flurry(), activationTick: 0);

        var fired = new List<int>();

        for (var attack = 1; attack <= 16; attack++)
        {
            if (registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, attack)) ==
                TriggerOutcome.FIRES)
            {
                fired.Add(attack);
            }
        }

        fired.ShouldBe(new[] { 5, 10, 15 });
        registry[id].OccasionCount.ShouldBe(16);
    }

    /// <summary>
    /// 🔒 <b>Fact one — per instance, not per actor and not global.</b> Two copies of one effect on
    /// one actor count separately.
    /// </summary>
    /// <remarks>
    /// Driven so that the two copies are out of step: the first copy sees ten attacks and the second
    /// sees five. A per-actor counter would have both at fifteen and fire both; a global one would be
    /// worse still.
    /// </remarks>
    [Fact]
    public void Two_copies_of_one_effect_count_separately()
    {
        var registry = TriggerTestBattle.Registry();

        var first = TriggerTestBattle.Instance("HERO#0/perk-slot-1/PK_FLURRY_T1");
        var second = TriggerTestBattle.Instance("HERO#0/gear-affix-3/PK_FLURRY_T1");

        registry.Register(first, TriggerTestBattle.Flurry(), 0);
        registry.Register(second, TriggerTestBattle.Flurry(), 0);

        for (var attack = 1; attack <= 5; attack++)
        {
            registry.Evaluate(first, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, attack));
        }

        // The first copy has reached 5 and fired; the second has seen nothing at all.
        registry[first].OccasionCount.ShouldBe(5);
        registry[second].OccasionCount.ShouldBe(0);
        registry[first].FireCount.ShouldBe(1);
        registry[second].FireCount.ShouldBe(0);

        for (var attack = 6; attack <= 10; attack++)
        {
            registry.Evaluate(first, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, attack));
            registry.Evaluate(second, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, attack));
        }

        registry[first].OccasionCount.ShouldBe(10);
        registry[second].OccasionCount.ShouldBe(5);
        registry[first].FireCount.ShouldBe(2);
        registry[second].FireCount.ShouldBe(1, "the second copy reached its own 5th attack, not the 10th");
    }

    /// <summary>
    /// 🔒 The per-instance rule has teeth: registering two copies under one id is refused, so a
    /// caller cannot merge their counters by accident.
    /// </summary>
    [Fact]
    public void Registering_two_copies_under_one_id_is_refused()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("HERO#0/PK_FLURRY_T1");

        registry.Register(id, TriggerTestBattle.Flurry(), 0);

        var failure = Should.Throw<EffectContextException>(
            () => registry.Register(id, TriggerTestBattle.Flurry(), 0));

        failure.Message.ShouldContain("already registered", Case.Sensitive);
        failure.Message.ShouldContain("count separately", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <b>Fact two, first half — <c>ON_ATTACK</c> counters reset at battle start.</b> A new battle
    /// is a new registry, so the reset is structural.
    /// </summary>
    [Fact]
    public void An_ON_ATTACK_counter_resets_at_battle_start()
    {
        var run = new RunTriggerCounters();
        var id = TriggerTestBattle.Instance("HERO#0/perk-slot-1/PK_FLURRY_T1");

        var battleOne = new TriggerRegistry(run);
        battleOne.Register(id, TriggerTestBattle.Flurry(), 0);

        for (var attack = 1; attack <= 4; attack++)
        {
            battleOne.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, attack));
        }

        battleOne[id].OccasionCount.ShouldBe(4);

        // The next fight of the same run: the same holding, the same run counters, a new registry.
        var battleTwo = new TriggerRegistry(run);
        battleTwo.Register(id, TriggerTestBattle.Flurry(), 0);

        battleTwo[id].OccasionCount.ShouldBe(0, "18 §3: ON_ATTACK counters reset at battle start");

        battleTwo.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, 1))
                 .ShouldBe(TriggerOutcome.EVERY_NTH_PENDING, "the 5th attack of battle one is not the 5th of battle two");
    }

    /// <summary>
    /// 🔒 <b>Fact two, second half — <c>ON_KILL</c> counters persist across battles for the run.</b>
    /// <c>PK_MIDAS</c>'s <em>"every 6th enemy killed"</em> spans fights.
    /// </summary>
    /// <remarks>
    /// The seam is the whole mechanism: the run holds the <see cref="RunTriggerCounters"/> and hands
    /// the same one to every battle. Persisting it is M3's and M1-05's — see
    /// <see cref="IRunTriggerCounters"/> — and there is deliberately no placeholder run controller
    /// here.
    /// </remarks>
    [Fact]
    public void An_ON_KILL_counter_persists_across_battles_for_the_run()
    {
        var run = new RunTriggerCounters();
        var id = TriggerTestBattle.Instance("HERO#0/perk-slot-2/PK_MIDAS_T1");

        var battleOne = new TriggerRegistry(run);
        battleOne.Register(id, TriggerTestBattle.Midas(), 0);

        for (var kill = 1; kill <= 4; kill++)
        {
            battleOne.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_KILL, kill))
                     .ShouldBe(TriggerOutcome.EVERY_NTH_PENDING);
        }

        run.Read(id).ShouldBe(4, "the count went through the seam, not into the battle");

        var battleTwo = new TriggerRegistry(run);
        battleTwo.Register(id, TriggerTestBattle.Midas(), 0);

        battleTwo[id].OccasionCount.ShouldBe(4, "18 §3: ON_KILL counters persist across battles");

        battleTwo.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_KILL, 1))
                 .ShouldBe(TriggerOutcome.EVERY_NTH_PENDING, "the 5th kill of the run");
        battleTwo.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_KILL, 2))
                 .ShouldBe(TriggerOutcome.FIRES, "the 6th kill of the run, in the second fight");

        run.Read(id).ShouldBe(6);
    }

    /// <summary>
    /// 🔒 <b>Fact three — <c>ON_KILL</c> never fires in a duel</b> (`05` §3.3: <em>"The only death in
    /// a duel ends the fight"</em>), <b>and a duel kill does not advance the run counter either</b>.
    /// </summary>
    /// <remarks>
    /// The second half is the one a "return false" implementation gets wrong. A duel is not part of a
    /// run, so a player who could advance <c>PK_MIDAS</c> in the arena would walk into their next run
    /// with the perk half-charged. M2-14 builds the duel; this is the predicate answering correctly
    /// when told it is one.
    /// </remarks>
    [Fact]
    public void An_ON_KILL_never_fires_in_a_duel_and_does_not_count_there_either()
    {
        var run = new RunTriggerCounters();
        var registry = new TriggerRegistry(run);
        var id = TriggerTestBattle.Instance("HERO#0/perk-slot-2/PK_MIDAS_T1");
        registry.Register(id, TriggerTestBattle.Midas(), 0);

        for (var kill = 1; kill <= 12; kill++)
        {
            var duelKill = new TriggerOccurrence
            {
                Kind = TriggerKind.ON_KILL,
                Tick = kill,
                IsDuel = true,
            };

            registry.Evaluate(id, duelKill).ShouldBe(TriggerOutcome.NEVER_FIRES_IN_A_DUEL);
        }

        run.Read(id).ShouldBe(0, "a duel is not part of a run, so a duel kill advances nothing");
        registry[id].FireCount.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 The counter counts <b>occurrences</b>, not firings: <c>PK_FLURRY</c> is "every 5th attack",
    /// not "every 5th attack that also passed a roll".
    /// </summary>
    /// <remarks>
    /// With a <c>chance</c> of 0 the trigger never fires, and the counter still reaches 15 — so the
    /// next attack after the chance improves is the 16th of the fight and not the 1st.
    /// </remarks>
    [Fact]
    public void The_counter_advances_on_every_occurrence_even_when_the_chance_misses()
    {
        var rng = EffectTestBattle.CombatRng(0x1234UL);
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("HERO#0/PK_WILD_SWING");

        registry.Register(
            id,
            TriggerTestBattle.Effect(
                "PK_WILD_SWING",
                new EffectTrigger { Kind = TriggerKind.ON_ATTACK, EveryNth = 5, Chance = 0.0 }),
            activationTick: 0);

        for (var attack = 1; attack <= 15; attack++)
        {
            registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, attack), rng);
        }

        registry[id].OccasionCount.ShouldBe(15);
        registry[id].FireCount.ShouldBe(0, "the chance missed all three times the counter came due");
        rng.Position.ShouldBe(3UL, "three draws — one per Nth attack, none for the twelve between");
    }

    /// <summary>
    /// An instance that is not active does not count either: a `18` §6 <c>PHASE</c>-scoped effect
    /// that ended is not watching the fight.
    /// </summary>
    [Fact]
    public void A_deactivated_instance_does_not_advance_its_counter()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("HERO#0/PK_FLURRY_T1");
        registry.Register(id, TriggerTestBattle.Flurry(), 0);

        registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, 1));
        registry.Deactivate(id);

        for (var attack = 2; attack <= 10; attack++)
        {
            registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.ON_ATTACK, attack))
                    .ShouldBe(TriggerOutcome.NOT_ACTIVE);
        }

        registry[id].OccasionCount.ShouldBe(1);
    }
}
