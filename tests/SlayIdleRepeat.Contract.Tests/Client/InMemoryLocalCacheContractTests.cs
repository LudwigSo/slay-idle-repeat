using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The contract, run against the in-memory fake — the implementation every use-case test caches
/// through, held to the file-backed adapter's answers.
/// </summary>
[ContractFixtureFor(typeof(InMemoryLocalCache))]
public sealed class InMemoryLocalCacheContractTests : ILocalCachePortContractTests
{
    protected override ILocalCachePort Create() => new InMemoryLocalCache();

    protected override ILocalCachePort Reopen(ILocalCachePort cache) =>
        ((InMemoryLocalCache)cache).Reopen();
}
