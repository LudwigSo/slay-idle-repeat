using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// The contract, run against the settable fake — every time-dependent use-case test runs on this
/// implementation, so it is held to the clock the game actually ships with.
/// </summary>
[ContractFixtureFor(typeof(AdjustableClock))]
public sealed class AdjustableClockContractTests : IClockPortContractTests
{
    protected override IClockPort Create() => new AdjustableClock();

    protected override void Elapse(IClockPort clock, TimeSpan by) => ((AdjustableClock)clock).Advance(by);
}
