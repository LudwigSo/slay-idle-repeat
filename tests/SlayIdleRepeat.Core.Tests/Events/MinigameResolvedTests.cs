using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Events;

/// <summary>
/// The minigame-resolution event: the two things it refuses, and the rendering it is read through.
/// What a tier MEANS is <c>MinigameSubmitTests</c>' to pin.
/// </summary>
/// <remarks>
/// 🔴 This event is the only honest way a client learns a server-rolled tier — two of the four
/// minigames draw their outcome on the server and no persisted field records the draw — so an event
/// that could carry a blank id or a blank token would reach a screen with nothing to show.
/// </remarks>
public sealed class MinigameResolvedTests
{
    private const string ChestPick = "MG_CHEST_PICK";

    private const string GoldTier = "GOLD";

    [Fact]
    public void A_resolution_reaches_its_consumers_as_a_DomainEvent()
    {
        // The assignment IS the derivation claim — it stops compiling if the base type goes.
        DomainEvent asEvent = new MinigameResolved(7, ChestPick, 2, GoldTier);

        asEvent.Sequence.ShouldBe(
            7,
            "a mis-forwarded base constructor — ': DomainEvent(0)' — would give every consumer the " +
            "same ordinal while the derived record still read back correctly.");
    }

    /// <summary>A resolution naming no minigame cannot be attributed to the tile that paid it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void A_resolution_with_no_minigame_id_is_refused(string? minigameId)
    {
        var thrown = Should.Throw<ArgumentException>(
            () => new MinigameResolved(1, minigameId!, 0, GoldTier));

        thrown.ParamName.ShouldBe("MinigameId");

        // Pins which rule fired rather than only that an ArgumentException was thrown — several
        // guards in Core throw one, and only this one is this rule's.
        thrown.Message.ShouldContain("MINIGAME_SUBMIT", Case.Sensitive);
    }

    /// <summary>
    /// …and one naming no outcome token describes a tier index into a table that content can reorder.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void A_resolution_with_no_outcome_token_is_refused(string? outcome)
    {
        var thrown = Should.Throw<ArgumentException>(
            () => new MinigameResolved(1, ChestPick, 2, outcome!));

        thrown.ParamName.ShouldBe("Outcome");
        thrown.Message.ShouldContain("outcome token", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 The two guards say different things, so a reader can tell which half was missing.
    /// </summary>
    /// <remarks>
    /// 🔴 Both cases above pass against one shared message. The id and the token are different
    /// failures with different causes — a handler that lost the catalogue, and one that lost the
    /// reward table — and one sentence for both sends an operator to the wrong document.
    /// </remarks>
    [Fact]
    public void The_two_guards_do_not_share_a_sentence()
    {
        var blankId = Should.Throw<ArgumentException>(
            () => new MinigameResolved(1, "", 0, GoldTier)).Message;
        var blankOutcome = Should.Throw<ArgumentException>(
            () => new MinigameResolved(1, ChestPick, 0, "")).Message;

        blankId.ShouldNotBeNullOrWhiteSpace();
        blankOutcome.ShouldNotBe(
            blankId,
            "the two refusals are authored as one sentence, so a log line saying a resolution was " +
            "unattributable never says which half of it was missing.");
    }

    /// <summary>
    /// 🔒 Every member is rendered through the invariant culture, per member and by hand.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The tier is rendered NEGATIVE here on purpose.</b> A record's synthesized
    /// <c>PrintMembers</c> formats through <c>StringBuilder.Append(object)</c> in the ambient
    /// culture, and boxing hides that from the IL scan — so the claim needs a value two cultures
    /// disagree about, and a non-negative integer is not one. Under <c>sv-SE</c> a negative integer
    /// renders with U+2212 MINUS SIGN rather than U+002D, which is the whole discriminator.
    /// The payload puts no guard on the tier, so this is a legal construction rather than a
    /// contrived one.
    /// </remarks>
    [Fact]
    public void ToString_renders_identically_under_any_culture()
    {
        var swedish = new CultureInfo("sv-SE");

        (-1).ToString(swedish).ShouldNotBe(
            (-1).ToString(CultureInfo.InvariantCulture),
            "under globalization-invariant mode new CultureInfo(\"sv-SE\") silently returns the " +
            "invariant culture, and the comparison below would then hold over nothing.");

        var resolved = new MinigameResolved(2, ChestPick, -1, GoldTier);

        Render(resolved, swedish).ShouldBe(Render(resolved, CultureInfo.InvariantCulture));

        Render(resolved, CultureInfo.InvariantCulture).ShouldContain(
            "Sequence = 2, MinigameId = " + ChestPick + ", Tier = -1, Outcome = " + GoldTier,
            Case.Sensitive,
            "the members are rendered in declaration order, each through the invariant culture, " +
            "with the base's Sequence first — the shape every other event in this hierarchy has.");

        Render(resolved, swedish).ShouldNotContain(
            "−",
            Case.Sensitive,
            "U+2212 MINUS SIGN is what sv-SE renders a negative integer with. If it appears here " +
            "the hand-written PrintMembers is gone and the synthesized one is back.");
    }

    private static string Render(MinigameResolved evt, CultureInfo culture)
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
