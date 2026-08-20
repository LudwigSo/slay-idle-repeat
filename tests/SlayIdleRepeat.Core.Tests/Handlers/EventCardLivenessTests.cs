using Shouldly;
using SlayIdleRepeat.Core.Content.BoardEvents;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
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
