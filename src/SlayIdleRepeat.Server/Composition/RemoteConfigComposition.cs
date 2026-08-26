using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>The remote-config functional area: maps <c>GET /config</c> over the shared backbone's reloading source.</summary>
/// <remarks>
/// Composition-root code on the command area's regime: the backbone — and with it the source and
/// its reload loop — is built on the FIRST request, never at map time, so a host without game-data
/// still boots and answers <c>/health</c>. No auth: the flags document is broadcast config, the
/// same bytes for every client, so the principal seam is not consulted.
/// </remarks>
public static class RemoteConfigComposition
{
    /// <summary>Maps <c>GET /config</c>.</summary>
    /// <param name="app">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    public static WebApplication MapRemoteConfigEndpoint(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
            "/config",
            async (HttpContext http, CancellationToken ct) =>
            {
                var backbone = GameBackbone.Shared(app.Configuration, app.Environment);
                var reply = RemoteConfigRequestHandler.Handle(backbone.RemoteConfig.Document);

                http.Response.StatusCode = reply.StatusCode;
                http.Response.ContentType = reply.ContentType;
                http.Response.Headers.CacheControl = reply.CacheControl;
                await http.Response.WriteAsync(reply.Body, ct);
            });

        return app;
    }
}
