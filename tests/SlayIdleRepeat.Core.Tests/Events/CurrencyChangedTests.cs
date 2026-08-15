using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// Every currency movement in the game emits <c>CurrencyChanged</c> with a reason. This suite covers
/// the event itself: the four things it carries, and the one thing it refuses. The reason is what
/// turns income attribution into a query over events rather than hand-written bookkeeping that will
/// disagree — an unattributed row is unusable, which is why a blank one is refused at construction.
/// </summary>
public sealed class CurrencyChangedTests
{
    [Fact]
    public void A_currency_change_carries_the_currency_the_delta_and_the_reason()
    {
        var moved = new CurrencyChanged(3, CurrencyId.CROWNS, 250, "dungeon_clear");

        moved.Sequence.ShouldBe(3);
        moved.Id.ShouldBe(CurrencyId.CROWNS);
        moved.Delta.ShouldBe(250L);
        moved.Reason.ShouldBe("dungeon_clear");
    }

    /// <summary>
    /// It reaches its consumers as a <c>DomainEvent</c>: consumers read one heterogeneous list, not
    /// bespoke hooks, so the ordinal they order by has to be the base's <c>Sequence</c>.
    /// </summary>
    [Fact]
    public void A_currency_change_reaches_its_consumers_as_a_DomainEvent()
    {
        // The assignment IS the derivation claim — it stops compiling if the base type goes.
        DomainEvent asEvent = new CurrencyChanged(7, CurrencyId.GOLD, -10, "shop_purchase");

        asEvent.Sequence.ShouldBe(
            7,
            "a mis-forwarded base constructor — ': DomainEvent(0)' — would give every consumer the same " +
            "ordinal while the derived record still read back correctly.");

        typeof(CurrencyChanged).GetProperty(nameof(DomainEvent.Sequence))!.DeclaringType.ShouldBe(
            typeof(DomainEvent),
            "the consumers read Sequence off DomainEvent. A redeclared one on the subtype would shadow it, " +
            "and the animation script (14 §2.4) and the economy log (14 §7.1) would order by the base's.");
    }

    /// <summary>The reason is not optional; null, empty and whitespace are all refused.</summary>
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

    [Fact]
    public void A_reason_is_kept_exactly_as_given()
    {
        new CurrencyChanged(1, CurrencyId.HONOR, 1, " arena_win ").Reason.ShouldBe(" arena_win ");
    }

    /// <summary>
    /// A spend is a negative delta and stays one. Construction must not clamp, absolute-value or
    /// reject it, since income reporting nets income against spend.
    /// </summary>
    [Fact]
    public void A_spend_is_a_negative_delta_and_is_kept_as_one()
    {
        new CurrencyChanged(1, CurrencyId.ENERGY, -20, "run_start").Delta.ShouldBe(-20L);
    }

    /// <summary>
    /// This pins a rule that is deliberately absent. A zero delta is permitted: every movement emits
    /// an event, but not every event needs to be a movement (an energy clamp may well produce one).
    /// </summary>
    [Fact]
    public void A_zero_delta_is_permitted()
    {
        new CurrencyChanged(1, CurrencyId.MERGE_DUST, 0, "merge_refund_capped").Delta.ShouldBe(0L);
    }

    /// <summary>
    /// One event type covers all eight currencies, including <c>GOLD</c>, which is run-scoped while
    /// the other seven are player-scoped. That splits storage across two aggregates; it does not
    /// split this event.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCurrency))]
    public void Every_wallet_currency_is_expressible_as_one_currency_change(CurrencyId currency)
    {
        new CurrencyChanged(1, currency, 1, "test").Id.ShouldBe(currency);
    }

    /// <summary>The floor under the theory above: an empty source would run zero cases and pass.</summary>
    [Fact]
    public void The_currency_theory_covers_the_whole_wallet()
    {
        Wallet.Length.ShouldBe(
            8,
            "10 §1 fixes eight wallet currencies, and Wallet is what the MemberData source is built from. " +
            "A source that shrank would run fewer cases and still report success.");
    }

    /// <summary>
    /// Value equality over all four components. The economy log is append-only and the replay script
    /// is ordered, so two credits of the same amount for the same reason are distinct rows
    /// distinguished by nothing but the ordinal.
    /// </summary>
    [Fact]
    public void Two_currency_changes_are_equal_only_when_every_component_matches()
    {
        var first = new CurrencyChanged(4, CurrencyId.CROWNS, 100, "daily_login");

        first.ShouldBe(new CurrencyChanged(4, CurrencyId.CROWNS, 100, "daily_login"));

        first.ShouldNotBe(new CurrencyChanged(5, CurrencyId.CROWNS, 100, "daily_login"));
        first.ShouldNotBe(new CurrencyChanged(4, CurrencyId.GOLD, 100, "daily_login"));
        first.ShouldNotBe(new CurrencyChanged(4, CurrencyId.CROWNS, 101, "daily_login"));
        first.ShouldNotBe(new CurrencyChanged(4, CurrencyId.CROWNS, 100, "Daily_Login"));
    }

    /// <summary>
    /// The event renders identically under every culture, and a negative delta is what makes that a
    /// real claim: under <c>sv-SE</c> a delta of -10 renders with U+2212 rather than U+002D, and the
    /// boxing in a synthesized <c>PrintMembers</c> hides that from the IL scan. Swedish rather than
    /// German, since German renders a negative integer with an ordinary hyphen and would prove nothing.
    /// </summary>
    [Fact]
    public void ToString_renders_identically_under_any_culture()
    {
        var swedish = new CultureInfo("sv-SE");

        (-10L).ToString(swedish).ShouldNotBe(
            (-10L).ToString(CultureInfo.InvariantCulture),
            "this assertion is only meaningful if the runtime actually has a Swedish culture. Under " +
            "globalization-invariant mode new CultureInfo(\"sv-SE\") silently returns the invariant " +
            "culture, and the comparison below would then hold over nothing.");

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

    /// <summary>The wallet, read off the enum rather than transcribed.</summary>
    private static readonly CurrencyId[] Wallet = Enum.GetValues<CurrencyId>();

    /// <summary>The wallet as theory data.</summary>
    public static TheoryData<CurrencyId> EveryCurrency()
    {
        var data = new TheoryData<CurrencyId>();

        foreach (var currency in Wallet)
        {
            data.Add(currency);
        }

        return data;
    }
}
