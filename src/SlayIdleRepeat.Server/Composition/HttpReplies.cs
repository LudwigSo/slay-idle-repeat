namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// 🔒 The one place a request handler's answer becomes an HTTP response, and the one place a
/// request body becomes a string.
/// </summary>
/// <remarks>
/// <para>
/// Every functional area here follows the same shape: the route lambda calls a handler that returns
/// a status and a body, and the lambda writes it. M5-03, M5-06 and M5-07 each arrived at that shape
/// independently and each brought its own copy of these two methods — byte-identical apart from the
/// one header the query area adds. Three copies of "what content type does a JSON answer carry" is
/// three places to get an empty-body response wrong, and the copies had already begun to drift.
/// </para>
/// <para>
/// The reply <em>records</em> are deliberately not unified with them. Each area's record is part of
/// its handler's signature and its own tests; what is shared here is the transport plumbing, which
/// belongs to the composition root and to nothing else.
/// </para>
/// </remarks>
internal static class HttpReplies
{
    /// <summary>The content type every JSON answer carries. Never sent with an empty body.</summary>
    private const string Json = "application/json; charset=utf-8";

    /// <summary>Reads a request body as text.</summary>
    /// <param name="http">The request being served.</param>
    /// <param name="ct">Cancellation.</param>
    internal static async Task<string> ReadBodyAsync(HttpContext http, CancellationToken ct)
    {
        using var reader = new StreamReader(http.Request.Body);
        return await reader.ReadToEndAsync(ct);
    }

    /// <summary>Writes one handler's answer.</summary>
    /// <param name="http">The request being served.</param>
    /// <param name="statusCode">The status the handler decided.</param>
    /// <param name="body">The JSON body, or empty for a status-only answer.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="cacheControl">
    /// The <c>Cache-Control</c> value this area must send, or <see langword="null"/> to send none.
    /// Passed rather than defaulted: a read that must never be cached says so at its own route, and
    /// a default here would silently mark every other area's answers with it too.
    /// </param>
    internal static async Task WriteAsync(
        HttpContext http, int statusCode, string body, CancellationToken ct, string? cacheControl = null)
    {
        http.Response.StatusCode = statusCode;

        if (cacheControl is not null)
        {
            http.Response.Headers.CacheControl = cacheControl;
        }

        if (body.Length > 0)
        {
            http.Response.ContentType = Json;
            await http.Response.WriteAsync(body, ct);
        }
    }
}
