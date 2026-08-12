using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// 🔒 `30` §7 — *"Every currency movement in the game emits <c>CurrencyChanged</c> with a
/// reason."* This suite covers the event itself: the four things it carries, and the one thing it
/// refuses.
/// </summary>
/// <remarks>
/// <para>
/// The rule that every currency <i>mutation</i> emits one is
/// <c>DomainPurityTests.Every_currency_mutation_emits_CurrencyChanged</c> (M0-08), an IL scan that
/// has existed since before this type did. It is still <b>vacuous</b> after this commit and stays
/// so until M1-04: it recognises its subjects through a hard-coded <c>CurrencyId</c> field-type
/// name, and the first currency-carrying field arrives with the <c>Player</c> aggregate. What this
/// commit changes is that the event half of that rule's predicate now names a real type.
/// </para>
/// <para>
/// The reason is what turns `21` §8.3's <c>income_attribution.csv</c> — the report answering risk
/// <b>R10</b>, the compounding of dungeon, event and guild income — into a query over events
/// rather than thirty pieces of hand-written bookkeeping that will disagree with each other. An
/// unattributed row is a row that report cannot use, which is why a blank one is refused at
/// construction rather than logged and skipped later.
/// </para>
/// </remarks>
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
    /// It is a <c>DomainEvent</c>, which is the whole mechanism: `30` §7's four consumers read one
    /// heterogeneous list, not four bespoke hooks.
    /// </summary>
    [Fact]
    public void A_currency_change_is_a_DomainEvent()
    {
        DomainEvent asEvent = new CurrencyChanged(7, CurrencyId.GOLD, -10, "shop_purchase");

        asEvent.ShouldBeOfType<CurrencyChanged>();
        asEvent.Sequence.ShouldBe(7);
    }

    /// <summary>
    /// 🔒 The reason is not optional. Null, empty and whitespace are all refused, and the message
    /// names the report that stops working without it rather than reading like a null check.
    /// </summary>
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

        // Pins WHICH rule fired, not just that an ArgumentException was thrown (steering S2):
        // several guards in Core throw ArgumentException, and only this one is 30 §7's.
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
    /// reject it: `21` §8.3 nets income against spend, and a report that only ever sees credits
    /// answers R10 with the wrong number rather than with no number.
    /// </summary>
    [Fact]
    public void A_spend_is_a_negative_delta_and_is_kept_as_one()
    {
        new CurrencyChanged(1, CurrencyId.ENERGY, -20, "run_start").Delta.ShouldBe(-20L);
    }

    /// <summary>
    /// ⚠️ This pins a rule that is deliberately <b>absent</b>. A zero delta is permitted, because
    /// `30` §7 says every movement emits an event — it does not say every event is a movement, and
    /// M1-10's energy clamp may well produce one. If a later milestone decides a zero-delta row is
    /// noise in the economy log, that is a design change and it should cost this edit.
    /// </summary>
    [Fact]
    public void A_zero_delta_is_permitted()
    {
        new CurrencyChanged(1, CurrencyId.MERGE_DUST, 0, "merge_refund_capped").Delta.ShouldBe(0L);
    }

    /// <summary>
    /// One event type covers all eight currencies — including <c>GOLD</c>, which milestone
    /// assumption <b>A3</b> makes run-scoped while the other seven are player-scoped. That splits
    /// the <i>storage</i> across the <c>Run</c> and <c>Player</c> aggregates in M1-04/M1-05; it
    /// does not split this event, or `21` §8.3 would have to union two tables.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryCurrency))]
    public void Every_wallet_currency_is_expressible_as_one_currency_change(CurrencyId currency)
    {
        new CurrencyChanged(1, currency, 1, "test").Id.ShouldBe(currency);
    }

    /// <summary>The floor under the theory above (steering S3): an empty source would run zero cases and pass.</summary>
    [Fact]
    public void The_currency_theory_covers_the_whole_wallet()
    {
        Wallet.Length.ShouldBe(
            8,
            "10 §1 fixes eight wallet currencies, and Wallet is what the MemberData source is built from. " +
            "A source that shrank would run fewer cases and still report success.");
    }

    /// <summary>
    /// Value equality over all four components. The economy log (`14` §7.1) is append-only and the
    /// replay script (`14` §2.4) is ordered, so two credits of the same amount for the same reason
    /// are distinct rows distinguished by nothing but the ordinal.
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

    /// <summary>The `10` §1 wallet, read off the enum rather than transcribed.</summary>
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
