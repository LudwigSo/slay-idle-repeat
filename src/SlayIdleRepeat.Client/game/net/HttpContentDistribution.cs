namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// The content-distribution seam over the server's two content routes.
/// </summary>
/// <remarks>
/// <para>
/// The handler is injected rather than an <see cref="HttpClient"/>, so every route, method and
/// status this decides on is observable without a server, a socket or a container.
/// </para>
/// <para>
/// 🔒 <b>The pointer document's <c>bundleUrl</c> is read and deliberately ignored.</b>
/// <see cref="FetchBundleAsync"/> carries a stamp and no URL, so it structurally cannot follow one:
/// a pointer naming another host could not redirect this client.
/// </para>
/// <para>
/// 🔒 <b>The stamp is returned verbatim and unvalidated.</b> The seam's contract is that it is "not
/// yet known to be well-formed", because the presenter above turns a malformed stamp into a named
/// failure a player is shown.
/// </para>
/// <para>
/// ⚠️ <b>Automatic decompression stays off.</b> gzip here is the media type, not a transfer
/// encoding; a handler that unpacked a server which also set a gzip content encoding would hand the
/// verifier unpacked bytes and produce a spurious rejection.
/// </para>
/// </remarks>
public sealed class HttpContentDistribution : IContentDistributionClient, IDisposable
{
    /// <summary>Builds the seam over its deployment configuration and the transport it talks through.</summary>
    /// <param name="baseAddress">Where the server is. Both routes are resolved relative to it.</param>
    /// <param name="requestTimeout">How long one request may take.</param>
    /// <param name="handler">The transport. Owned by the caller.</param>
    /// <exception cref="ArgumentNullException"><paramref name="baseAddress"/> or <paramref name="handler"/> is null.</exception>
    public HttpContentDistribution(Uri baseAddress, TimeSpan requestTimeout, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(handler);
    }

    /// <inheritdoc/>
    /// <exception cref="ContentDistributionException">The pointer could not be read.</exception>
    public Task<string> FetchCurrentVersionAsync(CancellationToken ct) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    /// <exception cref="ContentDistributionException">The bundle could not be fetched.</exception>
    public Task<ReadOnlyMemory<byte>> FetchBundleAsync(string version, CancellationToken ct) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}
