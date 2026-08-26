using SlayIdleRepeat.Core;

namespace SlayIdleRepeat.Application.Hosting;

/// <summary>
/// The two ambient values nothing in this build can resolve, spelled as the absence they are rather
/// than as a plausible-looking guess.
/// </summary>
/// <remarks>
/// Named factories rather than constructor defaults on purpose: a composition root has to state
/// which value it is passing, so "nobody resolved this" stays visible at the call site and greps.
/// </remarks>
public static class LocalHostAmbience
{
    /// <summary>The entitlement of a player whose subscription nothing has resolved.</summary>
    /// <returns>No Plus, and no term to lapse.</returns>
    /// <remarks>
    /// The real value comes from the store-subscription port, which is deferred. <c>false</c> here is
    /// the absence of an entitlement rather than a guess at one, and a null expiry is the only
    /// honest instant for a term that was never granted.
    /// </remarks>
    public static Entitlements NoSubscriptionResolved() =>
        new(hasPlus: false, expiresAtUtc: null);

    /// <summary>The kill switches of a host no remote config has reached.</summary>
    /// <returns>Nothing killed.</returns>
    /// <remarks>
    /// The real value comes from the remote-config port, which is deferred. The switches are kill
    /// lists rather than allow lists, so "no config arrived" has exactly one non-inventing answer:
    /// nothing is killed. That is the identity element of the value, not a default chosen for it.
    /// </remarks>
    public static FeatureFlags NoRemoteConfigResolved() =>
        new(
            pvpEnabled: true,
            plusOfferEnabled: true,
            mailEnabled: true,
            disabledAdPlacements: [],
            disabledChapters: []);
}
