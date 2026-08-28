using SlayIdleRepeat.Server.Auth;

namespace SlayIdleRepeat.Server.Endpoints;

/// <summary>What an auth route writes back: the HTTP status and the exact body.</summary>
/// <param name="StatusCode">The status the route answers with.</param>
/// <param name="Body">The JSON body, or empty where the contract carries none.</param>
public sealed record AuthReply(int StatusCode, string Body);

/// <summary>The four auth endpoints as plain functions — collaborators in, status and body out.</summary>
/// <remarks>
/// No ASP.NET type appears in any signature, for the same reason the command handlers carry none:
/// this repository constructs no host in any test tier, so the whole HTTP surface has to be
/// callable directly or it is not covered at all. The route lambdas adapt <c>HttpContext</c> to
/// these calls and do nothing else.
/// </remarks>
public static class AuthRequestHandler
{
    /// <summary><c>POST /auth/device</c> — mints an anonymous account and its device credential.</summary>
    /// <param name="store">The auth rows.</param>
    /// <param name="names">The display-name filter.</param>
    /// <param name="nowUtc">The request instant — injected, never read ambiently.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    public static Task<AuthReply> HandleDeviceRegistrationAsync(
        IAuthStore store,
        IDisplayNamePolicy names,
        DateTimeOffset nowUtc,
        string body,
        CancellationToken ct) =>
        throw new NotImplementedException(
            "M5-06 Phase 3: decide the display name, mint player + device credential, store the " +
            "digest, and answer 200 with deviceId/deviceSecret/playerId/displayName — or 400 " +
            "carrying the refusal value alone.");

    /// <summary><c>POST /auth/session</c> — device-secret authentication opening a token family.</summary>
    /// <param name="store">The auth rows.</param>
    /// <param name="issuer">Mints the access token.</param>
    /// <param name="options">The lifetimes and the renewal fraction.</param>
    /// <param name="nowUtc">The request instant — injected, never read ambiently.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    public static Task<AuthReply> HandleSessionAsync(
        IAuthStore store,
        AccessTokenIssuer issuer,
        AuthOptions options,
        DateTimeOffset nowUtc,
        string body,
        CancellationToken ct) =>
        throw new NotImplementedException(
            "M5-06 Phase 3: an unknown device and a wrong secret answer the SAME 401 — a difference " +
            "between them is a device-enumeration oracle. A soft-deleted account answers 403.");

    /// <summary><c>POST /auth/refresh</c> — single-use rotation within a family.</summary>
    /// <param name="store">The auth rows.</param>
    /// <param name="issuer">Mints the access token.</param>
    /// <param name="options">The lifetimes and the renewal fraction.</param>
    /// <param name="nowUtc">The request instant — injected, never read ambiently.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    public static Task<AuthReply> HandleRefreshAsync(
        IAuthStore store,
        AccessTokenIssuer issuer,
        AuthOptions options,
        DateTimeOffset nowUtc,
        string body,
        CancellationToken ct) =>
        throw new NotImplementedException(
            "M5-06 Phase 3: a rotated-token presentation revokes the whole family FIRST, then " +
            "answers 401; the client's cure is device-secret re-authentication.");

    /// <summary><c>DELETE /account</c> — access token plus device-secret re-confirmation.</summary>
    /// <param name="store">The auth rows.</param>
    /// <param name="principals">The authenticated-player seam.</param>
    /// <param name="authorizationHeader">The request's <c>Authorization</c> header, or <c>null</c>.</param>
    /// <param name="nowUtc">The request instant — injected, never read ambiently.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// 🔒 A failed secret re-confirmation is <c>403</c>, not <c>401</c>: a 401 is the status the
    /// client cures by refreshing and retrying the SAME request, which would resubmit the same wrong
    /// secret forever. It is an account-state answer, not a token answer.
    /// </remarks>
    public static Task<AuthReply> HandleAccountDeletionAsync(
        IAuthStore store,
        IPrincipalResolver principals,
        string? authorizationHeader,
        DateTimeOffset nowUtc,
        string body,
        CancellationToken ct) =>
        throw new NotImplementedException(
            "M5-06 Phase 3: soft-delete immediately, revoke every family, and answer the hard-delete " +
            "due date thirty days out; a wrong device secret changes nothing at all.");
}
