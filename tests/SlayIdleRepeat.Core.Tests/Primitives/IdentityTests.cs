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

    /// <summary>
    /// An id is a <c>readonly record struct</c>, not a <c>record</c> class.
    /// </summary>
    /// <remarks>
    /// This is the one shape decision in `30` §4 that <b>every other case in this file passes either
    /// way</b> — a record class carries its value, prints it, compares by it and rejects a blank one
    /// exactly as the struct does. The difference is in the bytes and in the nulls, so it needs its
    /// own case or nothing in the suite reads it.
    /// </remarks>
    [Theory]
    [InlineData(typeof(PlayerId))]
    [InlineData(typeof(RunId))]
    public void An_id_is_a_value_type_not_a_record_class(Type idType)
    {
        idType.IsValueType.ShouldBeTrue(
            $"{idType.Name} is a reference type. `14` §16.6 gives EVERY nullable-capable slot a " +
            "presence byte, so a class id adds one to every snapshot field that carries it — " +
            "PrimitiveEncodingTests pins that at 7 bytes, and this is the change that would move it. " +
            "It also makes a null id representable again in a field whose whole point is that it " +
            "cannot be absent.");
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
            .Message.ShouldMatchWildcard(
                "*RunId*",
                "the refusal must name RunId, for the same reason its PlayerId twin must name " +
                "PlayerId: the two guards are identical, so the type name is the only thing in the " +
                "message that says which seam the reader should open.");
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

    /// <summary>
    /// Ids compare <b>ordinally</b> — by code unit, never by culture.
    /// </summary>
    /// <remarks>
    /// Case is the cheap half. The half that actually distinguishes ordinal from culture-aware is
    /// canonical equivalence: <c>"a" + U+030A</c> (combining ring) and <c>U+00E5</c> render the same
    /// glyph and <i>are</i> equal under <see cref="StringComparison.InvariantCulture"/>, and are two
    /// different strings ordinally. An id whose equality went through a culture comparer would make
    /// two distinct player rows the same player on an ICU host and not on a globalization-invariant
    /// one — a divergence that never reproduces on the machine that reported it.
    /// </remarks>
    [Fact]
    public void Ids_compare_ordinally_not_by_culture_or_case()
    {
        new PlayerId("p-1").ShouldNotBe(new PlayerId("P-1"));
        new RunId("r-1").ShouldNotBe(new RunId("R-1"));

        var combining = "a" + (char)0x030A;
        var precomposed = ((char)0x00E5).ToString();

        new PlayerId(combining).ShouldNotBe(new PlayerId(precomposed));
        new RunId(combining).ShouldNotBe(new RunId(precomposed));
    }
}
