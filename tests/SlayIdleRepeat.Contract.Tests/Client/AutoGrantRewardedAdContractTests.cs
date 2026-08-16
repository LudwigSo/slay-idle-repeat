using SlayIdleRepeat.Adapters.Ads.AutoGrant;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The contract, run against the subscriber auto-grant adapter — a real implementation that shows
/// no ad, and the reason the suite states "a completed outcome does not imply a token" rather than
/// the biconditional.
/// </summary>
[ContractFixtureFor(typeof(AutoGrantRewardedAd))]
public sealed class AutoGrantRewardedAdContractTests : IRewardedAdPortContractTests
{
    protected override IRewardedAdPort Create() => new AutoGrantRewardedAd();

    /// <summary>Any placement: this adapter needs no inventory, so every one of them is ready.</summary>
    protected override string ReadyPlacement => "revive_run";
}
