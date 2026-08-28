using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Server.Endpoints;

/// <summary>What <c>GET /content/current</c> writes back — status, body and the two pinned headers.</summary>
/// <param name="StatusCode">Always 200: the server always knows which content it is serving.</param>
/// <param name="Body">The pointer document naming the current stamp and where its bundle lives.</param>
/// <param name="ContentType">The declared content type.</param>
/// <param name="CacheControl">The declared cache policy — this document is the thing that changes.</param>
public sealed record ContentCurrentReply(int StatusCode, string Body, string ContentType, string CacheControl);

/// <summary>What <c>GET /content/{version}</c> writes back — status, bytes and the two pinned headers.</summary>
/// <param name="StatusCode">200 when the version is retained, 404 when it is unknown or malformed.</param>
/// <param name="Body">The gzip bundle, or empty on a 404.</param>
/// <param name="ContentType">The declared content type.</param>
/// <param name="CacheControl">The declared cache policy — a bundle is named by its own hash and can never change.</param>
public sealed record ContentBundleReply(int StatusCode, ReadOnlyMemory<byte> Body, string ContentType, string CacheControl);

/// <summary>The two content-distribution handlers as the plain functions they are — no ASP.NET in any signature.</summary>
/// <remarks>
/// <para>
/// The pair is deliberately asymmetric about caching. The pointer document is the one thing that
/// moves, so it is <c>no-cache</c>: a client that cached it would keep fetching a content set the
/// server has already replaced. A bundle is named by the hash of its own bytes, so it can never
/// change under its URL and is served immutable for a year.
/// </para>
/// <para>
/// 🔒 A malformed version and an unretained one both answer 404 with an empty body, and neither
/// says which it was. There is nothing an honest client can do differently between the two — both
/// mean "go and read the pointer again" — and distinguishing them would tell a prober which stamps
/// this server has ever held.
/// </para>
/// </remarks>
public static class ContentDistributionRequestHandler
{
    /// <summary>Answers the pointer request with the version this server is serving now.</summary>
    /// <param name="current">The current content stamp.</param>
    /// <returns>200, the pointer document, and <c>no-cache</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="current"/> is null.</exception>
    public static ContentCurrentReply Current(ContentVersion current)
    {
        ArgumentNullException.ThrowIfNull(current);

        throw new NotImplementedException();
    }

    /// <summary>Answers a bundle request out of whatever the shelf holds.</summary>
    /// <param name="requestedVersion">The stamp as it arrived on the route — untrusted text, not yet a version.</param>
    /// <param name="read">Reads one retained bundle, answering <c>null</c> when the shelf does not hold it.</param>
    /// <returns>200 with the gzip bundle, or 404 with an empty body.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="requestedVersion"/> or <paramref name="read"/> is null.</exception>
    public static ContentBundleReply Bundle(
        string requestedVersion, Func<ContentVersion, ReadOnlyMemory<byte>?> read)
    {
        ArgumentNullException.ThrowIfNull(requestedVersion);
        ArgumentNullException.ThrowIfNull(read);

        throw new NotImplementedException();
    }
}
