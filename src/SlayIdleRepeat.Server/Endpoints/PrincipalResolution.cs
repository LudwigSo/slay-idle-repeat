using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Server.Endpoints;

/// <summary>What resolving a request's caller produced.</summary>
/// <remarks>
/// The vocabulary is 14 §16.2's HTTP mapping for auth, nothing more: a resolved player proceeds,
/// <see cref="Unauthorized"/> is the 401 a client cures with a token refresh and the SAME
/// <c>commandId</c>, <see cref="Locked"/> is the 403 account-state screen. Exactly one shape is
/// populated; the factories are the only builders.
/// </remarks>
public sealed record PrincipalResolution
{
    private PrincipalResolution(PlayerId? player, int? refusalStatus)
    {
        Player = player;
        RefusalStatus = refusalStatus;
    }

    /// <summary>The authenticated player, or <c>null</c> on a refusal.</summary>
    public PlayerId? Player { get; }

    /// <summary><c>401</c> or <c>403</c>, or <c>null</c> when a player resolved.</summary>
    public int? RefusalStatus { get; }

    /// <summary>The caller is this player.</summary>
    public static PrincipalResolution Resolved(PlayerId player) => new(player, refusalStatus: null);

    /// <summary>Missing, expired or invalid credentials — HTTP 401.</summary>
    public static PrincipalResolution Unauthorized() => new(player: null, 401);

    /// <summary>The account is locked or sanctioned — HTTP 403.</summary>
    public static PrincipalResolution Locked() => new(player: null, 403);
}

/// <summary>The authenticated-player seam in front of the command endpoints.</summary>
/// <remarks>
/// M5-06 replaces the placeholder behind this with the real JWT validation; the endpoints never
/// learn which. It takes the raw <c>Authorization</c> header value rather than an HTTP context so
/// the whole request path stays testable without constructing one.
/// </remarks>
public interface IPrincipalResolver
{
    /// <summary>Resolves the request's caller from its <c>Authorization</c> header.</summary>
    /// <param name="authorizationHeader">The header value as sent, or <c>null</c> when absent.</param>
    PrincipalResolution Resolve(string? authorizationHeader);
}
