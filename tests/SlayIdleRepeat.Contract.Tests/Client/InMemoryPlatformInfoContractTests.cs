using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The contract, run against the settable fake every use-case scenario is written over.
/// </summary>
/// <remarks>
/// A freshly constructed fake reports a device that identifies itself, so
/// <see cref="CreateWithUnidentifiedDevice"/> has to ask for the absence — which is the arrangement
/// the fake exists to make reachable, and the one the real host reader is permanently in.
/// </remarks>
[ContractFixtureFor(typeof(InMemoryPlatformInfo))]
public sealed class InMemoryPlatformInfoContractTests : IPlatformInfoPortContractTests
{
    protected override IPlatformInfoPort Create() => new InMemoryPlatformInfo();

    protected override IPlatformInfoPort CreateWithUnidentifiedDevice()
    {
        var platform = new InMemoryPlatformInfo();
        platform.ReportDeviceModel(null);

        return platform;
    }
}
