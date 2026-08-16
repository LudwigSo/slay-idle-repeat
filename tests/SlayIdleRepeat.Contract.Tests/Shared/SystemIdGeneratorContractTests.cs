using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Contract.Tests.Shared;

/// <summary>The contract, run against the real generator — the one that mints shipped identifiers.</summary>
[ContractFixtureFor(typeof(SystemIdGenerator))]
public sealed class SystemIdGeneratorContractTests : IIdGeneratorPortContractTests
{
    protected override IIdGeneratorPort Create() => new SystemIdGenerator();
}
