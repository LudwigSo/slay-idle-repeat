using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Triggers;

/// <summary>
/// 🔒 The tick arithmetic behind every timed trigger — the guards that keep `05` §3's fixed-tick
/// simulation from acquiring a floating-point accumulation point.
/// </summary>
public sealed class TriggerScheduleTests
{
    /// <summary>Whole seconds convert exactly, which is the point of counting in ticks.</summary>
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.05, 1)]
    [InlineData(1.0, 20)]
    [InlineData(1.2, 24)]
    [InlineData(8.0, 160)]
    [InlineData(70.0, 1400)]
    [InlineData(90.0, 1800)]
    public void A_span_of_seconds_becomes_whole_ticks(double seconds, int expected)
    {
        TriggerSchedule.Ticks(seconds, "interval", "PERIODIC").ShouldBe(expected);
    }

    /// <summary>
    /// 🔒 A span that is not a whole number of ticks is refused rather than rounded — the same rule,
    /// for the same reason, that <c>CombatLog.AppendTelegraph</c> applies to a telegraph's lead.
    /// </summary>
    /// <remarks>
    /// A trigger at <c>tick + 0.6</c> points between two ticks and therefore at neither, and rounding
    /// it silently would make two implementations that each rounded differently disagree about the
    /// fight.
    /// </remarks>
    [Theory]
    [InlineData(0.03)]
    [InlineData(1.0001)]
    [InlineData(7.777)]
    public void A_fractional_span_is_refused(double seconds)
    {
        var failure = Should.Throw<EffectContextException>(
            () => TriggerSchedule.Ticks(seconds, "interval", "PERIODIC"));

        failure.Token.ShouldBe("PERIODIC");
        failure.Message.ShouldContain("fixed-tick", Case.Sensitive);
        failure.Message.ShouldContain("interval", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 A span that cannot be a span at all is refused before the cast — an overflow that wrapped
    /// into a negative tick would read exactly like a trigger that fires immediately.
    /// </summary>
    [Theory]
    [InlineData(-1.0)]
    [InlineData(90.05)]
    [InlineData(1e18)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_span_no_fight_can_reach_is_refused(double seconds)
    {
        Should.Throw<EffectContextException>(
            () => TriggerSchedule.Ticks(seconds, "startDelay", "PERIODIC"));
    }

    /// <summary>
    /// 🔒 Steering S6 — a <c>PERIODIC</c> with no <c>interval</c> is refused, never defaulted. With
    /// no N there is no <em>"every N seconds"</em>.
    /// </summary>
    [Fact]
    public void A_PERIODIC_with_no_interval_is_refused()
    {
        var failure = Should.Throw<EffectContextException>(
            () => TriggerSchedule.IntervalTicks(new EffectTrigger { Kind = TriggerKind.PERIODIC }));

        failure.Token.ShouldBe(nameof(TriggerKind.PERIODIC));
        failure.Message.ShouldContain("carries no interval", Case.Sensitive);
        failure.Message.ShouldContain("defaulting one would invent", Case.Sensitive);
    }

    /// <summary>An interval shorter than one tick would fire unboundedly inside one tick.</summary>
    [Fact]
    public void An_interval_shorter_than_a_tick_is_refused()
    {
        Should.Throw<EffectContextException>(() => TriggerSchedule.IntervalTicks(
            new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 0.0 }));
    }

    /// <summary>
    /// 🔒 R8 and the <c>startDelay</c> ruling, as arithmetic: the first firing is
    /// <c>anchor + (startDelay ?? interval)</c>.
    /// </summary>
    [Theory]
    [InlineData(8.0, null, 0, 160)]
    [InlineData(8.0, null, 600, 760)]
    [InlineData(1.0, 70.0, 0, 1400)]
    [InlineData(1.0, 70.0, 240, 1640)]
    [InlineData(14.0, 0.0, 400, 400)]
    public void The_first_firing_is_the_anchor_plus_the_start_delay(
        double interval, double? startDelay, int anchorTick, int expected)
    {
        var trigger = new EffectTrigger
        {
            Kind = TriggerKind.PERIODIC,
            Interval = interval,
            StartDelay = startDelay,
        };

        TriggerSchedule.FirstFiringTick(trigger, anchorTick).ShouldBe(expected);
    }

    /// <summary>
    /// 🔒 An explicit <c>startDelay: 0</c> is honoured — the absent-means-one-interval ruling is
    /// about the <b>absence</b>, and an author who writes zero means zero.
    /// </summary>
    /// <remarks>
    /// The row above proves the arithmetic; this states the distinction, because a default
    /// implemented as <c>StartDelay ?? Interval</c> and one implemented as
    /// <c>StartDelay is null or 0 ? Interval : StartDelay</c> differ only here.
    /// </remarks>
    [Fact]
    public void An_explicit_zero_start_delay_fires_on_the_anchor_tick()
    {
        var onAnchor = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 14.0, StartDelay = 0.0 };
        var absent = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 14.0 };

        TriggerSchedule.FirstFiringTick(onAnchor, 400).ShouldBe(400);
        TriggerSchedule.FirstFiringTick(absent, 400).ShouldBe(400 + TriggerTestBattle.At(14.0));
    }

    /// <summary>An absent <c>cooldown</c> is no cooldown, and a written one is its span in ticks.</summary>
    [Fact]
    public void A_cooldown_is_its_span_in_ticks_and_absent_means_none()
    {
        TriggerSchedule.CooldownTicks(new EffectTrigger { Kind = TriggerKind.ON_HIT_TAKEN })
                       .ShouldBe(0);

        TriggerSchedule.CooldownTicks(new EffectTrigger { Kind = TriggerKind.ON_HIT_TAKEN, Cooldown = 6.0 })
                       .ShouldBe(120);
    }
}
