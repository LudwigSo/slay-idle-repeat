namespace SlayIdleRepeat.Core;

/// <summary>The player's subscription entitlement, as a read-only value the composition root resolved.</summary>
/// <remarks>
/// <para>
/// The domain may read <see cref="HasPlus"/> only to resolve the ad-reward auto-grant cap, never
/// to alter a stat, a rate or a drop — enforced by an architecture rule that makes this type
/// unreachable from rules and power computation except for that one licensed exception.
/// </para>
/// <para>
/// Deliberately not a <c>record</c>: a synthesized <c>Equals</c> would read the flag and branch on
/// it, exactly the shape the architecture rule above forbids elsewhere in the domain.
/// </para>
/// </remarks>
public sealed class Entitlements
{
    /// <summary>Creates the entitlement value the composition root resolved for this command.</summary>
    /// <param name="hasPlus">Whether Plus is active, as the server decided.</param>
    /// <param name="expiresAtUtc">When Plus lapses, or <c>null</c> when there is nothing to lapse.</param>
    public Entitlements(bool hasPlus, DateTimeOffset? expiresAtUtc)
    {
        HasPlus = hasPlus;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Whether Plus is active. Readable only by the ad-reward auto-grant cap rule.</summary>
    public bool HasPlus { get; }

    /// <summary>When the current Plus term lapses, or <c>null</c> when none is known.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; }
}
