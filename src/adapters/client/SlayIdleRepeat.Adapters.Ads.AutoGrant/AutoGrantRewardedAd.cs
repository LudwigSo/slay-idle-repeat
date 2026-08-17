using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Adapters.Ads.AutoGrant;

/// <summary>
/// The subscriber's <see cref="IRewardedAdPort"/>: no ad is shown and the reward is granted
/// instantly.
/// </summary>
/// <remarks>
/// <para>
/// A subscriber has already paid for the thing an ad would have bought, so the honest
/// implementation of "show an ad and report what happened" is to show nothing and report that the
/// reward is owed. There is no inventory to run out of and nothing to preload, which is why every
/// placement is ready and <see cref="PreloadAsync"/> is a completed no-op.
/// </para>
/// <para>
/// 🔒 The outcome carries a <see langword="null"/> verification token, and that is the design
/// rather than an omission: no impression exists, so there is nothing the server could
/// independently check. A manufactured token would be a forgery the server would then have to be
/// taught to accept, and the absent one is what tells it this grant came from an entitlement.
/// </para>
/// <para>
/// Which players get this implementation is the composition root's decision, taken from the
/// server-issued entitlement. Nothing here knows what a subscription is.
/// </para>
/// </remarks>
public sealed class AutoGrantRewardedAd : IRewardedAdPort
{
    /// <inheritdoc/>
    /// <remarks>Always <see langword="true"/>: the grant needs no inventory.</remarks>
    public bool IsReady(string adPlacementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adPlacementId);

        return true;
    }

    /// <inheritdoc/>
    public Task<AdOutcome> ShowAsync(string adPlacementId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adPlacementId);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(new AdOutcome(AdResultKind.Completed, VerificationToken: null));
    }

    /// <inheritdoc/>
    public Task PreloadAsync(string adPlacementId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adPlacementId);

        return Task.CompletedTask;
    }
}
