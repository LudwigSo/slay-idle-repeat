using SlayIdleRepeat.Adapters.Ambient.System;
using SlayIdleRepeat.Adapters.Content.LocalFile;
using SlayIdleRepeat.Application.Hosting;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>The command endpoints' functional area: composes the pipeline and maps the two routes.</summary>
/// <remarks>
/// <para>
/// This is composition-root code, so concrete adapter types are named here and nowhere else. What
/// it names today, with its expiry: the real ambient adapters (<see cref="SystemClock"/>,
/// <see cref="SystemIdGenerator"/>) and the local-file content source, which stay; and four
/// placeholders, each greppable by name — <see cref="PlaceholderVolatileWorldStore"/> and
/// <see cref="VolatileCommandLedger"/> (M5-05's stores), <see cref="PlaceholderBearerPlayerIdResolver"/>
/// (M5-06's auth), <see cref="UnlimitedCommandThrottle"/> (M5-14's limiter) — plus
/// <see cref="LocalHostAmbience"/>'s two named absences (M5-06 resolves the entitlement per player,
/// M5-10 the flags from remote config).
/// </para>
/// <para>
/// The gateway is built lazily, on the first command: it needs the content set, which lives at
/// <c>GameData:Root</c> (default <c>game-data</c> under the content root), and a deployment without
/// one — today's container image — must still boot and answer <c>GET /health</c>. The first command
/// on such a host faults loudly instead.
/// </para>
/// </remarks>
public static class GameCommandComposition
{
    /// <summary>Maps <c>POST /run/{runId}/command</c> and <c>POST /player/command</c>.</summary>
    /// <param name="app">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    public static WebApplication MapGameCommandEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var gateway = new Lazy<CommandGateway>(
            () => BuildGateway(app.Configuration, app.Environment),
            LazyThreadSafetyMode.ExecutionAndPublication);

        IPrincipalResolver principals = new PlaceholderBearerPlayerIdResolver();

        app.MapPost(
            "/run/{runId}/command",
            async (HttpContext http, string runId, CancellationToken ct) =>
            {
                var reply = await CommandRequestHandler.HandleRunCommandAsync(
                    principals, gateway.Value, http.Request.Headers.Authorization, runId,
                    await ReadBodyAsync(http, ct), ct);

                await WriteAsync(http, reply, ct);
            });

        app.MapPost(
            "/player/command",
            async (HttpContext http, CancellationToken ct) =>
            {
                var reply = await CommandRequestHandler.HandlePlayerCommandAsync(
                    principals, gateway.Value, http.Request.Headers.Authorization,
                    await ReadBodyAsync(http, ct), ct);

                await WriteAsync(http, reply, ct);
            });

        return app;
    }

    private static CommandGateway BuildGateway(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredRoot = configuration["GameData:Root"] ?? "game-data";
        var dataRoot = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);

        var content = ContentLoader.Load(new LocalFileContentSource(dataRoot)).Require();

        var store = new WorldSliceStore(new PlaceholderVolatileWorldStore());

        return new CommandGateway(
            new ApplyCommandUseCase(store, new DomainEventDispatcher([])),
            new SystemClock(),
            new SystemIdGenerator(),
            content,
            LocalHostAmbience.NoSubscriptionResolved(),
            LocalHostAmbience.NoRemoteConfigResolved(),
            new VolatileCommandLedger(),
            new UnlimitedCommandThrottle());
    }

    private static async Task<string> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        return await reader.ReadToEndAsync(ct);
    }

    private static async Task WriteAsync(HttpContext http, Application.Wire.GatewayReply reply, CancellationToken ct)
    {
        http.Response.StatusCode = reply.StatusCode;

        if (reply.Body.Length > 0)
        {
            http.Response.ContentType = "application/json; charset=utf-8";
            await http.Response.WriteAsync(reply.Body, ct);
        }
    }
}
