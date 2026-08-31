using Shouldly;
using SlayIdleRepeat.Application.Wire;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>The idempotency key's own guard — the receiving side of <c>IIdGeneratorPort.NewCommandId</c>'s contract.</summary>
public sealed class CommandIdTests
{
    [Theory]
    [InlineData("c_8f3a")]
    [InlineData("cid-abc-1")]
    [InlineData("A")]
    public void A_wellformed_id_constructs_and_round_trips(string value)
    {
        CommandId.IsWellFormed(value).ShouldBeTrue();
        new CommandId(value).Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData("", "empty")]
    [InlineData("a b", "an embedded space")]
    [InlineData(" a", "leading whitespace")]
    [InlineData("a\t", "a tab")]
    [InlineData("a\nb", "a newline")]
    [InlineData("a\u0001", "a control character")]
    public void A_value_that_would_not_survive_a_url_a_log_line_or_a_header_is_refused(string? value, string why)
    {
        CommandId.IsWellFormed(value).ShouldBeFalse(why);

        if (value is not null)
        {
            Should.Throw<ArgumentException>(() => new CommandId(value));
        }
    }

    [Fact]
    public void Equality_is_ordinal_over_the_text()
    {
        new CommandId("c-1").ShouldBe(new CommandId("c-1"));
        new CommandId("c-1").ShouldNotBe(new CommandId("C-1"));
    }
}
