using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Server.Endpoints;

/// <summary>The command endpoints as plain functions: header + route + body in, status + body out.</summary>
/// <remarks>
/// Everything the two routes do happens here, against the principal seam and the gateway, with no
/// ASP.NET type in any signature — this repository's test tier constructs no host, so the HTTP
/// surface has to be this thin to be tested at all. The route lambdas in the composition adapt
/// <c>HttpContext</c> to these calls and nothing else.
/// </remarks>
public static class CommandRequestHandler
{
    /// <summary><c>POST /run/{runId}/command</c>.</summary>
    /// <param name="principals">The authenticated-player seam.</param>
    /// <param name="gateway">The command pipeline.</param>
    /// <param name="authorizationHeader">The request's <c>Authorization</c> header, or <c>null</c>.</param>
    /// <param name="runId">The route's <c>runId</c> segment.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException">A seam or the body is null.</exception>
    public static async Task<GatewayReply> HandleRunCommandAsync(
        IPrincipalResolver principals,
        CommandGateway gateway,
        string? authorizationHeader,
        string runId,
        string body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(body);

        // A segment RunId's own guard refuses (whitespace, an escaped empty) names no resource —
        // the ASP.NET default for an unroutable path, kept rather than promoted to a rejection:
        // RUN_NOT_FOUND is for a well-formed id with no run behind it.
        if (string.IsNullOrWhiteSpace(runId))
        {
            return new GatewayReply(404, string.Empty);
        }

        var principal = principals.Resolve(authorizationHeader);
        if (principal.Player is not { } player)
        {
            return new GatewayReply(principal.RefusalStatus!.Value, string.Empty);
        }

        return await gateway.SubmitRunCommandAsync(player, new RunId(runId), body, ct).ConfigureAwait(false);
    }

    /// <summary><c>POST /player/command</c>.</summary>
    /// <param name="principals">The authenticated-player seam.</param>
    /// <param name="gateway">The command pipeline.</param>
    /// <param name="authorizationHeader">The request's <c>Authorization</c> header, or <c>null</c>.</param>
    /// <param name="body">The request body.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException">A seam or the body is null.</exception>
    public static async Task<GatewayReply> HandlePlayerCommandAsync(
        IPrincipalResolver principals,
        CommandGateway gateway,
        string? authorizationHeader,
        string body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(body);

        var principal = principals.Resolve(authorizationHeader);
        if (principal.Player is not { } player)
        {
            return new GatewayReply(principal.RefusalStatus!.Value, string.Empty);
        }

        return await gateway.SubmitPlayerCommandAsync(player, body, ct).ConfigureAwait(false);
    }
}
