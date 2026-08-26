using Shouldly;
using SlayIdleRepeat.Server.Composition;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Composition;

/// <summary>
/// The M5-06 placeholder's whole contract: <c>Bearer &lt;playerId&gt;</c> resolves that player,
/// everything else is a 401. It authenticates nothing, and these cases pin that it at least never
/// resolves a player from a header that names none.
/// </summary>
public sealed class PlaceholderBearerPlayerIdResolverTests
{
    private static readonly PlaceholderBearerPlayerIdResolver Resolver = new();

    [Fact]
    public void A_bearer_header_resolves_the_player_it_names()
    {
        var resolution = Resolver.Resolve("Bearer PLAYER_abc");

        resolution.RefusalStatus.ShouldBeNull();
        resolution.Player!.Value.Value.ShouldBe("PLAYER_abc");
    }

    [Theory]
    [InlineData(null, "no header at all")]
    [InlineData("", "an empty header")]
    [InlineData("Bearer", "the scheme with no token")]
    [InlineData("Bearer ", "the scheme with an empty token")]
    [InlineData("Bearer   ", "the scheme with a whitespace token")]
    [InlineData("Basic PLAYER_abc", "a different scheme")]
    [InlineData("bearer PLAYER_abc", "the scheme in the wrong case — the match is ordinal")]
    public void Anything_else_is_unauthorized(string? header, string why)
    {
        var resolution = Resolver.Resolve(header);

        resolution.Player.ShouldBeNull(why);
        resolution.RefusalStatus.ShouldBe(
            401, "14 §16.2's 401: the client refreshes and retries the SAME commandId");
    }
}
