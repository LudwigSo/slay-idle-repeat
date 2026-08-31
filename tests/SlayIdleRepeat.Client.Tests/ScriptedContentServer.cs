using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The content-distribution routes, answered offline: a handler that records what it was asked and
/// replies with the pointer document and the bundle bytes a case scripted.
/// </summary>
/// <remarks>
/// <para>
/// No host, no socket, no container: everything below is a function from a request to a response.
/// A fresh file rather than the contract suite's game-server handler, because this suite cannot see
/// that project and the two answer different routes.
/// </para>
/// <para>
/// 🔒 The pointer document is written by hand at the server's own spelling. The client is being
/// tested against the JSON a server actually sends, so rendering it through some client-side policy
/// would be testing a dialect nobody speaks.
/// </para>
/// </remarks>
internal sealed class ScriptedContentServer : HttpMessageHandler
{
    private readonly string _pointerVersion;
    private readonly string _bundleUrl;
    private readonly byte[] _bundle;

    /// <summary>Answers with the given stamp and bytes.</summary>
    /// <param name="pointerVersion">Exactly what the pointer document names, well-formed or not.</param>
    /// <param name="bundle">The bytes the bundle route serves.</param>
    /// <param name="bundleUrl">The pointer's own URL member — a decoy, since nothing may follow it.</param>
    internal ScriptedContentServer(string pointerVersion, byte[]? bundle = null, string? bundleUrl = null)
    {
        _pointerVersion = pointerVersion;
        _bundle = bundle ?? [];
        _bundleUrl = bundleUrl ?? "http://decoy.invalid/somewhere/else.gz";
    }

    /// <summary>Every request this server was sent, as <c>METHOD path</c>, in order.</summary>
    internal List<string> Requests { get; } = [];

    /// <summary>A status forced on every answer, or <c>null</c> to answer normally.</summary>
    internal HttpStatusCode? ForcedStatus { get; set; }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;

        Requests.Add($"{request.Method.Method} {uri.AbsolutePath}");

        if (ForcedStatus is { } forced)
        {
            return Task.FromResult(new HttpResponseMessage(forced));
        }

        return Task.FromResult(
            uri.AbsolutePath switch
            {
                "/content/current" => Pointer(),
                _ when uri.AbsolutePath.StartsWith("/content/", StringComparison.Ordinal) => Bundle(),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
    }

    private HttpResponseMessage Pointer() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new { contentVersion = _pointerVersion, bundleUrl = _bundleUrl }),
                Encoding.UTF8,
                "application/json"),
        };

    private HttpResponseMessage Bundle()
    {
        var content = new ByteArrayContent(_bundle);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
