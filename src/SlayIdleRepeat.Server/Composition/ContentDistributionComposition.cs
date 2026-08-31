using SlayIdleRepeat.Server.Endpoints;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The content-distribution area: maps <c>GET /content/current</c> and <c>GET /content/{version}</c>
/// over the shared backbone's bundle shelf.
/// </summary>
/// <remarks>
/// <para>
/// Composition-root code on the command area's regime: the backbone — and with it the loaded
/// content set and the shelf — is built on the FIRST request, never at map time, so a host without
/// game-data still boots and answers <c>/health</c>.
/// </para>
/// <para>
/// No auth. The content set is the same bytes for every client and is what a client needs BEFORE it
/// can begin a session at all; putting the principal seam in front of it would make fetching
/// content require a session and beginning a session require content.
/// </para>
/// </remarks>
public static class ContentDistributionComposition
{
    /// <summary>Maps the pointer and the bundle routes.</summary>
    /// <param name="app">The host being composed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    public static WebApplication MapContentDistributionEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
            "/content/current",
            async (HttpContext http, CancellationToken ct) =>
            {
                var backbone = GameBackbone.Shared(app.Configuration, app.Environment);
                var reply = ContentDistributionRequestHandler.Current(backbone.Content.Version);

                http.Response.StatusCode = reply.StatusCode;
                http.Response.ContentType = reply.ContentType;
                http.Response.Headers.CacheControl = reply.CacheControl;
                await http.Response.WriteAsync(reply.Body, ct);
            });

        app.MapGet(
            "/content/{version}",
            async (HttpContext http, string version, CancellationToken ct) =>
            {
                var backbone = GameBackbone.Shared(app.Configuration, app.Environment);
                var reply = ContentDistributionRequestHandler.Bundle(version, backbone.Bundles.TryRead);

                http.Response.StatusCode = reply.StatusCode;

                if (reply.Body.Length > 0)
                {
                    http.Response.ContentType = reply.ContentType;
                    http.Response.Headers.CacheControl = reply.CacheControl;
                    await http.Response.Body.WriteAsync(reply.Body, ct);
                }
            });

        return app;
    }
}
