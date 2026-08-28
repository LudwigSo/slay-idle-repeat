using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Auth;

/// <summary>Whether an account may hold a session at all — the 403 arm, read per request.</summary>
/// <remarks>
/// Synchronous because the resolver seam in front of the command endpoints is, and that seam is
/// M5-03's contract rather than this task's to widen. The implementation behind it answers from a
/// cached view rather than a query on the request path.
/// </remarks>
public interface IAccountStatusReader
{
    /// <summary>Whether this account is soft-deleted, locked or sanctioned.</summary>
    bool IsLocked(PlayerId player);
}

/// <summary>
/// The real principal seam: <c>Authorization: Bearer &lt;jwt&gt;</c> in, the token's <c>sub</c> out.
/// </summary>
/// <remarks>
/// ⚠️ <b>WebSocket upgrade auth, for whichever milestone builds a push channel.</b> The upgrade
/// carries the same <c>Authorization: Bearer</c> header and is authenticated once, here, at the
/// handshake; when the server needs fresh credentials it closes the socket with code <c>4401</c> and
/// the client silently reconnects after a renewal. No hub exists yet and none is to be built for
/// this; the note is here so the design is not re-derived.
/// </remarks>
public sealed class JwtPrincipalResolver : IPrincipalResolver
{
    private readonly AccessTokenIssuer _issuer;
    private readonly IAccountStatusReader _accounts;
    private readonly IClockPort _clock;

    /// <summary>Builds the resolver over the token issuer, the account-state reader and the clock.</summary>
    /// <param name="issuer">Mints and validates this service's tokens.</param>
    /// <param name="accounts">Answers whether the resolved account may hold a session.</param>
    /// <param name="clock">The instant <c>exp</c> is measured against. Injected, never ambient.</param>
    public JwtPrincipalResolver(AccessTokenIssuer issuer, IAccountStatusReader accounts, IClockPort clock)
    {
        _issuer = issuer;
        _accounts = accounts;
        _clock = clock;
    }

    /// <summary>The issuer this resolver validates against.</summary>
    internal AccessTokenIssuer TokenIssuer => _issuer;

    /// <summary>The account-state reader behind the 403 arm.</summary>
    internal IAccountStatusReader Accounts => _accounts;

    /// <summary>The clock the expiry check reads.</summary>
    internal IClockPort Clock => _clock;

    /// <inheritdoc/>
    public PrincipalResolution Resolve(string? authorizationHeader)
    {
        if (authorizationHeader is null ||
            !authorizationHeader.StartsWith(BearerPrefix, StringComparison.Ordinal))
        {
            return PrincipalResolution.Unauthorized();
        }

        var presented = authorizationHeader[BearerPrefix.Length..];

        // Every way a token can be wrong is one status: which rule fired is a diagnosis for this
        // server's logs, not something a caller may probe for one refusal at a time.
        if (_issuer.Validate(presented, _clock.UtcNow).Claims is not { } claims)
        {
            return PrincipalResolution.Unauthorized();
        }

        return _accounts.IsLocked(claims.Player)
            ? PrincipalResolution.Locked()
            : PrincipalResolution.Resolved(claims.Player);
    }

    private const string BearerPrefix = "Bearer ";
}
