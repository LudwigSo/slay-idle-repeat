using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// The contract, run against the recording fake — every telemetry-facing scenario elsewhere runs on
/// this implementation, so it is held to the port the game actually ships with.
/// </summary>
[ContractFixtureFor(typeof(RecordingTelemetry))]
public sealed class RecordingTelemetryContractTests : ITelemetryPortContractTests
{
    protected override ITelemetryPort Create() => new RecordingTelemetry();
}
