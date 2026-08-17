using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>
/// The contract, run against the counting fake — the implementation every use-case test draws its
/// identifiers from, and the one the cross-instance case exists to keep honest.
/// </summary>
[ContractFixtureFor(typeof(CountingIdGenerator))]
public sealed class CountingIdGeneratorContractTests : IIdGeneratorPortContractTests
{
    protected override IIdGeneratorPort Create() => new CountingIdGenerator();
}
