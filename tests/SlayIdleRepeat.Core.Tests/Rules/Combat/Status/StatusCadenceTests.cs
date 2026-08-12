using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Status;

/// <summary>
/// 🔒 `05` §3.1's DoT/HoT cadence — <em>"on the 20th simulation tick after first application, and
/// every 20 ticks thereafter"</em>.
/// </summary>
public sealed class StatusCadenceTests
{
    /// <summary>
    /// 🔴 The anchor is the <b>first application</b>, not the battle — probed at tick 7, which is the
    /// only kind of anchor that can tell the two apart.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>An anchor of 0 cannot fail this test and must not be the probe.</b> A status applied on
    /// tick 0 ticks on 20, 40, 60 — and so does an implementation that counted from battle start and
    /// ignored the anchor entirely. The two readings are byte-identical at every anchor that is a
    /// multiple of 20, which is exactly the anchor a hand-written fight produces. Anchored at 7 they
    /// separate completely: this rule expects 27, 47, 67 and the battle-anchored one would say
    /// 20, 40, 60, with no tick in common in the first three seconds.
    /// </remarks>
    [Fact]
    public void The_cadence_is_anchored_on_first_application_and_not_on_the_battle()
    {
        var landings = Enumerable.Range(0, 100).Where(t => StatusCadence.LandsOn(7, t)).ToArray();

        landings.ShouldBe(new[] { 27, 47, 67, 87 });

        // The negative control: the battle-anchored reading's own boundaries are not boundaries here.
        StatusCadence.LandsOn(7, 20).ShouldBeFalse();
        StatusCadence.LandsOn(7, 40).ShouldBeFalse();
        StatusCadence.LandsOn(7, 60).ShouldBeFalse();
    }

    /// <summary>
    /// 🔒 The first tick is the 20th <b>after</b> application — never on the application tick itself.
    /// </summary>
    /// <remarks>
    /// 🔴 The off-by-one this closes is worth a third of every DoT in the game: a 3 s <c>BURN</c>
    /// ticking on its own application tick as well deals four ticks instead of three, on a fight that
    /// otherwise looks entirely correct. Probed at two anchors, because <c>0 % 20 == 0</c> makes the
    /// bug reachable at every anchor and the second shape is what stops the rule being an assertion
    /// about tick 0.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(19)]
    [InlineData(1799)]
    public void An_instance_never_ticks_on_the_tick_it_was_applied_on(int anchor)
    {
        StatusCadence.LandsOn(anchor, anchor).ShouldBeFalse();
        StatusCadence.LandsOn(anchor, anchor + StatusCadence.TicksPerCadence).ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 `05` §3 — the cadence is <em>"once per second of battle time"</em>, and one second is `05`
    /// §3's twenty ticks.
    /// </summary>
    /// <remarks>
    /// Stated against <c>CombatLog.TicksPerSecond</c> rather than against the literal 20, because the
    /// claim is that the two are the same fact. A cadence constant that drifted from the tick rate
    /// would make "once per second" false while every arithmetic test of the cadence stayed green.
    /// </remarks>
    [Fact]
    public void One_cadence_is_one_second_of_05_section_3s_clock()
    {
        StatusCadence.TicksPerCadence.ShouldBe(CombatLog.TicksPerSecond);
        BattleClock.SecondsAt(StatusCadence.TicksPerCadence).ShouldBe(1.0);
    }

    /// <summary>
    /// A tick before the anchor is never a boundary — an instance applied at tick 40 does not tick
    /// at 20.
    /// </summary>
    /// <remarks>
    /// The negative control for the modulo: <c>(20 − 40) % 20 == 0</c> in C#, so an implementation
    /// that tested only the remainder and not the sign would fire on every earlier boundary as well.
    /// </remarks>
    [Fact]
    public void No_boundary_falls_before_the_anchor()
    {
        StatusCadence.LandsOn(40, 20).ShouldBeFalse();
        StatusCadence.LandsOn(40, 0).ShouldBeFalse();
        StatusCadence.LandsOn(40, 60).ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 The per-tick answer and the count agree — a fight's boundaries are exactly the ticks
    /// <see cref="StatusCadence.LandsOn"/> reports.
    /// </summary>
    /// <remarks>
    /// Two independent statements of one rule, held against each other over a whole 90 s fight. A
    /// counting bug and a boundary bug do not look alike, so a single expression of the rule tested
    /// against itself would be a tautology; this is the second expression.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(13)]
    public void The_boundary_test_and_the_landed_count_are_the_same_rule(int anchor)
    {
        var counted = 0;

        for (var tick = 0; tick < CombatLog.MaxTicks; tick++)
        {
            if (StatusCadence.LandsOn(anchor, tick))
            {
                counted++;
            }

            StatusCadence.TicksLandedBy(anchor, tick).ShouldBe(counted);
        }

        // The floor: a fight that produced no boundaries at all would satisfy every line above.
        counted.ShouldBeGreaterThan(80);
    }

    /// <summary>
    /// A negative anchor is refused rather than producing boundaries before the fight started.
    /// </summary>
    [Fact]
    public void A_negative_anchor_is_refused()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => StatusCadence.LandsOn(-1, 20));

        thrown.Message.ShouldContain("anchorTick", Case.Sensitive);
    }
}
