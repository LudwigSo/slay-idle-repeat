using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>The ward pool cap: <c>wardCapPct</c> × the actor's post-multiplier Max HP.</summary>
/// <remarks>
/// Two obligations nothing else can enforce: the cap must read the post-multiplier Max HP (keeping
/// only the final block caps every glass-cannon-shaped ward at 1 HP with nothing going red), and it
/// must be re-read on every re-aggregation, since an enrage effect makes a boss's post-multiplier
/// Max HP not a battle constant. Internal bench seam: the cases grant wards at chosen ticks and read
/// the pool and the post-step-7 basis mid-fight, none of which a public entry point's log carries.
/// </remarks>
public sealed class WardCapTests
{
    private const double BaseMaxHp = 1000.0;

    // ══════════════════════════════════ obligation 1 — post-multiplier Max HP, not Final

    /// <summary>
    /// A glass-cannon-shaped perk's shields stay functional, which is why the cap reads
    /// post-multiplier Max HP rather than the final (post-<c>STAT_SET</c>) block: the final block
    /// says 1 Max HP and the post-multiplier reading says 2000.
    /// </summary>
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

    // ══════════════════════════════════ obligation 2 — re-read, never cached

    /// <summary>
    /// The cap is re-read on every re-aggregation: a boss whose post-multiplier Max HP grows
    /// mid-fight has a ward pool that grows with it. A cap taken once at battle start would leave the
    /// pool full for the whole fight, so the second grant would return 0 either way; the
    /// <c>enraged: false</c> row is the negative control that pins that 0 as the honest answer.
    /// </summary>
    [Theory]
    [InlineData(true, 2000.0, 1000.0)]
    [InlineData(false, 1000.0, 0.0)]
    public void The_cap_is_re_read_on_every_re_aggregation_and_never_cached_at_battle_start(
        bool enraged, double expectedLateCap, double expectedSecondGrant)
    {
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
    /// <c>PostMultiplierMaxHp</c> changes on the tick the condition flips, and not a tick later — a
    /// cap refreshed lazily on the next grant would pass the case above but not this.
    /// </summary>
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
    /// <c>wardCapPct</c> is read from data too — the same fight at three ceilings. The 0.25 row
    /// separates "clips at wardCapPct × Max HP" from "clips at Max HP".
    /// </summary>
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
    /// The tick loop itself expires a due ward segment; the probe never calls
    /// <c>ExpireWards</c> by hand. Two expiry ticks so a sweep hard-wired to one cannot pass both, and
    /// the reading is <c>Wards.Total</c> at the tick (one tick behind the expiry, by the bench's
    /// slot geometry) rather than the log, since a never-swept pool and one swept into an empty log
    /// look the same in the events.
    /// </summary>
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
    /// The negative control for <see cref="The_tick_loop_expires_a_due_ward_segment"/> — a segment
    /// with no timer is never swept. Without it, a sweep dropping every segment on every tick would
    /// pass the theory above on both rows.
    /// </summary>
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
    /// Asserted end to end over the fight's log rather than the pool's return values. Both halves
    /// reach the same end state by two different routes, so the assertion is about the route. This
    /// calls <c>ExpireWards</c> by hand and so pins the mechanism only; the routing is
    /// <see cref="The_tick_loop_expires_a_due_ward_segment"/>'s.
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
                p.Pipeline.DealMaxHpPctDamage(p.Hero, 100.0, bypassesWards: false, "EFF_D", source: null);

                p.Hero.Wards.Total.ShouldBe(0.0);
            });

        byDamage.EventsOf(CombatEventType.WardBroken).Count.ShouldBe(1);
        byDamage.EventsOf(CombatEventType.StatusExpired).ShouldBeEmpty();
    }

    /// <summary>
    /// <c>Shield</c> fires on every grant, clipped ones included: a cast the player watched happen
    /// must have an event to draw, and its value being 0 is the information.
    /// </summary>
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

    /// <summary>See <see cref="AttackPipelineBench.Stats"/> for the two defaults.</summary>
    private static ActorStats Stats(double maxHp) => AttackPipelineBench.Stats(maxHp);
}
