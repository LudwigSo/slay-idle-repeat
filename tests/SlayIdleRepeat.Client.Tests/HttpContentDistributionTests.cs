using System.Net;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Client.Game.Net;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The socket under the content sync: two routes, the stamp handed back untouched, and the bytes
/// handed on to the verifier that decides whether to believe them.
/// </summary>
/// <remarks>
/// Driven against a scripted handler rather than a server — no host, no socket, no container. What
/// is asserted is what this adapter decides: which route, which method, and what it does with an
/// answer.
/// </remarks>
public sealed class HttpContentDistributionTests
{
    private static readonly Uri ServerBase = new("http://content.test/");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task FetchCurrentVersionAsync_reads_the_pointer_route_with_a_GET()
    {
        var served = Snapshot(1m);
        var server = new ScriptedContentServer(served.Version.Value);

        await Client(server).FetchCurrentVersionAsync(CancellationToken.None);

        server.Requests.ShouldBe(
            ["GET /content/current"],
            "the pointer is one unauthenticated read of a fixed route. A different verb or a route " +
            "assembled by hand is a client speaking a protocol the server does not answer, and the " +
            "only symptom is a content sync that reports the server unreachable while it is up.");
    }

    /// <summary>
    /// 🔒 The stamp is handed back <b>verbatim</b>, however malformed it looks.
    /// </summary>
    /// <remarks>
    /// The seam's contract is that a version is "not yet known to be well-formed" — the presenter
    /// above turns a bad one into a named failure a player is shown, and it can only do that if it
    /// is told exactly what the server said. An adapter that trimmed or lower-cased on the way past
    /// would silently repair a server defect into a stamp that then fails to match anything.
    /// </remarks>
    [Fact]
    public async Task FetchCurrentVersionAsync_answers_exactly_what_the_pointer_named()
    {
        const string Untidy = "  DEADBEEF  ";
        var server = new ScriptedContentServer(Untidy);

        var named = await Client(server).FetchCurrentVersionAsync(CancellationToken.None);

        named.ShouldBe(
            Untidy,
            "the stamp came back normalised. Whitespace and case are exactly what tells a malformed " +
            "pointer from a good one, and repairing them here turns a server-side defect into a " +
            "download of a bundle nobody named.");
    }

    [Fact]
    public async Task FetchBundleAsync_reads_the_route_for_the_stamp_it_was_given()
    {
        var served = Snapshot(2m);
        var server = new ScriptedContentServer(served.Version.Value, Bundle(served));

        await Client(server).FetchBundleAsync(served.Version.Value, CancellationToken.None);

        server.Requests.ShouldBe(
            [$"GET /content/{served.Version.Value}"],
            "the bundle route is the stamp, resolved against the configured base address.");
    }

    /// <summary>
    /// 🔒 <b>The pointer's <c>bundleUrl</c> is never followed.</b>
    /// </summary>
    /// <remarks>
    /// A pointer document naming another host would otherwise redirect this client's content
    /// download wherever it liked, and the bytes would then be re-stamped against a version that
    /// same document supplied. The signature carries a stamp and no URL, so the redirect is
    /// structurally impossible — this is the case that says so.
    /// </remarks>
    [Fact]
    public async Task FetchBundleAsync_ignores_the_url_the_pointer_named()
    {
        var served = Snapshot(2m);
        var server = new ScriptedContentServer(
            served.Version.Value, Bundle(served), bundleUrl: "http://content.test/elsewhere/decoy.gz");
        var client = Client(server);

        await client.FetchCurrentVersionAsync(CancellationToken.None);
        await client.FetchBundleAsync(served.Version.Value, CancellationToken.None);

        server.Requests.ShouldBe(
            ["GET /content/current", $"GET /content/{served.Version.Value}"],
            "the client followed the pointer's own URL. That member hints at a distribution network " +
            "this project does not have, and honouring it hands a document the power to say where " +
            "the game's rules are downloaded from.");
    }

    /// <summary>
    /// 🔒 The bytes reach the verifier unaltered — which is the whole point of the socket.
    /// </summary>
    /// <remarks>
    /// Asserted by re-stamping through the real verifier rather than by comparing arrays: an
    /// adapter that decompressed on the way past, or that dropped a trailing chunk, produces bytes
    /// that are still bytes and a bundle that no longer opens.
    /// </remarks>
    [Fact]
    public async Task FetchBundleAsync_hands_over_bytes_the_verifier_opens_at_the_served_stamp()
    {
        var served = Snapshot(3m);
        var server = new ScriptedContentServer(served.Version.Value, Bundle(served));

        var bytes = await Client(server).FetchBundleAsync(served.Version.Value, CancellationToken.None);

        ContentBundle.Open(bytes, served.Version).Version.ShouldBe(
            served.Version,
            "the downloaded bundle no longer re-stamps to the version it was served under, so either " +
            "the transport altered the bytes — automatic decompression is the usual culprit, since " +
            "gzip here is the media type and not a transfer encoding — or it truncated them. Either " +
            "way the sync above reports a rejected bundle and the game never updates.");
    }

    [Fact]
    public async Task A_server_error_on_the_pointer_is_a_content_distribution_failure()
    {
        var server = new ScriptedContentServer("unused") { ForcedStatus = HttpStatusCode.InternalServerError };

        await Should.ThrowAsync<ContentDistributionException>(
            () => Client(server).FetchCurrentVersionAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_server_error_on_the_bundle_is_a_content_distribution_failure()
    {
        var served = Snapshot(2m);
        var server = new ScriptedContentServer(served.Version.Value, Bundle(served))
        {
            ForcedStatus = HttpStatusCode.InternalServerError,
        };

        await Should.ThrowAsync<ContentDistributionException>(
            () => Client(server).FetchBundleAsync(served.Version.Value, CancellationToken.None));
    }

    private static HttpContentDistribution Client(ScriptedContentServer server) =>
        new(ServerBase, Timeout, server);

    private static ContentSnapshot Snapshot(decimal value)
    {
        var documents = new[]
        {
            new ContentDocument(
                "tuning/a.json",
                ContentValue.Object([new KeyValuePair<string, ContentValue>("x", ContentValue.Number(value))])),
        };

        return new ContentSnapshot(ContentHashing.Compute(documents), documents);
    }

    private static byte[] Bundle(ContentSnapshot snapshot) =>
        ContentBundle.Pack(snapshot.DocumentPaths.Select(snapshot.GetDocument));
}
