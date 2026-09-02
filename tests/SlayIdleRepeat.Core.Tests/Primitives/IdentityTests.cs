using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary><see cref="PlayerId"/> and <see cref="RunId"/>, the two aggregate-root identities.</summary>
public sealed class IdentityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_PlayerId_refuses_a_blank_identifier(string? blank)
    {
        Should.Throw<ArgumentException>(() => new PlayerId(blank!))
            .Message.ShouldMatchWildcard(
                "*PlayerId*",
                "both id types guard the same way, so the type name is the only thing in the " +
                "message that says which seam the reader should open.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_RunId_refuses_a_blank_identifier(string? blank)
    {
        Should.Throw<ArgumentException>(() => new RunId(blank!))
            .Message.ShouldMatchWildcard("*RunId*");
    }

    [Fact]
    public void An_id_renders_its_value_verbatim()
    {
        new PlayerId("p-0001").ToString().ShouldBe("p-0001", "a log line reads the id, not the record's shape");
        new RunId("r-0001").ToString().ShouldBe("r-0001");
    }

    /// <summary>
    /// A struct's default runs no constructor, so <c>Value</c> is null there — the one hole the blank
    /// guard cannot close. A bare <c>=&gt; Value</c> would make <c>$"{id}"</c> the empty string at the
    /// one moment a reader needs the log line to say the id was never set.
    /// </summary>
    [Fact]
    public void A_default_id_prints_as_the_default_it_is()
    {
        default(PlayerId).ToString().ShouldBe("default(PlayerId)");
        default(RunId).ToString().ShouldBe("default(RunId)");

        $"{default(PlayerId)}".ShouldNotBeNullOrEmpty(
            "an interpolated default id must say something. The empty string is the answer that " +
            "makes an unset id look like an absent log field.");
    }
}
