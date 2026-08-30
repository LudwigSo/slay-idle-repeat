using System.Text.Json;

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
    /// <summary>M5-09's pointer route, relative to the configured base address.</summary>
    private const string PointerRoute = "content/current";

    /// <summary>M5-09's bundle route, which is the stamp itself.</summary>
    private const string BundleRoutePrefix = "content/";

    /// <summary>The pointer document's own spelling of the stamp it names.</summary>
    private const string VersionMember = "contentVersion";

    private readonly HttpClient _client;

    /// <summary>Builds the seam over its deployment configuration and the transport it talks through.</summary>
    /// <param name="baseAddress">Where the server is. Both routes are resolved relative to it.</param>
    /// <param name="requestTimeout">How long one request may take.</param>
    /// <param name="handler">The transport. Owned by the caller.</param>
    /// <exception cref="ArgumentNullException"><paramref name="baseAddress"/> or <paramref name="handler"/> is null.</exception>
    public HttpContentDistribution(Uri baseAddress, TimeSpan requestTimeout, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentNullException.ThrowIfNull(handler);

        _client = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = baseAddress,
            Timeout = requestTimeout,
        };
    }

    /// <inheritdoc/>
    /// <exception cref="ContentDistributionException">The pointer could not be read.</exception>
    public async Task<string> FetchCurrentVersionAsync(CancellationToken ct)
    {
        using var response = await GetAsync(PointerRoute, ct).ConfigureAwait(false);
        var body = await ReadTextAsync(response, PointerRoute, ct).ConfigureAwait(false);

        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.TryGetProperty(VersionMember, out var named) &&
                   named.ValueKind == JsonValueKind.String
                ? named.GetString()!
                : throw new ContentDistributionException(
                    $"the pointer document carries no '{VersionMember}' string, so there is no " +
                    "content set for it to be naming.");
        }
        catch (JsonException fault)
        {
            throw new ContentDistributionException("the pointer document is not JSON.", fault);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ContentDistributionException">The bundle could not be fetched.</exception>
    public async Task<ReadOnlyMemory<byte>> FetchBundleAsync(string version, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var route = BundleRoutePrefix + version;

        using var response = await GetAsync(route, ct).ConfigureAwait(false);

        Require(response, route);

        try
        {
            return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (HttpRequestException fault)
        {
            throw new ContentDistributionException($"the bundle at '{route}' stopped mid-body.", fault);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _client.Dispose();

    private async Task<HttpResponseMessage> GetAsync(string route, CancellationToken ct)
    {
        try
        {
            return await _client.GetAsync(route, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException fault)
        {
            throw new ContentDistributionException($"'{route}' could not be reached.", fault);
        }
        catch (OperationCanceledException fault) when (!ct.IsCancellationRequested)
        {
            // The transport's own timeout, which arrives as a cancellation nobody asked for. Left
            // as one it would look like the app closing, and the sync above deliberately lets a
            // cancellation through untouched.
            throw new ContentDistributionException($"'{route}' did not answer in time.", fault);
        }
    }

    private static async Task<string> ReadTextAsync(
        HttpResponseMessage response, string route, CancellationToken ct)
    {
        Require(response, route);

        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static void Require(HttpResponseMessage response, string route)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new ContentDistributionException(
                $"'{route}' answered {(int)response.StatusCode}.");
        }
    }
}
