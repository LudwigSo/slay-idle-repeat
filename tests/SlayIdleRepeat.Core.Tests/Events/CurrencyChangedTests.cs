using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>The event every currency movement emits: the one thing it refuses, and the text it renders.</summary>
public sealed class CurrencyChangedTests
{
    [Fact]
    public void A_currency_change_reaches_its_consumers_as_a_DomainEvent()
    {
        // The assignment IS the derivation claim — it stops compiling if the base type goes.
        DomainEvent asEvent = new CurrencyChanged(7, CurrencyId.GOLD, -10, "shop_purchase");

        asEvent.Sequence.ShouldBe(
            7,
            "a mis-forwarded base constructor — ': DomainEvent(0)' — would give every consumer the same " +
            "ordinal while the derived record still read back correctly.");
    }

    /// <summary>The reason is what turns income attribution into a query over events; a blank one is unusable.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void A_currency_change_with_no_reason_is_refused(string? reason)
    {
        var thrown = Should.Throw<ArgumentException>(
            () => new CurrencyChanged(1, CurrencyId.SOUL_SHARDS, 5, reason!));

        thrown.ParamName.ShouldBe("Reason");

        // Pins which rule fired, not just that an ArgumentException was thrown — several guards in
        // Core throw one, and only this one is this rule's.
        thrown.Message.ShouldContain("21 §8.3", Case.Sensitive);
        thrown.Message.ShouldContain("income_attribution", Case.Sensitive);
    }

    /// <summary>
    /// A negative delta is what makes the culture claim real: under <c>sv-SE</c> a delta of -10
    /// renders with U+2212 rather than U+002D, and the boxing in a synthesized <c>PrintMembers</c>
    /// hides that from the IL scan.
    /// </summary>
    [Fact]
    public void ToString_renders_identically_under_any_culture()
    {
        var swedish = new CultureInfo("sv-SE");

        (-10L).ToString(swedish).ShouldNotBe(
            (-10L).ToString(CultureInfo.InvariantCulture),
            "under globalization-invariant mode new CultureInfo(\"sv-SE\") silently returns the " +
            "invariant culture, and the comparison below would then hold over nothing.");

        var spend = new CurrencyChanged(2, CurrencyId.GOLD, -10L, "shrine_purchase");

        Render(spend, swedish).ShouldBe(Render(spend, CultureInfo.InvariantCulture));

        Render(spend, CultureInfo.InvariantCulture)
            .ShouldContain("Sequence = 2, Id = GOLD, Delta = -10, Reason = shrine_purchase", Case.Sensitive);

        Render(spend, swedish).ShouldNotContain("−", Case.Sensitive,
            "U+2212 MINUS SIGN is what sv-SE renders a negative integer with. If it appears here the " +
            "hand-written PrintMembers is gone and the synthesized one is back.");
    }

    private static string Render(CurrencyChanged evt, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            return evt.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
