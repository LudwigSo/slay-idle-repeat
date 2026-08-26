using SlayIdleRepeat.Application.Services.Analytics;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>The command endpoints' functional area: composes the pipeline over the shared backbone and maps the two routes.</summary>
/// <remarks>
/// Composition-root code: the shared state comes from <see cref="GameBackbone"/>, never newed
/// here; what this area adds is its own — the gateway, and two placeholders each greppable by
/// name: <see cref="PlaceholderBearerPlayerIdResolver"/> (M5-06's auth) and
/// <see cref="UnlimitedCommandThrottle"/> (M5-14's limiter). <c>CONTENT_VERSION_MISMATCH</c> has
/// no arm anywhere yet — the content pinning it checks against is M5-09's.
/// </remarks>
public static class GameCommandComposition
{
    private static readonly object GatewayGate = new();
    private static CommandGateway? _gateway;

    /// <summary>Maps <c>POST /run/{runId}/command</c> and <c>POST /player/command</c>.</summary>
    /// <param name="app">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    public static WebApplication MapGameCommandEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        IPrincipalResolver principals = new PlaceholderBearerPlayerIdResolver();

        // The server's only tracing: no ASP.NET auto-instrumentation package is pinned, so a
        // command that is not wrapped here appears on no trace at all.
        var telemetry = ObservabilityComposition.Telemetry(app.Configuration);

        app.MapPost(
            "/run/{runId}/command",
            async (HttpContext http, string runId, CancellationToken ct) =>
            {
                using var span = telemetry.BeginSpan("run_command");
                var reply = await CommandRequestHandler.HandleRunCommandAsync(
                    principals, Gateway(app), http.Request.Headers.Authorization, runId,
                    await ReadBodyAsync(http, ct), ct);

                await WriteAsync(http, reply, ct);
            });

        app.MapPost(
            "/player/command",
            async (HttpContext http, CancellationToken ct) =>
            {
                using var span = telemetry.BeginSpan("player_command");
                var reply = await CommandRequestHandler.HandlePlayerCommandAsync(
                    principals, Gateway(app), http.Request.Headers.Authorization,
                    await ReadBodyAsync(http, ct), ct);

                await WriteAsync(http, reply, ct);
            });

        return app;
    }

    /// <summary>The area's gateway over the shared backbone — built on first command, a failed build retried on the backbone's own rule.</summary>
    private static CommandGateway Gateway(WebApplication app)
    {
        if (Volatile.Read(ref _gateway) is { } built)
        {
            return built;
        }

        lock (GatewayGate)
        {
            if (_gateway is not null)
            {
                return _gateway;
            }

            var backbone = GameBackbone.Shared(app.Configuration, app.Environment);

            // Post-commit fan-out: analytics and the domain-events counter, over the SAME composed
            // ports the observability area flushes and exports — never a second, private instance.
            var dispatcher = new DomainEventDispatcher(
            [
                new AnalyticsEventSink(ObservabilityComposition.AnalyticsSink(app.Configuration)),
                new TelemetryEventSink(ObservabilityComposition.Telemetry(app.Configuration)),
            ]);

            return _gateway = new CommandGateway(
                new ApplyCommandUseCase(backbone.WorldStore, dispatcher),
                backbone.Clock,
                backbone.Ids,
                backbone.Content,
                backbone.Entitlements,
                backbone.Flags,
                backbone.Ledger,
                new UnlimitedCommandThrottle());
        }
    }

    private static async Task<string> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        return await reader.ReadToEndAsync(ct);
    }

    private static async Task WriteAsync(HttpContext http, GatewayReply reply, CancellationToken ct)
    {
        http.Response.StatusCode = reply.StatusCode;

        if (reply.Body.Length > 0)
        {
            http.Response.ContentType = "application/json; charset=utf-8";
            await http.Response.WriteAsync(reply.Body, ct);
        }
    }
}
