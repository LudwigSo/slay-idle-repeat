namespace SlayIdleRepeat.Application.Auth;

/// <summary>When an access token should be renewed in the background, and whether that moment has come.</summary>
/// <param name="RenewAtUtc">The instant the client starts renewing at.</param>
/// <param name="RenewalIsDue">Whether the instant handed to the policy is at or past <paramref name="RenewAtUtc"/>.</param>
public readonly record struct SilentRenewalDecision(DateTimeOffset RenewAtUtc, bool RenewalIsDue);

/// <summary>The background-renewal decision, as a function of four values and nothing else.</summary>
/// <remarks>
/// No clock, no state, no ambient configuration: the fraction is server-operations configuration that
/// arrives with the session, and the instants arrive with the token. A policy that read the clock
/// itself could not be asked what it would decide at any moment other than this one.
/// </remarks>
public static class SilentRenewalPolicy
{
    /// <summary>Decides when to renew, and whether it is time.</summary>
    /// <param name="issuedAtUtc">The instant the access token was issued at.</param>
    /// <param name="expiresAtUtc">The instant it stops being accepted.</param>
    /// <param name="renewalFraction">
    /// How far through the lifetime renewal starts, in <c>(0, 1]</c>. Outside that it is refused
    /// rather than clamped — a clamp turns a misconfigured deployment into a silently different one.
    /// </param>
    /// <param name="nowUtc">The instant the question is asked at.</param>
    /// <returns>The renewal instant and whether it has arrived.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="renewalFraction"/> is outside <c>(0, 1]</c>.</exception>
    public static SilentRenewalDecision Decide(
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc,
        double renewalFraction,
        DateTimeOffset nowUtc) =>
        throw new NotImplementedException(
            "SilentRenewalPolicy.Decide has no body yet. It answers the instant that lies " +
            "renewalFraction of the way from issue to expiry, and whether nowUtc has reached it.");
}
