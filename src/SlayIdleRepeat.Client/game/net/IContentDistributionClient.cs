namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>The two content-distribution calls the client makes, as the client sees them.</summary>
/// <remarks>
/// <para>
/// A plain interface in the client project, deliberately NOT an application port. Nothing in
/// <c>SlayIdleRepeat.Application</c> fetches content over a network — the server loads it off its
/// own disk — so a port would declare a dependency the application does not have. This is the
/// client's own seam, and it exists so <c>ContentSyncPresenter</c> can be driven under a test
/// runner with no HTTP anywhere near it.
/// </para>
/// <para>
/// Both members speak in the transport's own terms: a version arrives as text because it arrived
/// from a server, and bytes arrive as bytes. Turning either into a typed value is a decision that
/// belongs above this seam, where a malformed answer can become a named failure a player is shown.
/// </para>
/// </remarks>
public interface IContentDistributionClient
{
    /// <summary>Reads the pointer document and returns the stamp it names, verbatim.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The server's current content stamp as text — not yet known to be well-formed.</returns>
    Task<string> FetchCurrentVersionAsync(CancellationToken ct);

    /// <summary>Downloads one bundle by stamp.</summary>
    /// <param name="version">The stamp to fetch.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The gzip bundle, exactly as served.</returns>
    Task<ReadOnlyMemory<byte>> FetchBundleAsync(string version, CancellationToken ct);
}
