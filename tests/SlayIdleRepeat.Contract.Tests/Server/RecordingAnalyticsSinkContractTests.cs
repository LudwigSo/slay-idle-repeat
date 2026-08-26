using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// The contract, run against the recording fake — every analytics-facing scenario elsewhere runs on
/// this implementation, so it is held to the sink the game actually ships with.
/// </summary>
[ContractFixtureFor(typeof(RecordingAnalyticsSink))]
public sealed class RecordingAnalyticsSinkContractTests : IAnalyticsSinkPortContractTests
{
    protected override IAnalyticsSinkPort Create() => new RecordingAnalyticsSink();
}
