using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Services.Events;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The contract, run against the composed in-process host — the implementation the client actually
/// boots on today.
/// </summary>
/// <remarks>
/// Composed over the in-memory adapters rather than the file-backed cache: the pairing of the real
/// cache with the world-slice store is <c>ILocalCachePort</c>'s own suite's subject, and wiring it
/// here would make every failure in this file ambiguous between the host and the disk.
/// </remarks>
[ContractFixtureFor(typeof(InProcessGameHost))]
public sealed class InProcessGameHostContractTests : IGameHostContractTests
{
    protected override IGameHost Create() =>
        new InProcessGameHost(
            new InMemoryLocalCache(),
            new AdjustableClock(),
            new CountingIdGenerator(),
            ClientWorlds.Content,
            LocalHostAmbience.NoSubscriptionResolved(),
            LocalHostAmbience.NoRemoteConfigResolved(),
            Array.Empty<IDomainEventSink>());
}
