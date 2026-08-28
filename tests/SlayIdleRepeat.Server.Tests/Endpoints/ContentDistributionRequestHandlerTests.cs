using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Server.Endpoints;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Endpoints;

/// <summary>
/// The two content-distribution contracts: the pointer document that moves, and the bundle that
/// cannot.
/// </summary>
/// <remarks>
/// The caching pair is asserted in both directions on purpose. Serving the pointer with a lifetime
/// strands every client on a content set the server has already replaced; serving a bundle without
/// one re-downloads bytes whose URL is the hash of those exact bytes.
/// </remarks>
public sealed class ContentDistributionRequestHandlerTests
{
    private static readonly ContentVersion Current =
        ContentVersion.FromHex(new string('a', ContentVersion.HexLength));

    private static readonly ContentVersion Other =
        ContentVersion.FromHex(new string('b', ContentVersion.HexLength));

    private static readonly byte[] Bytes = [0x1f, 0x8b, 0x08, 0x00, 0x42];

    /// <summary>A shelf holding exactly the named versions.</summary>
    private static Func<ContentVersion, ReadOnlyMemory<byte>?> Shelf(params ContentVersion[] held) =>
        version => held.Any(h => h.Equals(version)) ? Bytes : null;

    // ------------------------------------------------------------------ C4

    [Fact]
    public void The_pointer_names_the_current_stamp_and_the_route_its_bundle_is_served_on()
    {
        var reply = ContentDistributionRequestHandler.Current(Current);

        reply.StatusCode.ShouldBe(200);

        using var document = JsonDocument.Parse(reply.Body);

        document.RootElement.GetProperty("contentVersion").GetString()
            .ShouldBe(Current.Value, "the wire form of the stamp is the bare 64 hex characters");
        document.RootElement.GetProperty("bundleUrl").GetString()
            .ShouldBe("/content/" + Current.Value, "the pointer must name a route the client can actually GET");
    }

    /// <summary>
    /// The stamp rides bare. A prefixed spelling would not round-trip <c>ContentVersion.FromHex</c>
    /// and would not compare equal to the <c>BEGIN_SESSION</c> hash the same client sends back.
    /// </summary>
    [Fact]
    public void The_pointer_carries_no_algorithm_prefix_on_the_stamp()
    {
        var body = ContentDistributionRequestHandler.Current(Current).Body;

        body.ShouldNotContain("sha256:");
        body.ShouldNotContain("fnv1a:");

        using var document = JsonDocument.Parse(body);
        var named = document.RootElement.GetProperty("contentVersion").GetString();

        ContentVersion.TryFromHex(named!, out var parsed).ShouldBeTrue(
            "the client parses this field straight back into a stamp");
        parsed!.ShouldBe(Current);
    }

    [Fact]
    public void The_pointer_is_declared_json_and_is_never_cached()
    {
        var reply = ContentDistributionRequestHandler.Current(Current);

        reply.ContentType.ShouldBe("application/json; charset=utf-8");
        reply.CacheControl.ShouldBe(
            "no-cache",
            "this document is the one thing that moves; a cached copy pins a client to content the "
            + "server has already replaced");
    }

    [Fact]
    public void A_different_current_stamp_produces_a_different_pointer()
    {
        var first = ContentDistributionRequestHandler.Current(Current).Body;
        var second = ContentDistributionRequestHandler.Current(Other).Body;

        // The negative control for the two cases above: a handler that rendered a constant would
        // satisfy every shape assertion and never notice the content set changing underneath it.
        second.ShouldNotBe(first);
    }

    // ------------------------------------------------------------------ C5

    [Fact]
    public void A_retained_bundle_is_served_as_the_gzip_payload_it_is()
    {
        var reply = ContentDistributionRequestHandler.Bundle(Current.Value, Shelf(Current));

        reply.StatusCode.ShouldBe(200);
        reply.Body.ToArray().ShouldBe(Bytes);
        reply.ContentType.ShouldBe(
            "application/gzip",
            "the gzip is the payload whose hash is the contract, not a transfer encoding an "
            + "intermediary may undo");
    }

    [Fact]
    public void A_bundle_is_served_immutable_because_its_url_is_the_hash_of_its_bytes()
    {
        var reply = ContentDistributionRequestHandler.Bundle(Current.Value, Shelf(Current));

        reply.CacheControl.ShouldBe("public, max-age=31536000, immutable");
    }

    [Fact]
    public void The_bundle_route_serves_a_retained_version_that_is_not_the_current_one()
    {
        // The whole point of retention: a run pinned to an older stamp must still be able to fetch
        // the content it was played against.
        var reply = ContentDistributionRequestHandler.Bundle(Other.Value, Shelf(Current, Other));

        reply.StatusCode.ShouldBe(200);
        reply.Body.ToArray().ShouldBe(Bytes);
    }

    // ------------------------------------------------------------------ C6

    [Theory]
    [InlineData("")]
    [InlineData("deadbeef")]
    [InlineData("sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("../../etc/passwd")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_route_segment_that_is_not_a_stamp_is_a_404_and_the_shelf_is_never_asked(string requested)
    {
        var asked = new List<ContentVersion>();

        var reply = ContentDistributionRequestHandler.Bundle(
            requested,
            version =>
            {
                asked.Add(version);
                return Bytes;
            });

        reply.StatusCode.ShouldBe(404);
        reply.Body.Length.ShouldBe(0, "a 404 carries no contract shape");
        asked.ShouldBeEmpty(
            "text that is not a stamp must be refused before it reaches a file name — the segment "
            + "is untrusted, and the shelf names its files after stamps");
    }

    [Fact]
    public void A_well_formed_stamp_the_shelf_no_longer_holds_is_a_404()
    {
        var reply = ContentDistributionRequestHandler.Bundle(Other.Value, Shelf(Current));

        reply.StatusCode.ShouldBe(404);
        reply.Body.Length.ShouldBe(0);
    }

    /// <summary>
    /// The two 404s are indistinguishable on the wire. There is nothing an honest client does
    /// differently between them, and telling them apart would report which stamps this server has
    /// ever held.
    /// </summary>
    [Fact]
    public void A_malformed_segment_and_an_unretained_stamp_answer_identically()
    {
        var malformed = ContentDistributionRequestHandler.Bundle("deadbeef", Shelf(Current));
        var unretained = ContentDistributionRequestHandler.Bundle(Other.Value, Shelf(Current));

        malformed.StatusCode.ShouldBe(unretained.StatusCode);
        malformed.Body.Length.ShouldBe(unretained.Body.Length);
        malformed.ContentType.ShouldBe(unretained.ContentType);
        malformed.CacheControl.ShouldBe(unretained.CacheControl);
    }

    /// <summary>
    /// The negative control for the whole 404 block: the same shelf, the same handler, a stamp it
    /// does hold. Without this, a handler that answered 404 to everything would pass every case
    /// above.
    /// </summary>
    [Fact]
    public void The_shelf_that_refuses_every_case_above_still_serves_the_stamp_it_holds()
    {
        ContentDistributionRequestHandler.Bundle(Current.Value, Shelf(Current)).StatusCode.ShouldBe(200);
    }
}
