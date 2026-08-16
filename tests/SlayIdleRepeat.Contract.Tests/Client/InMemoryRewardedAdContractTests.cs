using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The contract, run against the in-memory fake — the implementation every use-case test offers
/// its rewarded ads through.
/// </summary>
[ContractFixtureFor(typeof(InMemoryRewardedAd))]
public sealed class InMemoryRewardedAdContractTests : IRewardedAdPortContractTests
{
    protected override IRewardedAdPort Create() => new InMemoryRewardedAd();

    /// <summary>A placement a preload makes ready, with no network and no vendor SDK involved.</summary>
    protected override string ReadyPlacement => "second_chance";
}
