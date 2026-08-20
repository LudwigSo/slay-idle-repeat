using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The placeholder that gets a run off a tile whose own screen this build has not written — which
/// tiles those are, and what it submits for each.
/// </summary>
/// <remarks>
/// 🔒 Three of these cases are stated over the SHIPPED content rather than a fixture, and they are
/// the ones that matter: the whole placeholder rests on claims about authored data — that a minigame
/// id names a real reward table, and that every event card offers something free — and a fixture that
/// authored those claims itself would prove nothing about the build a player installs.
/// </remarks>
public sealed class UnbuiltTileScreensTests
{
    /// <summary>Where the event cards are read from, for the cases over shipped content.</summary>
    private const string BoardEventsFile = "board_events.json";

    /// <summary>The minigame reward tables, whose keys are the four <c>MG_*</c> ids.</summary>
    private const string CurrenciesFile = "currencies.json";

    [Fact]
    public void The_two_tiles_with_no_screen_are_the_event_and_the_minigame()
    {
        UnbuiltTileScreens.HasNoScreen((int)TileKind.Event).ShouldBeTrue();
        UnbuiltTileScreens.HasNoScreen((int)TileKind.Minigame).ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 Stated over EVERY OTHER kind rather than over a couple of examples, because the claim is
    /// an absence: a kind that quietly joined this table would have its tile skipped for the lowest
    /// reward it can pay instead of opening the screen it has, and no case about the two members
    /// above could ever notice.
    /// </summary>
    [Fact]
    public void No_other_tile_kind_is_skipped()
    {
        foreach (var kind in Enum.GetValues<TileKind>())
        {
            if (kind is TileKind.Event or TileKind.Minigame)
            {
                continue;
            }

            UnbuiltTileScreens.HasNoScreen((int)kind).ShouldBeFalse(
                $"{kind} either opens a screen this build has written or resolves in place through " +
                "RESOLVE_TILE, so skipping it would pay a placeholder reward over a tile that works.");
        }

        // And a number outside the vocabulary altogether, which is what a build one kind behind the
        // rules layer would be reading.
        UnbuiltTileScreens.HasNoScreen(BoardTileKinds.Count).ShouldBeFalse();
        UnbuiltTileScreens.HasNoScreen(BoardTileKinds.NoPendingTile).ShouldBeFalse();
    }

    [Fact]
    public void A_minigame_tile_is_left_by_MINIGAME_SUBMIT_at_the_lowest_tier()
    {
        var command = UnbuiltTileScreens.CommandThatLeaves(
            (int)TileKind.Minigame, drawnEventCardId: null, BoardContent.Strings());

        var submit = command.ShouldBeOfType<MinigameSubmitCommand>(
            "MINIGAME_SUBMIT is the only command that clears a minigame tile — RESOLVE_TILE " +
            "acknowledges it and clears nothing, which is the dead end this placeholder exists for.");

        submit.MinigameId.ShouldBe(UnbuiltTileScreens.PlaceholderMinigameId);
        submit.Result.ShouldBe(UnbuiltTileScreens.LowestOutcomeTier);
    }

    /// <summary>
    /// 🔒 An event tile answers with the DRAW while no card is drawn, and with the choice once one
    /// is. Both arms, because one alone cannot tell a decision from a constant — and the wrong arm
    /// is refused by the rules layer either way: <c>EVENT_CHOOSE</c> before the draw finds no card,
    /// and a second <c>RESOLVE_TILE</c> after it is refused so a client cannot re-draw a card it
    /// disliked.
    /// </summary>
    [Fact]
    public void An_event_tile_is_drawn_first_and_chosen_second()
    {
        UnbuiltTileScreens
            .CommandThatLeaves((int)TileKind.Event, drawnEventCardId: null, BoardContent.Strings())
            .ShouldBeOfType<ResolveTileCommand>();

        UnbuiltTileScreens
            .CommandThatLeaves((int)TileKind.Event, drawnEventCardId: "", BoardContent.Strings())
            .ShouldBeOfType<ResolveTileCommand>(
                "the row spells 'no card drawn' as the empty string, so an empty id is an absence " +
                "and not an id.");

        UnbuiltTileScreens
            .CommandThatLeaves(
                (int)TileKind.Event,
                "EVT_FIXTURE",
                BoardContent.AuthoringEventCard(1, "EVT_FIXTURE", optionsCost: [false, true]))
            .ShouldBeOfType<EventChooseCommand>();
    }

    [Fact]
    public void A_tile_that_has_a_screen_is_not_answered_with_a_command_at_all()
    {
        UnbuiltTileScreens
            .CommandThatLeaves((int)TileKind.Shop, drawnEventCardId: null, BoardContent.Strings())
            .ShouldBeNull();
    }

    /// <summary>
    /// 🔴 The claim the placeholder's reliability rests on: it takes the first option that costs
    /// nothing, not the first option. <c>EVENT_CHOOSE</c> refuses an option the player cannot afford,
    /// so a placeholder that always took index 0 would put a broke run straight back on the dead end.
    /// </summary>
    [Fact]
    public void The_first_cost_free_option_is_taken_even_when_a_priced_one_comes_first()
    {
        var content = BoardContent.AuthoringEventCard(
            1, "EVT_FIXTURE", optionsCost: [true, true, false, false]);

        UnbuiltTileScreens.FreeOptionIndexOf(content, "EVT_FIXTURE").ShouldBe(2);

        var freeFirst = BoardContent.AuthoringEventCard(1, "EVT_FIXTURE", optionsCost: [false, true]);

        UnbuiltTileScreens.FreeOptionIndexOf(freeFirst, "EVT_FIXTURE").ShouldBe(0);
    }

    /// <summary>
    /// A card the content set does not carry, and a card whose every option is priced, both fall
    /// back to the first option — which can be refused for funds, and is why the fallback is not the
    /// rule.
    /// </summary>
    [Fact]
    public void A_card_with_nothing_free_falls_back_to_the_first_option()
    {
        var allPriced = BoardContent.AuthoringEventCard(1, "EVT_FIXTURE", optionsCost: [true, true]);

        UnbuiltTileScreens.FreeOptionIndexOf(allPriced, "EVT_FIXTURE")
            .ShouldBe(UnbuiltTileScreens.FirstOption);

        UnbuiltTileScreens.FreeOptionIndexOf(allPriced, "EVT_NOT_IN_THIS_CONTENT_SET")
            .ShouldBe(UnbuiltTileScreens.FirstOption);

        UnbuiltTileScreens.FreeOptionIndexOf(BoardContent.Strings(), "EVT_FIXTURE")
            .ShouldBe(UnbuiltTileScreens.FirstOption);
    }

    /// <summary>
    /// 🔴 <b>Over the SHIPPED cards, because the fallback above is a hole and this is what keeps a
    /// player out of it.</b> Every one of `19` Part A's cards authors at least one option that
    /// charges nothing, so the skip is always affordable; a card authored later with a price on every
    /// option would make the skip refusable for a run with no Gold, and this is where that shows up.
    /// </summary>
    [Fact]
    public void Every_shipped_event_card_authors_an_option_that_costs_nothing()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoPaths.ContentDataRoot, "content", "board_events", BoardEventsFile)));

        var cards = document.RootElement.GetProperty("cards");

        cards.GetArrayLength().ShouldBeGreaterThan(0, "an empty catalogue would pass vacuously.");

        foreach (var card in cards.EnumerateArray())
        {
            var id = card.GetProperty("id").GetString()!;
            var options = card.GetProperty("options");
            var chosen = UnbuiltTileScreens.FreeOptionIndexOf(BootContent.Shipped, id);

            chosen.ShouldBeLessThan(options.GetArrayLength(), $"{id}: the index names no option.");

            options[chosen].TryGetProperty("cost", out _).ShouldBeFalse(
                $"{id}: the skip would submit a PRICED option, so EVENT_CHOOSE refuses it with " +
                "INSUFFICIENT_FUNDS for a run that cannot pay — and the run is back on the dead end " +
                "this placeholder removes. Either this card needs a free option, or the placeholder " +
                "needs a way off a tile it cannot afford.");
        }
    }

    /// <summary>
    /// 🔴 <b>The transcription's pin.</b> <c>MinigameCatalogue</c> is internal to the rules
    /// assembly, so the id is copied rather than referenced and nothing would notice it going stale
    /// — except that the reward tables are keyed by the same ids, and a submission naming a table
    /// that does not exist is refused. The tier is pinned in the same breath, because a tier outside
    /// the table is the other way the same submission is refused.
    /// </summary>
    [Fact]
    public void The_transcribed_minigame_id_names_a_shipped_reward_table_with_that_tier_in_it()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoPaths.ContentDataRoot, "tuning", CurrenciesFile)));

        document.RootElement.GetProperty("minigameRewards")
            .TryGetProperty(UnbuiltTileScreens.PlaceholderMinigameId, out var table)
            .ShouldBeTrue(
                $"'{UnbuiltTileScreens.PlaceholderMinigameId}' names no reward table, so " +
                "MINIGAME_SUBMIT refuses it as an unknown minigame and the tile stays pending.");

        UnbuiltTileScreens.LowestOutcomeTier.ShouldBeLessThan(
            table.GetArrayLength(),
            "a tier outside the authored table is refused, which leaves the tile pending.");
    }
}
