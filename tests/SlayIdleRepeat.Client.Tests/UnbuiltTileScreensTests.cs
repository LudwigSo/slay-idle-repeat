using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Rules.Board;
using Xunit;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The placeholder that gets a run off the one tile whose own screen this build has not written —
/// which tile that is, and what it submits for it.
/// </summary>
/// <remarks>
/// 🔒 The last case is stated over the SHIPPED content rather than a fixture, and it is the one that
/// matters: the placeholder rests on a claim about authored data — that a minigame id names a real
/// reward table with the claimed tier in it — and a fixture that authored that claim itself would
/// prove nothing about the build a player installs.
/// <para>
/// 🔴 The Event tile's half of this file is gone with the Event arms of the placeholder. It has a
/// screen now, so its cases live in <c>EventPresenterTests</c> and <c>EventTileContractTests</c> —
/// and the claim that every shipped card offers something free is now
/// <c>EventTileContractTests</c>', asked of <c>GameRules.Apply</c>'s real behaviour rather than of a
/// second reader over the same document.
/// </para>
/// </remarks>
public sealed class UnbuiltTileScreensTests
{
    /// <summary>The minigame reward tables, whose keys are the four <c>MG_*</c> ids.</summary>
    private const string CurrenciesFile = "currencies.json";

    [Fact]
    public void The_one_tile_with_no_screen_is_the_minigame() =>
        UnbuiltTileScreens.HasNoScreen((int)TileKind.Minigame).ShouldBeTrue(
            "the minigame is the last tile kind with no screen of its own, and the placeholder is " +
            "the only thing that gets a run off it.");

    /// <summary>
    /// 🔒 Stated over EVERY OTHER kind rather than over a couple of examples, because the claim is
    /// an absence: a kind that quietly joined this table would have its tile skipped for the lowest
    /// reward it can pay instead of opening the screen it has, and no case about the one member
    /// above could ever notice.
    /// </summary>
    /// <remarks>
    /// 🔴 The Event tile is now one of the kinds swept here, which is the point of the sweep: it has
    /// a screen, so a placeholder still claiming it would resolve a card the player never read.
    /// </remarks>
    [Fact]
    public void No_other_tile_kind_is_skipped()
    {
        foreach (var kind in Enum.GetValues<TileKind>())
        {
            if (kind is TileKind.Minigame)
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
        var command = UnbuiltTileScreens.CommandThatLeaves((int)TileKind.Minigame);

        var submit = command.ShouldBeOfType<MinigameSubmitCommand>(
            "MINIGAME_SUBMIT is the only command that clears a minigame tile — RESOLVE_TILE " +
            "acknowledges it and clears nothing, which is the dead end this placeholder exists for.");

        submit.MinigameId.ShouldBe(UnbuiltTileScreens.PlaceholderMinigameId);
        submit.Result.ShouldBe(UnbuiltTileScreens.LowestOutcomeTier);
    }

    [Fact]
    public void A_tile_that_has_a_screen_is_not_answered_with_a_command_at_all() =>
        UnbuiltTileScreens.CommandThatLeaves((int)TileKind.Shop).ShouldBeNull();

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
