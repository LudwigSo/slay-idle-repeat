using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Triggers;

/// <summary>
/// 🔒 <b>R8</b> — a <c>PERIODIC</c>'s clock starts when its owning effect becomes active, and
/// <c>startDelay</c> is measured from that anchor.
/// </summary>
/// <remarks>
/// <para>
/// `17` §1.1 says <em>"fires every N seconds <b>from phase entry</b>"</em>; `18` §3 says every N
/// seconds of battle time with an <c>interval</c> and a <c>startDelay</c> and no phase anchor. Eight
/// boss fights depend on the difference. Every test below is one of the two documents' own worked
/// mechanics, so a failure names the fight it broke.
/// </para>
/// <para>
/// The driver is <see cref="TriggerTestBattle"/> — a hand-cranked tick source. M2-08's loop is not
/// needed to prove any of this, which is the property that makes the predicates pluggable into a
/// loop this task did not write.
/// </para>
/// </remarks>
public sealed class PeriodicAnchoringTests
{
    /// <summary>
    /// 🔒 A boss phase block anchors at <b>phase entry</b> — `17` §1.1 exactly. Thornmaw's phase 2
    /// Root (<c>PERIODIC 8s</c>) entered at 30 s fires at 38 s, 46 s, 54 s — never at 8 s.
    /// </summary>
    [Fact]
    public void A_phase_scoped_periodic_anchors_at_phase_entry()
    {
        var registry = TriggerTestBattle.Registry();
        var root = TriggerTestBattle.Instance("BOSS#0/BOSS_THORNMAW_P2_ROOT");
        var phaseEntry = TriggerTestBattle.At(30.0);

        var instance = registry.Register(root, TriggerTestBattle.ThornmawRoot(), phaseEntry);

        instance.AnchorTick.ShouldBe(phaseEntry);
        instance.NextFiringTick.ShouldBe(TriggerTestBattle.At(38.0));

        FiringTicks(registry, root, upTo: TriggerTestBattle.At(60.0))
            .ShouldBe(new[]
            {
                TriggerTestBattle.At(38.0),
                TriggerTestBattle.At(46.0),
                TriggerTestBattle.At(54.0),
            });
    }

    /// <summary>
    /// 🔒 A perk periodic becomes active at battle start and anchors <b>there</b> — `18` §3 exactly.
    /// The same effect, the same interval, a different anchor and therefore a different schedule.
    /// </summary>
    /// <remarks>
    /// Stated as a pair with the test above rather than on its own: the whole content of R8 is that
    /// one rule produces both readings, so a version that hard-coded "always battle start" or
    /// "always phase entry" would pass one of these and fail the other.
    /// </remarks>
    [Fact]
    public void A_perk_periodic_anchors_at_battle_start()
    {
        var registry = TriggerTestBattle.Registry();
        var aegis = TriggerTestBattle.Instance("HERO#0/PK_AEGIS_T1");

        var instance = registry.Register(
            aegis,
            TriggerTestBattle.Effect(
                "PK_AEGIS_T1",
                new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 8.0 },
                EffectOp.SHIELD),
            activationTick: 0);

        instance.AnchorTick.ShouldBe(0);

