using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// `14` §16.6 — the primitives M1-04 and M1-05 will put in a snapshot must already have a
/// canonical encoding.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CanonicalStateWriter"/> dispatches over a <b>closed allowlist</b> with no
/// <c>IEnumerable</c> fallback, so "will this hash?" is a real question with a real answer, and
/// the wrong answer is a <c>NotSupportedException</c> discovered by whoever declares
/// <c>PlayerSnapshot</c> rather than by whoever chose the id's shape. These tests answer it here,
/// in the task that chooses the shape.
/// </para>
/// <para>
/// They also pin <b>what</b> the encoding is, not merely that one exists: an id encodes as its
/// string and an enum as its number, with the id type contributing no bytes of its own. That is
/// what makes the id wrapper free at the wire, and it is the assumption M1-04's field-order pin
/// will be written on top of.
/// </para>
/// </remarks>
public sealed class PrimitiveEncodingTests
{
    private sealed record PlayerIdCarrier(PlayerId Id);

    private sealed record RunIdCarrier(RunId Id);

    private sealed record TextCarrier(string Text);

    private sealed record CurrencyCarrier(CurrencyId Currency);

    private sealed record ReasonCarrier(RejectionReason Reason);

    private sealed record NumberCarrier(int Number);

    [Fact]
    public void A_PlayerId_encodes_exactly_as_the_string_it_wraps()
    {
        CanonicalStateWriter.CanonicalBytes(new PlayerIdCarrier(new PlayerId("p-0001")))
            .ShouldBe(
                CanonicalStateWriter.CanonicalBytes(new TextCarrier("p-0001")),
                "a PlayerId is a positional record over one string, so 14 §16.6 encodes it as that " +
                "string: a 4-byte little-endian UTF-8 byte count then the bytes. The wrapper costs " +
                "nothing at the wire, and no CanonicalStateWriter allowlist entry was needed for it.");
    }

    [Fact]
    public void A_RunId_encodes_exactly_as_the_string_it_wraps()
    {
        CanonicalStateWriter.CanonicalBytes(new RunIdCarrier(new RunId("r-0001")))
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(new TextCarrier("r-0001")));
    }

    [Fact]
    public void An_id_is_not_a_presence_byte_wider_than_its_string()
    {
        var bytes = CanonicalStateWriter.CanonicalBytes(new PlayerIdCarrier(new PlayerId("ab")));

        bytes.Length.ShouldBe(
            7,
            "one presence byte for the string slot, 4 bytes of length prefix, 2 UTF-8 bytes — and " +
            "nothing for the id wrapper itself. A record CLASS id would be nullable-capable and pick " +
            "up a SECOND presence byte (§16.6: every nullable-capable slot carries one), moving every " +
            "stateHash M1-04 will pin. A readonly record struct cannot be absent, so it does not.");
    }

    [Fact]
    public void A_CurrencyId_encodes_as_its_numeric_value()
    {
        CanonicalStateWriter.CanonicalBytes(new CurrencyCarrier(CurrencyId.HONOR))
            .ShouldBe(
                CanonicalStateWriter.CanonicalBytes(new NumberCarrier((int)CurrencyId.HONOR)),
                "14 §16.6 encodes an enum through its underlying integral type. This is why the " +
                "explicit numbers on CurrencyId are wire values: the name never reaches the bytes.");
    }

    [Fact]
    public void A_RejectionReason_encodes_as_its_numeric_value()
    {
        CanonicalStateWriter.CanonicalBytes(new ReasonCarrier(RejectionReason.INSUFFICIENT_ENERGY))
            .ShouldBe(
                CanonicalStateWriter.CanonicalBytes(
                    new NumberCarrier((int)RejectionReason.INSUFFICIENT_ENERGY)));
    }

    [Fact]
    public void Two_ids_with_different_text_encode_differently()
    {
        CanonicalStateWriter.CanonicalBytes(new PlayerIdCarrier(new PlayerId("p-0001")))
            .ShouldNotBe(CanonicalStateWriter.CanonicalBytes(new PlayerIdCarrier(new PlayerId("p-0002"))));
    }

    [Fact]
    public void The_field_order_traversal_sees_an_id_as_its_string()
    {
        CanonicalStateWriter.CanonicalFieldOrder(typeof(PlayerIdCarrier))
            .ShouldBe(
                new[] { "Id.Value:System.String" },
                "the SchemaVersion field-order pin descends into an id rather than treating it as a " +
                "leaf, so wrapping a snapshot field in an id reads as the shape change it is.");
    }
}
