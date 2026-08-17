using Shouldly;
using SlayIdleRepeat.Application.Ports.Shared;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// States what <see cref="IClockPort"/> <em>means</em> (<c>23</c> §4.3, §5 A8) — written once
/// against the interface so the settable fake and the real system clock are held to one contract.
/// </summary>
/// <remarks>
/// <c>23</c> §4.3 makes this one of the two determinism-critical ports: a test supplies a frozen
/// clock and the reconnect chaos test and the economy simulator become reproducible. That only
/// holds if the fake and the real clock agree on what a reading is, which is what these cases pin.
/// </remarks>
[ContractSuiteFor(typeof(IClockPort))]
public abstract class IClockPortContractTests
{
    /// <summary>
    /// 🔒 The floor a reading must clear, and the reason it is a named constant rather than an
    /// inline literal: the trap this case exists for is a clock that returns
    /// <c>default(DateTimeOffset)</c> — year 1, offset zero — which satisfies "is UTC" and
    /// "is non-decreasing" perfectly. The assertion message has to say what the number means, or the
    /// next reader takes it for an arbitrary date and relaxes it.
    /// </summary>
    protected static readonly DateTimeOffset EpochFloor = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>How far this suite asks the clock to move in the advancement case.</summary>
    protected static readonly TimeSpan AdvancementStep = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 🔒 How much less than <see cref="AdvancementStep"/> a reading may have advanced by and still
    /// pass. This is a flake guard, not a relaxation of the contract.
    /// </summary>
    /// <remarks>
    /// A fixture over a real clock has to wait, and a wait built on the platform's timer can return
    /// a fraction under the span it was asked for: Windows schedules on a ~15.6 ms grid by default,
    /// and <c>Task.Delay</c> is allowed to complete on the tick at or just before the deadline. With
    /// no slack the case fails a few runs in a thousand, and a contract case that fails at random is
    /// one somebody deletes rather than debugs.
    /// <para>
    /// The remaining floor — <see cref="AdvancementStep"/> minus this — is still more than two timer
    /// ticks, so the defects this case exists for all stay caught: a frozen clock advances by zero,
    /// and a clock that ticks once advances by ~15.6 ms. Widening this to where a single tick would
    /// pass hands the case back its vacuity.
    /// </para>
    /// </remarks>
    protected static readonly TimeSpan TimerGranularitySlack = TimeSpan.FromMilliseconds(16);

    /// <summary>A clock of the implementation under test.</summary>
    protected abstract IClockPort Create();

    /// <summary>
    /// Moves the clock forward by at least <paramref name="by"/>. A settable fake sets its time; a
    /// real system clock actually waits.
    /// </summary>
    /// <remarks>
    /// 🔒 An implementation of this hook must not return early on purpose. The slack the assertion
    /// allows is there for timer granularity alone; a fixture that waits for half the span and
    /// leans on the slack is testing the slack.
    /// </remarks>
    protected abstract void Elapse(IClockPort clock, TimeSpan by);

    /// <summary>Every reading is UTC — no implementation hands back a local-zone offset.</summary>
    [Fact]
    public void UtcNow_is_in_UTC()
    {
        var clock = Create();

        clock.UtcNow.Offset.ShouldBe(
            TimeSpan.Zero,
            "IClockPort.UtcNow is UTC by contract. A reading carrying a local offset compares wrong "
            + "against every stored instant, and the failure only reproduces outside UTC+0.");
    }

    /// <summary>A later read never reports an earlier instant.</summary>
    [Fact]
    public void Two_successive_readings_are_non_decreasing()
    {
        var clock = Create();

        var first = clock.UtcNow;
        var second = clock.UtcNow;

        second.ShouldBeGreaterThanOrEqualTo(
            first,
            "a clock that can go backwards makes every elapsed-time computation able to produce a "
            + "negative span, which is how a cooldown becomes permanent.");
    }

    /// <summary>
    /// The reading is a real instant, not <c>default(DateTimeOffset)</c> — the shape a clock that
    /// was never wired takes, and the one that passes every other case in this suite.
    /// </summary>
    [Fact]
    public void UtcNow_is_after_the_stated_epoch_floor()
    {
        var clock = Create();

        clock.UtcNow.ShouldBeGreaterThan(
            EpochFloor,
            $"every reading must be after {EpochFloor:O}, the floor this project's development "
            + "started from. default(DateTimeOffset) is year 1 with offset zero, so an unwired "
            + "clock satisfies 'is UTC' and 'is non-decreasing' and only this case can see it.");
    }

    /// <summary>Time actually passes: after the fixture elapses a span, the reading has moved on by that span.</summary>
    [Fact]
    public void UtcNow_advances_by_at_least_the_span_that_elapsed()
    {
        var clock = Create();
        var before = clock.UtcNow;

        Elapse(clock, AdvancementStep);

        var advance = clock.UtcNow - before;

        advance.ShouldBeGreaterThanOrEqualTo(
            AdvancementStep - TimerGranularitySlack,
            $"the fixture elapsed {AdvancementStep.TotalMilliseconds} ms and the reading moved by "
            + $"{advance.TotalMilliseconds} ms. A frozen clock passes every other case in this "
            + "suite; this is the one that requires the reading to be connected to anything at all. "
            + $"{TimerGranularitySlack.TotalMilliseconds} ms of that is timer-granularity slack for "
            + "a fixture that has to wait on a real clock — it is not room for a clock that ticks "
            + "once and stops.");
    }
}
