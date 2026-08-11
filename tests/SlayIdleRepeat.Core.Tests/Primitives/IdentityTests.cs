using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// `30` §4 — <see cref="PlayerId"/> and <see cref="RunId"/>, the two aggregate-root identities
/// M1 needs.
/// </summary>
/// <remarks>
/// The property that matters is that they are <b>not interchangeable</b>. A bare <c>string</c>
/// playerId handed to a method expecting a runId compiles and then loads the wrong aggregate; two
/// distinct types make that a compile error. The compiler enforces it — what these tests pin is
/// the one thing that would quietly switch the compiler off: a conversion operator.
/// </remarks>
public sealed class IdentityTests
{
    [Theory]
    [InlineData(typeof(PlayerId))]
    [InlineData(typeof(RunId))]
    public void An_id_declares_no_conversion_to_or_from_anything(Type idType)
    {
        var conversions = idType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .Select(m =>
                $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})" +
                $" -> {m.ReturnType.Name}")
            .ToArray();

        conversions.ShouldBeEmpty(
            $"{idType.Name} declares a conversion operator. The whole value of a distinct id type is " +
            "that a PlayerId cannot be passed where a RunId is expected, and a conversion — even to " +
            "string, even explicit — is the hole through which that protection leaks back out.");
    }

    [Fact]
    public void A_PlayerId_is_not_a_RunId()
    {
        typeof(PlayerId).IsAssignableFrom(typeof(RunId)).ShouldBeFalse();
        typeof(RunId).IsAssignableFrom(typeof(PlayerId)).ShouldBeFalse();

        // The real claim: neither is a string wearing a name. A `global using PlayerId = string;`
        // alias would satisfy both assertions above and none of the protection they stand for.
        typeof(PlayerId).ShouldNotBe(typeof(string));
        typeof(RunId).ShouldNotBe(typeof(string));
    }

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
                "the refusal must name PlayerId. Both id types guard the same way, and a message that " +
                "does not say which one threw sends the reader to the wrong seam.");
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
    public void An_id_carries_its_value_verbatim()
    {
        new PlayerId("p-0001").Value.ShouldBe("p-0001");
        new RunId("r-0001").Value.ShouldBe("r-0001");

        new PlayerId("p-0001").ToString().ShouldBe("p-0001");
        new RunId("r-0001").ToString().ShouldBe("r-0001");
    }

    [Fact]
    public void Two_ids_with_the_same_text_are_the_same_id()
    {
        new PlayerId("p-1").ShouldBe(new PlayerId("p-1"));
        new PlayerId("p-1").GetHashCode().ShouldBe(new PlayerId("p-1").GetHashCode());
        new RunId("r-1").ShouldBe(new RunId("r-1"));
    }

    [Fact]
    public void Ids_compare_ordinally_not_case_insensitively()
    {
        new PlayerId("p-1").ShouldNotBe(new PlayerId("P-1"));
        new RunId("r-1").ShouldNotBe(new RunId("R-1"));
    }
}
