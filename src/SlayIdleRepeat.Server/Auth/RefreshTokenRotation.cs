namespace SlayIdleRepeat.Server.Auth;

/// <summary>Which rule refused a refresh presentation. One member per rule.</summary>
public enum RefreshRefusal
{
    /// <summary>No stored token carries this digest.</summary>
    UNKNOWN_TOKEN = 1,

    /// <summary>The token was already rotated — a reuse, and the family burns with it.</summary>
    ALREADY_ROTATED = 2,

    /// <summary>The token's family is already closed.</summary>
    FAMILY_REVOKED = 3,

    /// <summary>The token is past its own expiry.</summary>
    EXPIRED = 4,
}

/// <summary>What one refresh presentation decides. Digests only — a raw token never enters here.</summary>
/// <param name="Refusal">Which rule fired, or <c>null</c> when the rotation stands.</param>
/// <param name="FamilyId">The family the presented digest belongs to, when one was matched.</param>
/// <param name="IssuedToken">The replacement token, in the SAME family, on an accepted rotation.</param>
/// <param name="RotatedTokenDigest">The presented digest, to be marked rotated.</param>
/// <param name="RevokedTokenDigests">Every token digest the family revocation covers — live ones included.</param>
/// <param name="FamilyRevocation">Why the family is being closed, when it is.</param>
public sealed record RefreshRotationDecision(
    RefreshRefusal? Refusal,
    string? FamilyId,
    AuthRefreshToken? IssuedToken,
    byte[]? RotatedTokenDigest,
    IReadOnlyList<byte[]> RevokedTokenDigests,
    TokenFamilyRevocation? FamilyRevocation)
{
    /// <summary>Whether the presentation rotated.</summary>
    public bool Accepted => Refusal is null;
}

/// <summary>
/// The single-use rotating refresh rule, as a pure decision over stored rows. No store, no clock,
/// no I/O: the caller loads the rows, applies the decision and mints the raw token it never sees.
/// </summary>
public static class RefreshTokenRotation
{
    /// <summary>Decides what one presented refresh digest does to its family.</summary>
    /// <param name="state">The families and tokens in scope.</param>
    /// <param name="presentedDigest">SHA-256 of the token the client sent.</param>
    /// <param name="replacementDigest">SHA-256 of the token the caller has minted to hand back.</param>
    /// <param name="refreshTokenLifetime">The configured family lifetime, measured from the family's creation.</param>
    /// <param name="nowUtc">The decision instant — injected, never read ambiently.</param>
    public static RefreshRotationDecision Decide(
        AuthTokenState state,
        byte[] presentedDigest,
        byte[] replacementDigest,
        TimeSpan refreshTokenLifetime,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(state);

        var presented = state.Tokens.FirstOrDefault(
            token => DeviceCredentialFactory.DigestsMatch(token.TokenDigest, presentedDigest));

        // Matching on the DIGEST and nothing else is what keeps several live families — several
        // devices — from rotating each other out.
        if (presented is null ||
            state.Families.FirstOrDefault(family => family.FamilyId == presented.FamilyId) is not { } family)
        {
            return Refused(RefreshRefusal.UNKNOWN_TOKEN, familyId: null);
        }

        // Ahead of the reuse rule: a closed family must keep the reason it was closed for.
        if (family.RevokedAtUtc is not null)
        {
            return Refused(RefreshRefusal.FAMILY_REVOKED, family.FamilyId);
        }

        if (presented.RotatedAtUtc is not null)
        {
            return new RefreshRotationDecision(
                RefreshRefusal.ALREADY_ROTATED,
                family.FamilyId,
                IssuedToken: null,
                RotatedTokenDigest: null,
                state.Tokens
                    .Where(token => token.FamilyId == family.FamilyId)
                    .Select(token => token.TokenDigest)
                    .ToArray(),
                TokenFamilyRevocation.REFRESH_REUSE);
        }

        if (presented.ExpiresAtUtc <= nowUtc)
        {
            return Refused(RefreshRefusal.EXPIRED, family.FamilyId);
        }

        // The horizon belongs to the family, not to the token: measured from the device-secret
        // authentication that opened it, so refreshing can never make a family immortal.
        var horizon = family.CreatedAtUtc.Add(refreshTokenLifetime);
        var expiresAt = horizon < nowUtc.Add(refreshTokenLifetime) ? horizon : nowUtc.Add(refreshTokenLifetime);

        return new RefreshRotationDecision(
            Refusal: null,
            family.FamilyId,
            new AuthRefreshToken(
                replacementDigest,
                family.FamilyId,
                presented.DeviceId,
                presented.Player,
                nowUtc,
                expiresAt,
                RotatedAtUtc: null),
            presentedDigest,
            Array.Empty<byte[]>(),
            FamilyRevocation: null);
    }

    private static RefreshRotationDecision Refused(RefreshRefusal refusal, string? familyId) =>
        new(refusal, familyId, IssuedToken: null, RotatedTokenDigest: null,
            Array.Empty<byte[]>(), FamilyRevocation: null);
}
