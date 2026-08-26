namespace SlayIdleRepeat.Server.Endpoints;

/// <summary>What <c>GET /config</c> writes back — status, body and the two headers the contract pins.</summary>
/// <param name="StatusCode">Always 200: the document exists even when the file does not (the rendered identity).</param>
/// <param name="Body">The flags document, byte-for-byte what the source holds.</param>
/// <param name="ContentType">The declared content type.</param>
/// <param name="CacheControl">The declared cache policy — 14 §10's client-side caching half.</param>
public sealed record RemoteConfigReply(int StatusCode, string Body, string ContentType, string CacheControl);

/// <summary>The <c>GET /config</c> handler as the plain function it is — no ASP.NET in its signature.</summary>
public static class RemoteConfigRequestHandler
{
    /// <summary>Answers the config request with the source's current document.</summary>
    /// <param name="document">The flags document the source currently holds.</param>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public static RemoteConfigReply Handle(string document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new RemoteConfigReply(200, string.Empty, string.Empty, string.Empty);
    }
}
