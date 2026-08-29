using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>The contract, run against the scripted fake every client scenario is written over.</summary>
/// <remarks>
/// It is handed the shipped content set for the same reason the in-process host is: the fake mints
/// a real starting player rather than a made-up slice, so the states these cases reach are states
/// the domain can actually produce.
/// </remarks>
[ContractFixtureFor(typeof(InMemoryGameHost))]
public sealed class InMemoryGameHostContractTests : IGameHostContractTests
{
    protected override IGameHost Create() => new InMemoryGameHost(ClientWorlds.Content);
}
