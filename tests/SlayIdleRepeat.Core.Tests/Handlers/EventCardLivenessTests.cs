using Shouldly;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔴 Every authored event card offers a way out that costs nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one liveness property `19` Part A carries and nothing enforces.</b> An event tile is
/// resolved by <c>EVENT_CHOOSE</c> and by nothing else, and every option a player cannot pay is
/// answered <c>INSUFFICIENT_FUNDS</c> — so a card whose options ALL carried a cost would strand a
/// broke run on the tile with no legal move. Not hypothetically: `03` §5's own worked example,
/// <c>EVT_WELL</c>, has two costed options and one "walk away", and the walk-away is what makes it
/// safe. Nothing in the schema says so.
/// </para>
/// <para>
/// Stated over the SHIPPED catalogue rather than a fixture, because the claim is about the cards
/// this game ships. A thirty-first card authored with a cost on every option fails here, on the
/// commit that adds it, rather than in a player's run.
/// </para>
/// <para>
/// ⚠️ The check is "costs nothing", not "is affordable": a free option is affordable to every run
/// by construction, and an option costing 1 Gold is not a way out for a run holding none.
/// </para>
/// </remarks>
public sealed class EventCardLivenessTests
{
    [Fact]
    public void Every_authored_event_card_offers_at_least_one_costless_option()
    {
        var catalogue = EventCatalogue.Read(ShippedHarness.Content);

        var trapped = catalogue.All
            .Where(card => card.Options.All(option => option.CostCurrency is not null))
            .Select(card => card.Id)
            .ToArray();

        trapped.ShouldBeEmpty(
            "an event card whose every option costs something strands a run that cannot pay: " +
            "EVENT_CHOOSE is the only command that clears the tile, and every option is answered " +
            "INSUFFICIENT_FUNDS. The card needs a walk-away, the way 03 §5's own EVT_WELL has one.");
    }

    /// <summary>
    /// The floor, so the emptiness above is not a claim about an empty catalogue.
    /// </summary>
    /// <remarks>
    /// And a second arm that makes it bite: at least one card must actually CARRY a cost, or the
    /// case above is satisfied by a catalogue where nothing costs anything and the property it
    /// guards has never been exercised.
    /// </remarks>
    /// <summary>
    /// 🔒 <b>No card offers to reveal tiles, and none will.</b> Three options did — every one of
    /// them the sole outcome of its option — and all three are DELETED rather than deferred: the board
    /// is completely visible at all times (`16` D42), so what they offered cannot exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Stated over the note TEXT, which is unusual here and is the point.</b> An
    /// <c>UNSUPPORTED</c> effect's only content IS its note, so there is nothing else to assert
    /// against — and the failure this guards is a reader deciding the outcome was merely deferred and
    /// authoring it back. A deleted option leaves no trace for a structural check to find.
    /// </para>
    /// <para>
    /// 🔒 The three cards are named and their option counts floored, so re-adding an option to one is
    /// a failure even if it is worded so as not to say "reveal". `19` Part A's table still lists three
    /// options for each, and this is the record that the shipped data deliberately holds two.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_card_offers_to_reveal_tiles_and_the_three_that_did_are_two_options_short()
    {
        var catalogue = EventCatalogue.Read(ShippedHarness.Content);

        var reveals = catalogue.All
            .SelectMany(card => card.Options.SelectMany(option => option.Outcomes)
                                    .SelectMany(outcome => outcome.Effects)
                                    .Select(effect => (card.Id, effect.Note)))
            .Where(row => row.Note is not null &&
                          (row.Note.Contains("reveal", StringComparison.OrdinalIgnoreCase) ||
                           row.Note.Contains("tile preview", StringComparison.OrdinalIgnoreCase)))
            .Select(row => row.Id)
            .ToArray();

        reveals.ShouldBeEmpty(
            "an outcome that reveals tiles or widens a preview is offering something the board " +
            "already gives away for free (16 D42). It was deleted, not deferred, so this is a " +
            "re-authored option rather than a leftover.");

        foreach (var id in ShorterCards)
        {
            catalogue.All.Single(card => card.Id == id).Options.Count.ShouldBe(
                2,
                id + " offers two options in the shipped data where 19 Part A's table lists three. " +
                "The third was its tile-reveal option and it is not coming back — a card listing an " +
                "option that pays nothing asks the player to make a choice that is not one.");
        }
    }

    /// <summary>The three cards `19` Part A lists three options for and the data authors two of.</summary>
    private static readonly string[] ShorterCards =
        ["EVT_OLD_SOLDIER", "EVT_ARCHIVE", "EVT_LAST_LAMP"];

    /// <summary>
    /// 🔒 <b>The three outcomes that grant fixed dice actually grant them.</b> All three were
    /// <c>UNSUPPORTED</c> reroll-charge grants, which reads as DEFERRED — and they stayed that way for
    /// a whole milestone after the mechanic that replaced the reroll was built. Pinned over the
    /// SHIPPED catalogue so a future edit that re-defers one is a failure rather than a silent loss of
    /// three of the card pool's few payable rewards.
    /// </summary>
    /// <remarks>
    /// Counted, not located: which cards carry them is `19` Part A's business and moving one between
    /// cards is a content decision. That three exist and pay is this task's claim.
    /// </remarks>
    [Fact]
    public void The_three_authored_fixed_die_grants_are_payable_rather_than_deferred()
    {
        var catalogue = EventCatalogue.Read(ShippedHarness.Content);

        var grants = catalogue.All
            .SelectMany(card => card.Options)
            .SelectMany(option => option.Outcomes)
            .SelectMany(outcome => outcome.Effects)
            .Where(effect => effect.Op == EventEffectOp.FixedDie)
            .ToArray();

        grants.Length.ShouldBe(
            3, "19 Part A authors three reroll-charge grants, and all three are fixed dice now.");

        grants.Sum(effect => effect.FixedDice!.Value).ShouldBe(
            4, "+2, +1 and +1 — the authored amounts, carried across rather than flattened to one each.");
    }

    [Fact]
    public void The_catalogue_is_populated_and_some_of_it_costs_something()
    {
        var catalogue = EventCatalogue.Read(ShippedHarness.Content);

        catalogue.All.Count.ShouldBe(30, "19 Part A authors thirty cards.");
        catalogue.All.Any(card => card.Options.Any(option => option.CostCurrency != null))
            .ShouldBeTrue(
                "no authored option costs anything, so the case above is asserting over a catalogue " +
                "where the property it guards cannot be violated.");
    }
}
