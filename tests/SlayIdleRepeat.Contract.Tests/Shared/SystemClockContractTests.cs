using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// The contract, run against the real system clock — the implementation both hosts actually boot
/// on.
/// </summary>
/// <remarks>
/// 🔒 <see cref="Elapse"/> waits the whole span and then some. The suite allows 16 ms of slack for
/// the platform's timer grid, and that allowance belongs to the OS, not to a fixture that returns
/// early and lets the assertion absorb the difference.
/// </remarks>
[ContractFixtureFor(typeof(SystemClock))]
public sealed class SystemClockContractTests : IClockPortContractTests
{
    /// <summary>Waited on top of the requested span, so no reading depends on the suite's slack.</summary>
    private static readonly TimeSpan Overshoot = TimeSpan.FromMilliseconds(50);

    protected override IClockPort Create() => new SystemClock();

    protected override void Elapse(IClockPort clock, TimeSpan by) => Thread.Sleep(by + Overshoot);
}