        FiringTicks(registry, aegis, upTo: TriggerTestBattle.At(30.0))
            .ShouldBe(new[]
            {
                TriggerTestBattle.At(8.0),
                TriggerTestBattle.At(16.0),
                TriggerTestBattle.At(24.0),
            });
    }

    /// <summary>
    /// 🔒 <c>SYS_ENRAGE</c> is a <c>BATTLE</c>-scope built-in, not phase-scoped, so its
    /// <c>startDelay: 70.0</c> is measured from <b>battle start</b> — which is what `17` §1's
    /// <em>"a hard enrage at 70 s"</em> means.
    /// </summary>
    /// <remarks>
    /// 🔒 The three stacks are the R1 reading: <c>STAT_MULT</c>'s value <b>is</b> the multiplier, so
    /// three seconds of <c>×1.08</c> is <c>1.08³ = 1.259712</c> — <b>125.9712</b> on a base of 100.
    /// The stacking itself is M2-06's and M2-07's; what is proved here is that exactly three firings
    /// have landed by 72 s, because a schedule that fired at 71 s first would show two.
    /// <para>
    /// ⚠️ The multiplier is <b>read from the authored effect</b> rather than written as a literal.
    /// An earlier draft wrote <c>Math.Pow(1.08, 3)</c> with both the base and the exponent supplied
    /// by the test, which asserted only that the test could multiply — steering S1. As written, the
    /// line fails if <c>SYS_ENRAGE</c>'s authored value drifts from `05` §3.1's <c>×1.08</c> or if
    /// the schedule lands a different number of firings by 72 s.
    /// </para>
    /// </remarks>
    [Fact]
    public void SYS_ENRAGE_anchors_at_battle_start_and_fires_once_a_second_from_70_s()
    {
        var registry = TriggerTestBattle.Registry();
        var enrage = TriggerTestBattle.Instance("BOSS#0/SYS_ENRAGE");

        var instance = registry.Register(enrage, TriggerTestBattle.SysEnrage(), activationTick: 0);

        instance.AnchorTick.ShouldBe(0);
        instance.NextFiringTick.ShouldBe(TriggerTestBattle.At(70.0));

        var firings = FiringTicks(registry, enrage, upTo: TriggerTestBattle.At(72.0));

        firings.ShouldBe(new[]
        {
            TriggerTestBattle.At(70.0),
            TriggerTestBattle.At(71.0),
            TriggerTestBattle.At(72.0),
        });

        var multiplier = TriggerTestBattle.SysEnrage().Value!.Value;

        Math.Round(100.0 * Math.Pow(multiplier, firings.Count), 4).ShouldBe(
            125.9712,
            "R1: STAT_MULT's value IS the multiplier, so three seconds of the authored x1.08 is 1.08^3");
    }

    /// <summary>
    /// 🔒 <b>The double-anchor negative.</b> `05` §3.1's phase check can enter two phases inside one
    /// tick — <em>"a burst from 70% to 20% therefore fires phase 2's entry, then phase 3's"</em> —
    /// and a live instance must not re-anchor on the second entry.
    /// </summary>
    /// <remarks>
    /// Without the guard, Ossify anchored at 20 s and re-anchored at 20.05 s would have its first
    /// ward at 34.05 s instead of 34 s — one tick of free survival per phase transition, on a boss
    /// whose whole phase-2 identity is that ward. The failure mode is worse in a fight that changes
    /// phase faster than the interval: an instance that re-anchored on every entry would never fire
    /// at all.
    /// </remarks>
    [Fact]
    public void A_live_periodic_never_re_anchors()
    {
        var registry = TriggerTestBattle.Registry();
        var ossify = TriggerTestBattle.Instance("BOSS#0/BOSS_OSSUARY_KING_OSSIFY_WARD");
        var entry = TriggerTestBattle.At(20.0);

        var instance = registry.Register(ossify, TriggerTestBattle.Ossify(), entry);

        instance.AnchorTick.ShouldBe(entry);
        instance.NextFiringTick.ShouldBe(TriggerTestBattle.At(34.0));

        // A second activation in the same tick, and again a tick later — the two shapes a phase
        // check can produce.
        instance.Activate(entry);
        instance.Activate(entry + 1);

        instance.AnchorTick.ShouldBe(entry, "R8 anchors once; phases never revert (05 §3.1)");
        instance.NextFiringTick.ShouldBe(TriggerTestBattle.At(34.0));
    }

    /// <summary>
    /// 🔒 A phase entered and exited inside one tick behaves: the exited phase's block is
    /// deactivated and never fires, and the phase entered after it anchors <b>once</b>, on that tick.
    /// </summary>
    /// <remarks>
    /// This is the burst `05` §3.1 describes, driven by hand: one HP decrease crosses 66% and 33%,
    /// so phase 2 is entered and left and phase 3 is entered, all at the same tick. M2-12 re-proves
    /// it against the real boss engine; what is proved here is that the trigger model underneath it
    /// cannot double-anchor or leak a dead phase's periodic.
    /// </remarks>
    [Fact]
    public void A_phase_entered_and_exited_in_one_tick_leaves_no_periodic_behind()
    {
        var registry = TriggerTestBattle.Registry();
        var burst = TriggerTestBattle.At(12.0);

        var phase2 = TriggerTestBattle.Instance("BOSS#0/BOSS_THORNMAW_P2_ROOT");
        var phase3 = TriggerTestBattle.Instance("BOSS#0/BOSS_THORNMAW_P3_SUMMON");

        // Phase 2 entry.
        var root = registry.Register(phase2, TriggerTestBattle.ThornmawRoot(), burst);
        root.AnchorTick.ShouldBe(burst);

        // Phase 3 entry, same tick: phase 2's PHASE-scoped effects end (18 §6), phase 3's begin.
        registry.Deactivate(phase2);
        var summon = registry.Register(phase3, TriggerTestBattle.ThornmawBloomSummon(), burst);

        root.IsActive.ShouldBeFalse();
        root.AnchorTick.ShouldBeNull("a deactivated instance has no clock left to fire on");
        summon.AnchorTick.ShouldBe(burst, "phase 3 anchors once, on the tick it was entered");

        // 🔒 ONE ascending sweep over BOTH instances — the shape `05` §3.1's loop actually has, and
        // the only shape the registry accepts now that a backwards tick is refused. Driven far
        // enough past both intervals that a leaked phase-2 Root would have fired twice.
        var fired = new List<(int Tick, string Instance)>();

        for (var tick = 0; tick <= TriggerTestBattle.At(40.0); tick++)
        {
            foreach (var instance in registry.PeriodicDue(new[] { phase2, phase3 }, tick))
            {
                fired.Add((tick, instance.Id.Value));
            }
        }

        fired.Where(f => f.Instance == phase2.Value)
             .ShouldBeEmpty("phase 2 was left; 18 §6's PHASE scope ends at the exit");

        fired.Where(f => f.Instance == phase3.Value).Select(f => f.Tick)
             .ShouldBe(new[] { TriggerTestBattle.At(24.0), TriggerTestBattle.At(36.0) });
    }

    /// <summary>
    /// 🔒 An effect that ends and is granted again <b>does</b> re-anchor — the case the double-anchor
    /// guard must not swallow.
    /// </summary>
    /// <remarks>
    /// Stated because the guard and this are one line apart in the implementation: a guard written as
    /// "never re-anchor" rather than "never re-anchor a <em>live</em> instance" would leave a
    /// re-granted effect firing on a schedule its first grant set, which for a 14 s Ossify re-granted
    /// at 50 s means a ward at 34 s that already happened.
    /// </remarks>
    [Fact]
    public void A_deactivated_periodic_re_anchors_when_it_is_activated_again()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("BOSS#0/BOSS_OSSUARY_KING_OSSIFY_WARD");

        var instance = registry.Register(id, TriggerTestBattle.Ossify(), TriggerTestBattle.At(20.0));
        registry.Deactivate(id);

        instance.Activate(TriggerTestBattle.At(50.0));

        instance.AnchorTick.ShouldBe(TriggerTestBattle.At(50.0));
        instance.NextFiringTick.ShouldBe(TriggerTestBattle.At(64.0));
    }

    /// <summary>
    /// 🔒 An absent <c>startDelay</c> is <b>one interval</b>, never zero — see
    /// <see cref="TriggerSchedule"/> for the two `17` clauses that rule it.
    /// </summary>
    [Fact]
    public void An_absent_startDelay_puts_the_first_firing_one_interval_after_the_anchor()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("BOSS#0/BOSS_OSSUARY_KING_OSSIFY_WARD");

        var instance = registry.Register(id, TriggerTestBattle.Ossify(), activationTick: 0);

        instance.NextFiringTick.ShouldBe(
            TriggerTestBattle.At(14.0),
            "17 §1.1's 'every N seconds FROM phase entry' first fires at entry + N, and 17 §1's " +
            "1.0-1.5 s telegraph has nowhere to go before a firing on the anchor tick itself");

        registry.PeriodicDue(new[] { id }, 0).ShouldBeEmpty("nothing fires on the anchor tick");
    }

    /// <summary>
    /// 🔒 `05` §3.1 slot 3 fires within one actor <b>in ascending effect-id order</b>, and the order
    /// is imposed here rather than taken from the caller's list.
    /// </summary>
    /// <remarks>
    /// The ids are handed in reversed. `18` §8 makes the order ordinal — <c>EffectOrder</c>'s
    /// comparer — because a bare <c>OrderBy(x =&gt; x.Id)</c> consults the ambient collation and puts
    /// a German phone and a Linux container in different orders.
    /// </remarks>
    [Fact]
    public void Due_periodics_come_back_in_ascending_effect_id_order()
    {
        var registry = TriggerTestBattle.Registry();

        var ids = new[] { "BOSS#0/A_LATE", "BOSS#0/A_EARLY", "BOSS#0/A_MIDDLE" }
            .Select(TriggerTestBattle.Instance)
            .ToArray();

        var effectIds = new[] { "BOSS_Z_LAST", "BOSS_A_FIRST", "BOSS_M_MIDDLE" };

        for (var i = 0; i < ids.Length; i++)
        {
            registry.Register(
                ids[i],
                TriggerTestBattle.Effect(
                    effectIds[i],
                    new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0 }),
                activationTick: 0);
        }

        var due = registry.PeriodicDue(ids.Reverse(), TriggerTestBattle.At(1.0));

        due.Select(instance => instance.Effect.Id)
           .ShouldBe(new[] { "BOSS_A_FIRST", "BOSS_M_MIDDLE", "BOSS_Z_LAST" }, Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <see cref="TriggerRegistry.PeriodicDue"/> is the single <c>PERIODIC</c> path: calling it
    /// twice on one tick fires once, and <see cref="TriggerRegistry.Evaluate"/> refuses the kind
    /// outright.
    /// </summary>
    [Fact]
    public void The_periodic_path_is_the_only_one_and_cannot_double_fire_a_tick()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("BOSS#0/BOSS_THORNMAW_P2_ROOT");
        registry.Register(id, TriggerTestBattle.ThornmawRoot(), activationTick: 0);

        var at8 = TriggerTestBattle.At(8.0);

        registry.PeriodicDue(new[] { id }, at8).Count.ShouldBe(1);
        registry.PeriodicDue(new[] { id }, at8).ShouldBeEmpty("the schedule already advanced past it");

        var failure = Should.Throw<EffectContextException>(
            () => registry.Evaluate(id, TriggerTestBattle.Moment(TriggerKind.PERIODIC, at8)));

        failure.Token.ShouldBe(nameof(TriggerKind.PERIODIC));
        failure.Message.ShouldContain("PeriodicDue", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 Two instances of <b>one</b> effect tie on the effect id, and the tie is broken by the
    /// instance id — never by the caller's list order.
    /// </summary>
    /// <remarks>
    /// <c>EffectInstanceId</c> exists precisely because one actor can hold two copies of one effect
    /// (`18` §3), so <c>OrderBy(effect id)</c> alone is a <em>stable</em> sort over a tie: which of
    /// two <c>PK_AEGIS</c> wards lands first would be decided by how the tick loop happened to build
    /// its list. The candidates are handed in reversed, so a version without the tie-break returns
    /// them reversed.
    /// </remarks>
    [Fact]
    public void Two_instances_of_one_effect_are_tie_broken_by_the_instance_id()
    {
        var registry = TriggerTestBattle.Registry();

        var first = TriggerTestBattle.Instance("HERO#0/perk-slot-1/PK_AEGIS_T1");
        var second = TriggerTestBattle.Instance("HERO#0/perk-slot-2/PK_AEGIS_T1");

        foreach (var id in new[] { first, second })
        {
            registry.Register(
                id,
                TriggerTestBattle.Effect(
                    "PK_AEGIS_T1",
                    new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0 },
                    EffectOp.SHIELD),
                activationTick: 0);
        }

        var due = registry.PeriodicDue(new[] { second, first }, TriggerTestBattle.At(1.0));

        due.Select(instance => instance.Id.Value).ShouldBe(
            new[] { first.Value, second.Value },
            Case.Sensitive,
            "18 §8's order is total: the effect id first, then the instance id");
    }

    /// <summary>
    /// 🔒 A tick that goes backwards is refused rather than silently answering "nothing is due".
    /// </summary>
    /// <remarks>
    /// `05` §3.1's loop runs ticks in ascending order, and a caller that walked it wrongly would lose
    /// every firing in between with nothing going red — the shape of failure every other guard in
    /// this class throws on. <c>CombatLog.Append</c> refuses a backwards tick for the neighbouring
    /// reason.
    /// </remarks>
    [Fact]
    public void A_tick_that_goes_backwards_is_refused()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("BOSS#0/BOSS_THORNMAW_P2_ROOT");
        registry.Register(id, TriggerTestBattle.ThornmawRoot(), activationTick: 0);

        registry.PeriodicDue(new[] { id }, TriggerTestBattle.At(8.0)).Count.ShouldBe(1);

        var failure = Should.Throw<EffectContextException>(
            () => registry.PeriodicDue(new[] { id }, TriggerTestBattle.At(7.0)));

        failure.Message.ShouldContain("goes backwards", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 One instance decides once per moment: the same id twice in one call is refused.
    /// </summary>
    /// <remarks>
    /// Normally harmless, and that is the trap. A schedule that is behind leaves the instance due
    /// again the moment it fires, so the duplicate fires a second time inside one tick — a boss
    /// summoning two waves on one tick, from a caller bug no combat log would explain.
    /// </remarks>
    [Fact]
    public void A_duplicate_candidate_in_one_call_is_refused()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("BOSS#0/BOSS_THORNMAW_P2_ROOT");
        registry.Register(id, TriggerTestBattle.ThornmawRoot(), activationTick: 0);

        var failure = Should.Throw<EffectContextException>(
            () => registry.PeriodicDue(new[] { id, id }, TriggerTestBattle.At(8.0)));

        failure.Token.ShouldBe(id.Value);
        failure.Message.ShouldContain("twice among the candidates", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A <c>PERIODIC</c> is refused on the <b>instance</b> too, not only through the registry.
    /// </summary>
    /// <remarks>
    /// The registry hands instances out — from <c>Register</c>, from the indexer, from
    /// <c>PeriodicDue</c>, from <c>Instances</c> — so a guard that lived only on the registry wrapper
    /// would leave the ordinary path open: a <c>PERIODIC</c> walked through it passes every gate
    /// (no filter arm, no cooldown, no <c>everyNth</c>, no <c>chance</c>) and fires, leaving the
    /// schedule untouched so it fires again at its scheduled tick.
    /// </remarks>
    [Fact]
    public void A_PERIODIC_cannot_be_fired_through_the_ordinary_path_on_the_instance_either()
    {
        var registry = TriggerTestBattle.Registry();
        var id = TriggerTestBattle.Instance("BOSS#0/BOSS_THORNMAW_P2_ROOT");
        var instance = registry.Register(id, TriggerTestBattle.ThornmawRoot(), activationTick: 0);

        var failure = Should.Throw<EffectContextException>(
            () => instance.Evaluate(TriggerTestBattle.Moment(TriggerKind.PERIODIC, 40), rng: null));

        failure.Token.ShouldBe(nameof(TriggerKind.PERIODIC));
        instance.FireCount.ShouldBe(0, "nothing fired, and the schedule is untouched");
        instance.NextFiringTick.ShouldBe(TriggerTestBattle.At(8.0));
    }

    /// <summary>
    /// 🔒 The tick rate this layer counts in is `05` §3's, the same one the combat log counts in.
    /// </summary>
    /// <remarks>
    /// R17 makes <c>Rules.Effects</c> the bottom of the intra-<c>Rules</c> layering, so
    /// <see cref="TriggerSchedule"/> cannot name <c>CombatLog.TicksPerSecond</c> and states `05` §3's
    /// 20 Hz itself. That is two statements of one fact, which is a defect unless something compares
    /// them — this test is that something, and it lives in the test assembly precisely because the
    /// test assembly is allowed to see both.
    /// </remarks>
    [Fact]
    public void The_tick_rate_agrees_with_the_combat_log()
    {
        TriggerSchedule.TicksPerSecond.ShouldBe(CombatLog.TicksPerSecond);
        TriggerSchedule.MaxSpanTicks.ShouldBe(CombatLog.MaxTicks);
        TriggerTestBattle.TicksPerSecond.ShouldBe(CombatLog.TicksPerSecond);
    }

    /// <summary>
    /// Drives the hand-cranked tick source from 0 to <paramref name="upTo"/> and reports every tick
    /// the instance fired on.
    /// </summary>
    private static IReadOnlyList<int> FiringTicks(TriggerRegistry registry, EffectInstanceId id, int upTo)
    {
        var candidates = new[] { id };
        var fired = new List<int>();

        for (var tick = 0; tick <= upTo; tick++)
        {
            if (registry.PeriodicDue(candidates, tick).Count > 0)
            {
                fired.Add(tick);
            }
        }

        return fired;
    }
}
