using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §4.1's ward pool cap — 📐 <c>wardCapPct</c> × the actor's Max HP <b>as it stood after `18`
/// §8 step 7</b>.
/// </summary>
/// <remarks>
/// Two obligations nothing else can enforce: the actor must hold the whole <c>AggregatedStats</c>
/// (keeping <c>Final</c> and discarding the wrapper caps every <c>CP_GLASS_HEART</c> ward at 1 HP
/// with nothing going red), and it must be <b>re-read</b> on every re-aggregation, since
/// <c>SYS_ENRAGE</c> makes a boss's post-step-7 Max HP not a battle constant. Each case is written so
/// the wrong implementation produces a specific different number rather than an absence.
/// </remarks>
public sealed class WardCapTests
{
    private const double BaseMaxHp = 1000.0;

    // ══════════════════════════════════ obligation 1 — the whole record, not Final

    /// <summary>
    /// 🔒 `05` §4.1 / `18` §9.1 — <c>CP_GLASS_HEART</c>'s shields stay functional, which is why the
    /// cap reads post-step-7 Max HP at all.
    /// </summary>
    /// <remarks>
    /// The final block says <b>1</b> Max HP and the post-step-7 reading says <b>2000</b> — a factor of
    /// two thousand between right and wrong. The three rows are the three behaviours to tell apart:
    /// <b>under</b> the cap (fails if <c>Final</c> is read), <b>over</b> it (fails if the cap is
    /// ignored), and <b>at</b> it.
    /// </remarks>
    [Theory]
    [InlineData(1500.0, 1500.0)]
    [InlineData(2500.0, 2000.0)]
    [InlineData(2000.0, 2000.0)]
    public void The_cap_reads_post_step_7_Max_HP_so_CP_GLASS_HEART_keeps_its_shields(
        double granted, double expected) =>
        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(
                    Stats(BaseMaxHp),
                    1,
                    AttackPipelineBench.Standing(
                        "CP_GLASS_HEART_A", EffectOp.STAT_MULT, StatSelector.AllCombat, 2.0),
                    AttackPipelineBench.Standing(
                        "CP_GLASS_HEART_B", EffectOp.STAT_SET, StatSelector.Of(StatId.MAX_HP), 1.0)),
                BattleTestBench.Enemy(0, Stats(5000.0)),
            },
            p =>
            {
                p.Hero.Stats[StatId.MAX_HP].ShouldBe(1.0, "`18` §9.1 — STAT_SET wins at step 8");
                p.Hero.PostMultiplierMaxHp.ShouldBe(2000.0, "`18` §8 step 7 — 1000 x 2");

                p.Pipeline.GrantWard(p.Hero, granted, null, "PK_WARDED");

                p.Hero.Wards.Total.ShouldBe(expected);
            });

    /// <summary>
    /// 🔒 The obligation stated over the actor: <c>BattleActor</c> holds the whole
    /// <c>AggregatedStats</c>, so the reading survives at all.
    /// </summary>
    /// <remarks>
    /// The case above would also pass if some other route to 2000 existed; this asserts the identity.
    /// The two readings come off the same actor and must differ, which is only possible if the wrapper
    /// was kept.
    /// </remarks>
    [Fact]
    public void A_battle_actor_holds_the_whole_aggregation_and_not_only_its_final_block() =>
        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(
                    Stats(BaseMaxHp),
                    1,
                    AttackPipelineBench.Standing(
                        "CP_GLASS_HEART_A", EffectOp.STAT_MULT, StatSelector.AllCombat, 2.0),
                    AttackPipelineBench.Standing(
                        "CP_GLASS_HEART_B", EffectOp.STAT_SET, StatSelector.Of(StatId.MAX_HP), 1.0)),
                BattleTestBench.Enemy(0, Stats(5000.0)),
            },
            p =>
            {
                p.Hero.Aggregated.Final.ShouldBeSameAs(p.Hero.Stats);
                p.Hero.Aggregated.PostMultiplierMaxHp.ShouldBe(p.Hero.PostMultiplierMaxHp);
                p.Hero.PostMultiplierMaxHp.ShouldNotBe(p.Hero.Stats[StatId.MAX_HP]);
            });

    // ══════════════════════════════════ obligation 2 — re-read, never cached

    /// <summary>
    /// 🔒 The cap is <b>re-read on every re-aggregation</b> — a boss whose post-step-7 Max HP grows
    /// mid-fight has a ward pool that grows with it.
    /// </summary>
    /// <remarks>
    /// ⚠️ Driven by a <c>BATTLE_TIME</c>-gated <c>STAT_MULT</c> rather than the literal
    /// <c>SYS_ENRAGE</c> — see <see cref="AttackPipelineBench.EnrageShaped"/> for why a
    /// <c>PERIODIC STAT_MULT</c> moves no stat on this branch.
    /// <para>
    /// 🔒 A cap taken once at battle start leaves the pool full for the whole fight, so the second
    /// grant returns <b>0</b>; a cap that grew returns the 1000 of new headroom. The
    /// <c>enraged: false</c> row is the negative control and asserts exactly that 0.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(true, 2000.0, 1000.0)]
    [InlineData(false, 1000.0, 0.0)]
    public void The_cap_is_re_read_on_every_re_aggregation_and_never_cached_at_battle_start(
        bool enraged, double expectedLateCap, double expectedSecondGrant)
    {
        // 70 s is `05` §3.1's SYS_ENRAGE startDelay; at 20 ticks/second that is tick 1400.
        const int enrageTick = 70 * CombatLog.TicksPerSecond;

        var seen = new List<(int Tick, double Cap, double Granted)>();

        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(Stats(BaseMaxHp), 1),
                BattleTestBench.Enemy(
                    0,
                    Stats(BaseMaxHp),
                    isBoss: false,
                    effects: new[]
                    {
                        AttackPipelineBench.EnrageShaped(
                            "SYS_ENRAGE", StatId.MAX_HP, 2.0, enraged ? 70.0 : 1e9),
                    }),
            },
            p =>
            {
                if (p.Services.Tick is not (0 or enrageTick))
                {
                    return;
                }

                var before = p.Enemy().Wards.Total;
                p.Pipeline.GrantWard(p.Enemy(), 1500.0, null, "PK_WARDED");

                seen.Add((
                    p.Services.Tick,
                    p.Enemy().PostMultiplierMaxHp,
                    p.Enemy().Wards.Total - before));
            },
            maxTicks: enrageTick + 1);

        seen.Count.ShouldBe(2);

        seen[0].Tick.ShouldBe(0);
        seen[0].Cap.ShouldBe(BaseMaxHp, "before the multiplier, the post-step-7 Max HP is the base");
        seen[0].Granted.ShouldBe(BaseMaxHp, "1500 clipped to the 1000 cap");

        seen[1].Tick.ShouldBe(enrageTick);
        seen[1].Cap.ShouldBe(expectedLateCap);
        seen[1].Granted.ShouldBe(expectedSecondGrant);
    }

    /// <summary>
    /// 🔒 The same re-read stated over the reading itself: <c>PostMultiplierMaxHp</c> changes on the
    /// tick the condition flips, and not a tick later.
    /// </summary>
    /// <remarks>
    /// A cap refreshed lazily — on the next grant, say — would report the new number eventually and
    /// pass the case above. This pins the boundary at <c>BATTLE_TIME &gt;= 70</c>, tick 1400 exactly.
    /// </remarks>
    [Fact]
    public void The_post_step_7_reading_moves_on_the_exact_tick_the_multiplier_switches_on()
    {
        const int enrageTick = 70 * CombatLog.TicksPerSecond;

        var readings = new Dictionary<int, double>();

        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(Stats(BaseMaxHp), 1),
                BattleTestBench.Enemy(
                    0,
                    Stats(BaseMaxHp),
                    effects: new[]
                    {
                        AttackPipelineBench.EnrageShaped("SYS_ENRAGE", StatId.MAX_HP, 2.0, 70.0),
                    }),
            },
            p =>
            {
                if (p.Services.Tick is enrageTick - 1 or enrageTick)
                {
                    readings[p.Services.Tick] = p.Enemy().PostMultiplierMaxHp;
                }
            },
            maxTicks: enrageTick + 1);

        BattleClock.SecondsAt(enrageTick).ShouldBe(70.0);

        readings[enrageTick - 1].ShouldBe(BaseMaxHp, "battle time is 69.95 s");
        readings[enrageTick].ShouldBe(2.0 * BaseMaxHp, "battle time is 70.0 s");
    }

    /// <summary>
    /// 🔒 The 📐 <c>wardCapPct</c> is read from data too — the same fight at three ceilings.
    /// </summary>
    /// <remarks>
    /// The <c>0.25</c> row separates "clips at <c>wardCapPct × Max HP</c>" from "clips at Max HP": at
    /// the shipped 1.0 the two are numerically the same.
    /// </remarks>
    [Theory]
    [InlineData(1.0, 1000.0)]
    [InlineData(0.25, 250.0)]
    [InlineData(3.0, 3000.0)]
    public void The_pool_ceiling_is_wardCapPct_times_the_basis(double wardCapPct, double expected) =>
        AttackPipelineBench.Run(
            new[]
            {
                BattleTestBench.Hero(Stats(BaseMaxHp), 1),
                BattleTestBench.Enemy(0, Stats(5000.0)),
            },
            p =>
            {
                p.Pipeline.GrantWard(p.Hero, 9999.0, null, "PK_WARDED");

                p.Hero.Wards.Total.ShouldBe(expected);
            },
            wardCapPct: wardCapPct);

    // ══════════════════════════════════ the expiry side, in a real fight

    /// <summary>
    /// 🔴 `05` §3.1 slot 2 — <b>the tick loop</b> expires a due ward segment. Nothing else does.
    /// </summary>
    /// <remarks>
    /// 🔒 The probe never calls <c>ExpireWards</c>, and that is the whole test:
    /// <see cref="Expiry_emits_StatusExpired_and_damage_emits_WardBroken_over_the_same_end_state"/>
    /// calls it by hand, which pins the mechanism and hid the routing's absence — every segment
    /// carrying a timer lasted the whole fight while that test stayed green.
    /// <para>
    /// 🔒 Two expiry ticks so a sweep hard-wired to one cannot pass both, plus a <c>null</c>-timer
    /// control. The reading is <c>Wards.Total</c> <em>at</em> the tick rather than the log, because a
    /// pool that never swept and one swept into an empty log look the same in the events. It sits one
    /// tick behind the expiry by the bench's geometry: the probe fires from slot 1 and the sweep is
    /// slot 2 of the same tick.
    /// </para>
    /// <para>
    /// ⚠️ The fight wires <c>NoStatusTimeline</c> deliberately — a sweep living behind
    /// <c>IStatusTimeline.ExpireDue</c> is a no-op here, and here is exactly where a `05` §4.2
    /// <c>SHIELD</c> lives. That is the case the first fix got wrong.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void The_tick_loop_expires_a_due_ward_segment(int expiresAtTick)
    {
        var totals = new Dictionary<int, double>();

        AttackPipelineBench.Run(
            new[] { BattleTestBench.Hero(Stats(BaseMaxHp), 1), BattleTestBench.Enemy(0, Stats(5000.0)) },
            p =>
            {
                if (p.Services.Tick == 0)
                {
                    p.Services.GrantWard(p.Hero, 100.0, null, "PK_WARDED", expiresAtTick);
                }

                totals[p.Services.Tick] = p.Hero.Wards.Total;
            },
            maxTicks: 6);

        totals[expiresAtTick].ShouldBe(
            100.0, "slot 1 of the expiry tick is still ahead of slot 2's sweep");
        totals[expiresAtTick + 1].ShouldBe(
            0.0, "`05` §3.1 slot 2 swept it, with no help from the probe");
    }

    /// <summary>
    /// 🔒 The negative control for <see cref="The_tick_loop_expires_a_due_ward_segment"/> — a segment
    /// with no timer is never swept.
    /// </summary>
    /// <remarks>
    /// Without it, a slot 2 dropping <em>every</em> segment on every tick passes the theory above on
    /// both rows. `05` §4.1's <c>expiresAt?</c> is optional and every production <c>GrantWard</c>
    /// passes <c>null</c> today.
    /// </remarks>
    [Fact]
    public void A_ward_segment_with_no_timer_is_never_swept()
    {
        var totals = new Dictionary<int, double>();

        AttackPipelineBench.Run(
            new[] { BattleTestBench.Hero(Stats(BaseMaxHp), 1), BattleTestBench.Enemy(0, Stats(5000.0)) },
            p =>
            {
                if (p.Services.Tick == 0)
                {
                    p.Services.GrantWard(p.Hero, 100.0, null, "PK_WARDED", expiresAtTick: null);
                }

                totals[p.Services.Tick] = p.Hero.Wards.Total;
            },
            maxTicks: 6);

        totals.Values.ShouldAllBe(total => total == 100.0);
        totals.Count.ShouldBe(6, "floored so ShouldAllBe cannot pass on an empty collection");
    }

    /// <remarks>
    /// 🔒 The distinction `18` §6's <c>until: WARD_BROKEN</c> terminator is built on, asserted end to
    /// end over the fight's log rather than the pool's return values. Both halves reach the <b>same
    /// end state</b> by the two routes, so the assertion is about the route.
    /// <para>
    /// ⚠️ This calls <c>ExpireWards</c> by hand and so pins the <b>mechanism</b> only; the
    /// <b>routing</b> is <see cref="The_tick_loop_expires_a_due_ward_segment"/>'s.
    /// </para>
    /// </remarks>
    [Fact]
    public void Expiry_emits_StatusExpired_and_damage_emits_WardBroken_over_the_same_end_state()
    {
        var byExpiry = AttackPipelineBench.Run(
            new[] { BattleTestBench.Hero(Stats(BaseMaxHp), 1), BattleTestBench.Enemy(0, Stats(5000.0)) },
            p =>
            {
                if (p.Services.Tick == 0)
                {
                    p.Services.GrantWard(p.Hero, 100.0, null, "PK_WARDED", expiresAtTick: 2);
                    return;
                }

                if (p.Services.Tick == 2)
                {
                    p.Services.ExpireWards(p.Hero).ShouldBe(1);
                    p.Hero.Wards.Total.ShouldBe(0.0);
                }
            },
            maxTicks: 3);

        byExpiry.EventsOf(CombatEventType.StatusExpired).Single().Value.ShouldBe(100.0);
        byExpiry.EventsOf(CombatEventType.WardBroken).ShouldBeEmpty(
            "`05` §4.1: expiry 'does NOT fire WardBroken'");

        var byDamage = AttackPipelineBench.Run(
            new[] { BattleTestBench.Hero(Stats(BaseMaxHp), 1), BattleTestBench.Enemy(0, Stats(5000.0)) },
            p =>
            {
                p.Pipeline.GrantWard(p.Hero, 100.0, null, "PK_WARDED");
                p.Pipeline.DealMaxHpPctDamage(p.Hero, 100.0, bypassesWards: false, "EFF_D");

                p.Hero.Wards.Total.ShouldBe(0.0);
            });

        byDamage.EventsOf(CombatEventType.WardBroken).Count.ShouldBe(1);
        byDamage.EventsOf(CombatEventType.StatusExpired).ShouldBeEmpty();
    }

    /// <summary>🔒 `05` §4.1 — <c>Shield</c> fires on <b>every</b> grant, clipped ones included.</summary>
    /// <remarks>
    /// A grant clipped to nothing still emits, because `05` §8 makes the log the replay: a cast the
    /// player watched happen must have an event to draw, and its value being 0 is the information.
    /// </remarks>
    [Fact]
    public void Shield_fires_on_every_grant_including_one_clipped_to_nothing()
    {
        var probe = AttackPipelineBench.Run(
            new[] { BattleTestBench.Hero(Stats(BaseMaxHp), 1), BattleTestBench.Enemy(0, Stats(5000.0)) },
            p =>
            {
                p.Pipeline.GrantWard(p.Hero, 1000.0, null, "PK_WARDED");
                p.Pipeline.GrantWard(p.Hero, 500.0, null, "PK_AEGIS");
            });

        probe.EventsOf(CombatEventType.Shield)
            .Select(e => e.Value)
            .ShouldBe(new[] { 1000.0, 0.0 });
    }

    /// <summary>`05` §1's block — see <see cref="AttackPipelineBench.Stats"/> for the two defaults.</summary>
    private static ActorStats Stats(double maxHp) => AttackPipelineBench.Stats(maxHp);
}
