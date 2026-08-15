using Shouldly;
using SlayIdleRepeat.Core.Content.Dice;
using SlayIdleRepeat.Core.Rules.Dice;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Dice;

/// <summary>The seam between the wire-facing <c>DieFaceSpec.Kind</c> string and the real enum.</summary>
public sealed class DieFaceKindCodecTests
{
    [Theory]
    [InlineData("Pip", DieFaceKind.Pip)]
    [InlineData("Star", DieFaceKind.Star)]
    [InlineData("Surge", DieFaceKind.Surge)]
    [InlineData("Fortune", DieFaceKind.Fortune)]
    [InlineData("Void", DieFaceKind.Void)]
    [InlineData("Chain", DieFaceKind.Chain)]
    public void Every_one_of_04_1s_six_names_round_trips(string token, DieFaceKind kind)
    {
        DieFaceKindCodec.Parse(token).ShouldBe(kind);
        DieFaceKindCodec.ToWireToken(kind).ShouldBe(token);
    }

    [Theory]
    [InlineData("pip")]
    [InlineData("STAR")]
    [InlineData("Bogus")]
    [InlineData("")]
    public void An_unrecognised_token_is_rejected(string token)
    {
        Should.Throw<ArgumentException>(() => DieFaceKindCodec.Parse(token));
    }

    [Fact]
    public void A_null_token_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => DieFaceKindCodec.Parse(null!));
    }

    [Fact]
    public void An_undefined_kind_is_rejected_when_rendering()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DieFaceKindCodec.ToWireToken((DieFaceKind)999));
    }
}
