using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Server.Auth;

/// <summary>The auth rows the endpoints read and write.</summary>
/// <remarks>
/// <para>
/// 🔒 Declared HERE, in the composition root, and deliberately not under the application's port
/// folder: there is one real implementation and it is a wrapper this project owns, so promoting it
/// to a port would owe a shared contract suite and two fixtures for a seam with a single reader. If
/// a second real implementation ever appears, that answer changes and this becomes a port.
/// </para>
/// <para>
/// Every member takes and returns digests, never a secret or a raw token: what this seam can be
/// handed is the strongest guarantee available that nothing writes one down.
/// </para>
/// </remarks>
public interface IAuthStore
{
    /// <summary>Creates the anonymous account and its first device row in one step.</summary>
    Task CreateAnonymousAccountAsync(AuthDevice device, string displayName, CancellationToken ct);

    /// <summary>The device row, or <c>null</c> when no such device exists.</summary>
    Task<AuthDevice?> FindDeviceAsync(string deviceId, CancellationToken ct);

    /// <summary>The account's device row, for the deletion endpoint's secret re-confirmation.</summary>
    Task<AuthDevice?> FindDeviceForPlayerAsync(PlayerId player, CancellationToken ct);

    /// <summary>Whether the account is soft-deleted — the 403 arm on every auth route.</summary>
    Task<bool> IsAccountDeletedAsync(PlayerId player, CancellationToken ct);

    /// <summary>Opens a family with its first token and restamps the device's last-seen instant.</summary>
    Task OpenTokenFamilyAsync(
        AuthTokenFamily family,
        AuthRefreshToken token,
        DateTimeOffset lastSeenAtUtc,
        CancellationToken ct);

    /// <summary>The family carrying this token digest, with its tokens. Empty when nothing matches.</summary>
    Task<AuthTokenState> LoadTokenStateForDigestAsync(byte[] tokenDigest, CancellationToken ct);

    /// <summary>Applies a rotation decision: the rotation stamp, the replacement, the revocations.</summary>
    Task ApplyRotationAsync(RefreshRotationDecision decision, DateTimeOffset nowUtc, CancellationToken ct);

    /// <summary>Soft-deletes the account and revokes every one of its families.</summary>
    Task SoftDeleteAccountAsync(
        PlayerId player,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset hardDeleteDueAtUtc,
        CancellationToken ct);
}
