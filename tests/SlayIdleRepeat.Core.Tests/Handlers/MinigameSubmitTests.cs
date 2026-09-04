using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// MINIGAME_SUBMIT: the legality gate (a valid outcome tier, exactly one submission per tile) and
/// the chapter-scaled reward application, over the production dispatch table.
/// </summary>
/// <remarks>
/// The four minigames have three different tier counts (3, 4, 3, 3), used as the probe shapes for
/// the legality checks rather than asserting the same boundary once for all four.
/// </remarks>
public sealed class MinigameSubmitTests
{
    [Fact]
    public void An_unknown_minigame_id_is_rejected()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand("MG_NOT_A_REAL_MINIGAME", 0), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.ShouldBeSameAs(state, "a rejected command changes nothing.");
    }

    /// <summary>
    /// Every authored tier accepted, so the boundary case below is refusing a specific value and not
    /// every request.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Every_authored_tier_of_MG_TIMING_BAR_is_accepted(int tier)
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, tier), Worlds.Context);

        result.Accepted.ShouldBeTrue();
    }

    [Fact]
    public void A_tier_one_past_MG_TIMING_BARs_four_rows_is_rejected()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 4), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.ShouldBeSameAs(state);
    }

    [Fact]
    public void A_negative_claimed_tier_is_rejected()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.MemoryRune, -1), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.ShouldBeSameAs(state);
    }

    /// <summary>A second row count, so the boundary check is proven against more than one.</summary>
    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void MG_MEMORY_RUNEs_three_row_boundary_is_exact(int tier, bool expectedAccepted)
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.MemoryRune, tier), Worlds.Context);

        result.Accepted.ShouldBe(expectedAccepted);
    }

    /// <remarks>The first submission's own legal tier is reused, so only the unchanged position distinguishes the calls.</remarks>
    [Fact]
    public void A_second_submission_at_the_same_position_is_rejected_as_a_duplicate()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var first = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 1), Worlds.Context);

        first.Accepted.ShouldBeTrue();

        var second = SlayIdleRepeat.Core.GameRules.Apply(
            first.NewState, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 1), Worlds.Context);

        second.Accepted.ShouldBeFalse();
        second.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        second.NewState.ShouldBeSameAs(first.NewState, "the rejected duplicate changes nothing further.");
    }

    [Fact]
    public void A_second_submission_at_a_different_position_is_accepted()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var first = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 1), Worlds.Context);

        first.Accepted.ShouldBeTrue();

        // Run.MoveTo is MovementEngine's alone to call, so a fresh snapshot at the new position is
        // the seam this test may use instead.
        var movedSnapshot = first.NewState.Run!.ToSnapshot() with { Position = 9 };
        var movedState = first.NewState with { Run = Run.Rehydrate(movedSnapshot).Value };

        var second = SlayIdleRepeat.Core.GameRules.Apply(
            movedState, new MinigameSubmitCommand(MinigameCatalogue.MemoryRune, 0), Worlds.Context);

        second.Accepted.ShouldBeTrue();
        second.NewState.Run!.ResolvedMinigames.Count.ShouldBe(2);
    }

    [Fact]
    public void An_accepted_submission_records_the_resolution_against_its_position()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 7));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.MemoryRune, 0), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.ResolvedMinigames[7].ShouldBe(MinigameCatalogue.MemoryRune);
    }

    /// <summary>No scaling at chapter 1: 1 + 0.35 × 0 = 1.</summary>
    [Fact]
    public void Chapter_1_pays_the_authored_base_reward_unscaled()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5, chapterId: 1, gold: 0));

        // MG_TIMING_BAR tier 2 ("2 hits"): 400 Gold + 30 Crowns.
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 2), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(400L);
        result.NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(30L);
    }

    /// <summary>1 + adBundleScalar × (chapter − 1), applied per nonzero column, rounded to the nearest integer.</summary>
    [Fact]
    public void A_later_chapter_scales_every_nonzero_column_by_the_formula()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5, chapterId: 3, gold: 0));

        // MG_TIMING_BAR tier 2: 400 Gold + 30 Crowns, chapter 3 gives scalar 1 + 0.35*2 = 1.7.
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 2), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(680L, "400 * 1.7 = 680.");
        result.NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(51L, "30 * 1.7 = 51.");
    }

    /// <summary>Not only Gold and Crowns — the cases above could not tell those apart from "every column is applied".</summary>
    [Fact]
    public void Beast_Feed_is_applied_when_the_row_grants_it()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5, chapterId: 1));

        // MG_MEMORY_RUNE tier 2 ("both rounds"): 550 Gold + 70 Crowns + 20 Beast Feed.
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.MemoryRune, 2), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.BalanceOf(CurrencyId.BEAST_FEED).ShouldBe(20L);
    }

    [Fact]
    public void Enhance_Stones_is_applied_when_the_row_grants_it()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5, chapterId: 1));

        // MG_TIMING_BAR tier 3 ("3 hits"): 600 Gold + 80 Crowns + 5 Enhance Stones.
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 3), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBe(5L);
    }

    /// <summary>A zero column would otherwise put a misleading attribution row on a currency the tier does not pay.</summary>
    [Fact]
    public void A_zero_reward_column_produces_no_event_for_that_currency()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5, chapterId: 1, gold: 0));

        // MG_TIMING_BAR tier 0 ("0 hits"): 100 Gold only — Crowns, Beast Feed and Enhance Stones are
        // all zero on this row.
        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 0), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.Events.OfType<CurrencyChanged>().Count().ShouldBe(
            1,
            "only GOLD moved on this row. Counted over the currency movements rather than over the " +
            "whole list, because an accepted submission also announces WHICH outcome it resolved to " +
            "— a count over everything would turn that announcement into a failure here and would " +
            "stop meaning 'one currency moved' the day any other event joins it.");
        result.Events.OfType<CurrencyChanged>().Single().Id.ShouldBe(CurrencyId.GOLD);
        result.NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(0L);
        result.NewState.Player.BalanceOf(CurrencyId.BEAST_FEED).ShouldBe(0L);
        result.NewState.Player.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBe(0L);
    }

    [Fact]
    public void MG_CHEST_PICKs_claimed_Result_is_ignored_even_when_nonsensical()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.ChestPick, 999_999), Worlds.Context);

        result.Accepted.ShouldBeTrue(
            "MG_CHEST_PICK is server-rolled, so the client's claim is never read as a tier.");
    }

    [Fact]
    public void MG_DICE_DUELs_claimed_Result_is_ignored_even_when_negative()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.DiceDuel, -7), Worlds.Context);

        result.Accepted.ShouldBeTrue();
    }

    /// <summary>The same 999_999 the server-rolled case shrugged off — refused where the claim is actually read.</summary>
    [Fact]
    public void A_client_asserted_minigames_out_of_range_Result_is_still_refused()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 999_999), Worlds.Context);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void A_server_rolled_resolution_moves_the_runs_minigame_stream_position()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.ChestPick, 0), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.StreamPosition(RngStreams.Minigame(0)).ShouldBeGreaterThan(0UL);
    }

    /// <summary>The claim is trusted once it is legal, so nothing is drawn.</summary>
    [Fact]
    public void A_client_asserted_resolution_draws_no_RNG_stream()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 1), Worlds.Context);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.RngStreamPositions.ShouldBeEmpty();
    }

    // ----------------------------------------------------------------------------------------------
    // The resolution event.
    // ----------------------------------------------------------------------------------------------

    /// <summary>
    /// 🔒 <b>An accepted submission says which outcome it resolved to.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The reward rows travel as <c>CurrencyChanged</c>, which says how much moved and never says
    /// which row it came from — so without this event a screen can only report what it claimed. The
    /// tier and its token are both pinned, and the token is asked of the reward table rather than
    /// transcribed: an event naming only the index describes a different outcome the moment a
    /// content edit reorders the table.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_client_asserted_resolution_reports_the_tier_it_was_given(int tier)
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5, chapterId: 1));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, tier), Worlds.Context);

        result.Accepted.ShouldBeTrue();

        var resolved = result.Events.OfType<MinigameResolved>().ShouldHaveSingleItem();

        resolved.MinigameId.ShouldBe(
            MinigameCatalogue.TimingBar,
            "the resolution names another minigame than the one submitted, so a screen keyed on the " +
            "id it opened would ignore its own answer.");
        resolved.Tier.ShouldBe(
            tier,
            "the claim was tier " + tier + " and the resolution reported " + resolved.Tier +
            ". On a client-asserted arm the claim IS the outcome once it is legal, so any other " +
            "number means the rows paid and the row named are different rows.");
        resolved.Outcome.ShouldBe(
            MinigameRewardTuning.Read(Worlds.Context.Content)
                                .OutcomeName(MinigameCatalogue.TimingBar, tier),
            "the token is the reward table's own for that row. Carried beside the tier rather than " +
            "left to be looked up, because the tier is an index a content edit can reorder.");
    }

    /// <summary>
    /// 🔒 <b>A server-rolled resolution names the tier the SERVER drew, not the one claimed.</b>
    /// </summary>
    /// <remarks>
    /// 🔴 The claim here is nonsense on purpose. No persisted field carries a rolled tier, so this
    /// event is the only thing that can tell a screen what it was paid — and an event that echoed
    /// the claim would tell the player they won 999999.
    /// </remarks>
    [Fact]
    public void A_server_rolled_resolution_reports_the_tier_the_server_drew()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5, chapterId: 1));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.ChestPick, 999_999), Worlds.Context);

        result.Accepted.ShouldBeTrue();

        var tuning = MinigameRewardTuning.Read(Worlds.Context.Content);
        var resolved = result.Events.OfType<MinigameResolved>().ShouldHaveSingleItem();

        resolved.MinigameId.ShouldBe(MinigameCatalogue.ChestPick);
        resolved.Tier.ShouldBeInRange(
            0,
            tuning.TierCount(MinigameCatalogue.ChestPick) - 1,
            "the resolution reported tier " + resolved.Tier + ", which the chest pick's table has no " +
            "row for — so it is the client's ignored claim coming back out rather than the draw.");
        resolved.Outcome.ShouldBe(
            tuning.OutcomeName(MinigameCatalogue.ChestPick, resolved.Tier),
            "the token and the tier name two different rows of one table, so the screen captions " +
            "what was paid with another outcome's words.");

        result.NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(
            tuning.RewardFor(MinigameCatalogue.ChestPick, resolved.Tier, 1).Crowns,
            "the Crowns paid are not the ones the reported tier's row authors, so the event names a " +
            "tier other than the one the rewards came from. Crowns rather than Gold: Gold is scaled " +
            "by the run's own modifiers at the income site and the table's figure is not what lands.");
    }

    /// <summary>…and a refused submission resolved nothing, so it says nothing.</summary>
    /// <remarks>
    /// 🔒 The negative control. An event emitted before the legality gate would have a screen
    /// reporting a win off a command the rules layer turned away.
    /// </remarks>
    [Fact]
    public void A_refused_submission_reports_no_resolution()
    {
        var state = Worlds.InARun(RunSnapshots.With(position: 5));

        var result = SlayIdleRepeat.Core.GameRules.Apply(
            state, new MinigameSubmitCommand(MinigameCatalogue.TimingBar, 999_999), Worlds.Context);

        result.Accepted.ShouldBeFalse(
            "with the submission accepted this case is measuring an acceptance rather than a " +
            "refusal, and the absence below says nothing.");
        result.Events.OfType<MinigameResolved>().ShouldBeEmpty(
            "the command was refused and something still announced a resolution, so a screen " +
            "watching for one would report a tier off a command the rules layer never applied.");
    }
}
