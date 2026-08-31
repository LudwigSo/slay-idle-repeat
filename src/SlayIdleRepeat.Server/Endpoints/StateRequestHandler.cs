using System.Globalization;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Server.Endpoints;

/// <summary>The query surface as a plain function: header + route + query string in, status + body out.</summary>
/// <remarks>
/// The command handler's regime, for the same reason: no ASP.NET type in any signature, so the HTTP
/// surface is testable without a host. Every refusal is decided before the query is consulted — a
/// 401 that had already loaded rows would touch the primary once per unauthenticated request, which
/// is the shape a probe floods it with.
/// </remarks>
public static class StateRequestHandler
{
    /// <summary><c>GET /run/{runId}/state?sinceSequence=N</c>.</summary>
    /// <param name="principals">The authenticated-player seam.</param>
    /// <param name="query">The write-model read.</param>
    /// <param name="authorizationHeader">The request's <c>Authorization</c> header, or <c>null</c>.</param>
    /// <param name="runId">The route's <c>runId</c> segment.</param>
    /// <param name="sinceSequence">The <c>sinceSequence</c> query-string value, or <c>null</c> when it was not sent.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException">A seam is null.</exception>
    public static async Task<GatewayReply> HandleRunStateAsync(
        IPrincipalResolver principals,
        RunStateQuery query,
        string? authorizationHeader,
        string runId,
        string? sinceSequence,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principals);
        ArgumentNullException.ThrowIfNull(query);

        // A segment RunId's own guard refuses names no resource — the same reading the command
        // endpoint takes of an unroutable path.
        if (string.IsNullOrWhiteSpace(runId))
        {
            return new GatewayReply(404, string.Empty);
        }

        var principal = principals.Resolve(authorizationHeader);
        if (principal.Player is not { } player)
        {
            return new GatewayReply(principal.RefusalStatus!.Value, string.Empty);
        }

        // Required, and read strictly: no sign, no separators, no decimal point. A value defaulted to
        // 0 or truncated to a whole number would answer a question the client did not ask.
        if (!long.TryParse(sinceSequence, NumberStyles.None, CultureInfo.InvariantCulture, out var since))
        {
            return new GatewayReply(400, string.Empty);
        }

        return await query.ReadAsync(player, new RunId(runId), since, ct).ConfigureAwait(false);
    }
}
