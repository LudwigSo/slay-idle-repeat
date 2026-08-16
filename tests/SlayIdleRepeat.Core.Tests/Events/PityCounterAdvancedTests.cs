using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// Every movement of a pity counter is emitted rather than inferred. This suite covers the event
/// itself: the three things it carries, the one thing it refuses, and the rendering the client and
/// the server have to agree on.
/// </summary>
/// <remarks>
/// <c>24</c> §1.1 Visibility makes every counter player-visible, and <c>14</c> §2.1 makes the client
/// a renderer of a number it was told. That reconciliation runs off this stream, so a movement with
/// no key is unattributable and a reset that produced no event would be indistinguishable from a
/// counter that never advanced.
/// </remarks>
public sealed class PityCounterAdvancedTests
{
    private const string ChestA = "chest.standard:A";

    /// <summary>The event carries which counter moved and what it now reads.</summary>
    [Fact]
    public void A_counter_movement_carries_the_counter_and_its_new_value()
    {
        var advanced = new PityCounterAdvanced(3, ChestA, 9);

        advanced.Sequence.ShouldBe(3);
        advanced.Key.ShouldBe(ChestA);
        advanced.Value.ShouldBe(9);
    }

    /// <summary>
    /// <c>Value</c> is the value <em>after</em> the movement, so a guarantee firing is a zero rather
    /// than a delta of minus a hundred and fifty-nine.
    /// </summary>
    [Fact]
    public void A_guarantee_firing_is_emitted_as_a_value_of_zero()
    {
        new PityCounterAdvanced(1, ChestA, 0).Value.ShouldBe(
            0,
            "24 §1 M1: 'counter resets to 0'. A reset that emitted no event, or emitted a delta, " +
            "would leave the client's counter and the server's column unable to reconcile.");
    }

    /// <summary>It reaches its consumers as a <c>DomainEvent</c>, ordered by the base's ordinal.</summary>
    [Fact]
    public void A_counter_movement_reaches_its_consumers_as_a_DomainEvent()
    {
        // The assignment IS the derivation claim — it stops compiling if the base type goes.
        DomainEvent asEvent = new PityCounterAdvanced(7, ChestA, 1);

        asEvent.Sequence.ShouldBe(
            7,
            "a mis-forwarded base constructor — ': DomainEvent(0)' — would give every consumer the " +
            "same ordinal while the derived record still read back correctly.");

        typeof(PityCounterAdvanced).GetProperty(nameof(DomainEvent.Sequence))!.DeclaringType.ShouldBe(
            typeof(DomainEvent),
            "consumers read Sequence off DomainEvent; a redeclared one on the subtype would shadow it");
    }

    /// <summary>The key is not optional; null, empty and whitespace are all refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void A_counter_movement_with_no_key_is_refused(string? key)
    {
        var thrown = Should.Throw<ArgumentException>(() => new PityCounterAdvanced(1, key!, 1));

        thrown.ParamName.ShouldBe("Key");

        // Pins which rule fired, not just that an ArgumentException was thrown — several guards in
        // Core throw one, and only this one is this rule's.
        thrown.Message.ShouldContain("LuckTuning", Case.Sensitive);
        thrown.Message.ShouldContain("counterKey", Case.Sensitive);
    }

    /// <summary>A key is kept exactly as given — it is an authored id, not display text.</summary>
    [Fact]
    public void A_key_is_kept_exactly_as_given()
    {
        new PityCounterAdvanced(1, " chest.standard:A ", 1).Key.ShouldBe(" chest.standard:A ");
    }

    /// <summary>
    /// The guard survives a <c>with</c> copy, which is what a get-only property buys over the
    /// positional <c>init</c> one.
    /// </summary>
    /// <remarks>
    /// An <c>init</c> accessor is assignable through <c>with</c> without re-running the property
    /// initialiser, so <c>event with { Key = "" }</c> would otherwise produce an unattributable
    /// movement through a validated type.
    /// </remarks>
    [Fact]
    public void A_copy_carries_the_validated_key_rather_than_re_opening_it()
    {
        var advanced = new PityCounterAdvanced(1, ChestA, 9);

        (advanced with { Sequence = 2 }).Key.ShouldBe(ChestA);
        (advanced with { Value = 0 }).Key.ShouldBe(ChestA);
    }

    /// <summary>Value equality over all three components.</summary>
    /// <remarks>
    /// The stream is append-only and ordered, so two advances of the same counter to the same value
    /// are distinct rows distinguished by nothing but the ordinal.
    /// </remarks>
    [Fact]
    public void Two_counter_movements_are_equal_only_when_every_component_matches()
    {
        var first = new PityCounterAdvanced(4, ChestA, 9);

        first.ShouldBe(new PityCounterAdvanced(4, ChestA, 9));

        first.ShouldNotBe(new PityCounterAdvanced(5, ChestA, 9));
        first.ShouldNotBe(new PityCounterAdvanced(4, "chest.standard:S", 9));
        first.ShouldNotBe(new PityCounterAdvanced(4, ChestA, 10));
    }

    /// <summary>
    /// The whole rendering: the base's ordinal first, then the key, then the value — under an
    /// arbitrary culture as well as under the invariant one.
    /// </summary>
    /// <remarks>
    /// ⚠️ The culture half of this cannot fail on this payload and is not claimed to: a non-negative
    /// <see cref="int"/> renders without a group separator or a sign under every culture .NET ships,
    /// so there is no <c>sv-SE</c>-style discriminator here of the kind <c>CurrencyChanged.Delta</c>
    /// has. What the case really pins is the rendered <em>shape</em> — the member set and its order —
    /// which is what an added or renamed payload member would move. The invariant-formatting
    /// obligation itself is enforced by <c>AmbientApiTests</c>, in IL, where it is decidable.
    /// </remarks>
    [Fact]
    public void ToString_renders_the_ordinal_the_key_and_the_value_in_that_order()
    {
        var advanced = new PityCounterAdvanced(2, ChestA, 1234567);

        Render(advanced, new CultureInfo("de-DE")).ShouldBe(Render(advanced, CultureInfo.InvariantCulture));
        Render(advanced, CultureInfo.InvariantCulture).ShouldBe(
            "PityCounterAdvanced { Sequence = 2, Key = chest.standard:A, Value = 1234567 }");
    }

    private static string Render(PityCounterAdvanced advanced, CultureInfo culture)
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = culture;
            return advanced.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
