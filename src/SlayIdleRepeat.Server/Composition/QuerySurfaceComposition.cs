using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>The query surface's functional area: composes the run-state read over the shared backbone and maps its route.</summary>
/// <remarks>
/// <para>
/// Composition-root code: the rows and the ledger come from <see cref="GameBackbone"/>, never newed
/// here. This read must answer from the very rows the command endpoints write, and a second store
/// would be a disjoint world where a run that was just POSTed is invisible to the GET that follows
/// it, with nothing going red.
/// </para>
/// <para>
/// 🔒 <b>This read is served from the primary, always.</b> It is a write-model read, not a read
/// model: a client reconnecting immediately after a command must see that command's own result, and
/// a replica would answer with state older than the thing being reconnected after. The query states
/// that itself — <c>RunStateQuery.Routing</c> is the primary and is written down rather than left
/// to a default — and this deployment has one database and no replica to route to in any case.
/// </para>
/// <para>
/// The principal seam is the command area's placeholder (M5-06's real one replaces both together).
/// </para>
/// </remarks>
public static class QuerySurfaceComposition
{
    /// <summary>What every answer from this area is marked with, refusals included.</summary>
    private const string NeverCached = "no-store";

    private static readonly object QueryGate = new();
    private static RunStateQuery? _query;

    /// <summary>Maps <c>GET /run/{runId}/state</c>.</summary>
    /// <param name="app">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    public static WebApplication MapQuerySurfaceEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        IPrincipalResolver principals = AuthComposition.Principals(app);

        // The server's only tracing: no ASP.NET auto-instrumentation package is pinned, so a request
        // that is not wrapped here appears on no trace at all.
        var telemetry = ObservabilityComposition.Telemetry(app.Configuration);

        app.MapGet(
            "/run/{runId}/state",
            async (HttpContext http, string runId, CancellationToken ct) =>
            {
                using var span = telemetry.BeginSpan("run_state");
                var reply = await StateRequestHandler.HandleRunStateAsync(
                    principals, Query(app), http.Request.Headers.Authorization, runId,
                    http.Request.Query["sinceSequence"], ct);

                await WriteAsync(http, reply, ct);
            });

        return app;
    }

    /// <summary>The area's read over the shared backbone — built on first request, a failed build retried on the backbone's own rule.</summary>
    private static RunStateQuery Query(WebApplication app)
    {
        if (Volatile.Read(ref _query) is { } built)
        {
            return built;
        }

        lock (QueryGate)
        {
            if (_query is not null)
            {
                return _query;
            }

            var backbone = GameBackbone.Shared(app.Configuration, app.Environment);

            return _query = new RunStateQuery(new ReadOwnStateUseCase(backbone.WorldStore), backbone.Ledger);
        }
    }

    // The one header this area must send, which is why it is passed here rather than defaulted in
    // the shared writer. A GET is cacheable by default to every proxy, browser and CDN between here
    // and the client, and a cached copy of this answer is precisely the stale own-state read the
    // whole design refuses: the client reconnects, is handed the state it had before the command it
    // is reconnecting after, and its next command is a sequence gap. Cross-player read models are
    // the reads that may be cached; this is not one.
    private static Task WriteAsync(HttpContext http, GatewayReply reply, CancellationToken ct) =>
        HttpReplies.WriteAsync(http, reply.StatusCode, reply.Body, ct, NeverCached);
}
