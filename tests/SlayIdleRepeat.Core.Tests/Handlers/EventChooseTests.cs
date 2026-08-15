using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// 🔒 M3-03, `03` §5 / `19` Part A — <c>EVENT_CHOOSE</c>, driven through the production dispatch
/// table by <c>GameRules.Apply</c>.
/// </summary>
public sealed class EventChooseTests
{
    private static CommandResult Choose(WorldSlice state, int choiceIndex) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new EventChooseCommand(choiceIndex), TileWorlds.Context);

    private static WorldSlice OnCard(string cardId, long gold = 0L, int currentHp = 100, int chapterId = 1) =>
        TileWorlds.OnTile(
            TileKind.Event, chapterId: chapterId, gold: gold, currentHp: currentHp, eventCardId: cardId);

    // ------------------------------------------------------------------ the gate

    /// <summary>A run standing on no tile has no card to choose from.</summary>
    [Fact]
    public void A_run_with_no_pending_tile_is_rejected()
    {
        var result = Choose(TileWorlds.OnNoTile(), 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A pending tile that is not an event tile has no card either.</summary>
    [Theory]
    [InlineData((int)TileKind.Campfire)]
    [InlineData((int)TileKind.Treasure)]
    [InlineData((int)TileKind.Empty)]
    public void A_pending_tile_of_another_kind_is_rejected(int kind)
    {
        var result = Choose(TileWorlds.OnTile((TileKind)kind), 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// 🔒 An event tile whose card has NOT been drawn yet is rejected — `03` §5's two halves run in
    /// order, and choosing before RESOLVE_TILE has drawn is choosing from nothing.
    /// </summary>
    [Fact]
    public void An_event_tile_with_no_drawn_card_is_rejected()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Event), 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>An index naming no option is refused rather than throwing.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(1000)]
    public void An_out_of_range_choice_index_is_rejected(int choiceIndex)
    {
        // EVT_FIXTURE_GOLD offers exactly one option, so every index but 0 is out of range.
        var result = Choose(OnCard(FixtureCards.Gold), choiceIndex);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>🔒 P4 — a rejected choice leaves the run's card and balances untouched.</summary>
    [Fact]
    public void A_rejection_leaves_the_card_pending_and_the_gold_untouched()
    {
        var state = OnCard(FixtureCards.Gold, gold: 500);

        var result = Choose(state, 7);

        result.NewState.ShouldBeSameAs(state);
        result.NewState.Run!.Gold.ShouldBe(500);
        result.NewState.Run!.ToSnapshot().PendingEventCardId.ShouldBe(FixtureCards.Gold);
    }

    // ------------------------------------------------------------------ currency effects

    /// <summary>🔒 A flat GOLD grant moves on the RUN — `10` §1's one run-scoped currency.</summary>
    [Fact]
    public void A_gold_grant_moves_on_the_run()
    {
        var result = Choose(OnCard(FixtureCards.Gold, gold: 20), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(120);

        var paid = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>();
        paid.Id.ShouldBe(CurrencyId.GOLD);
        paid.Delta.ShouldBe(100);
        paid.Reason.ShouldBe("event_card_outcome");
    }

    /// <summary>🔒 …and a wallet grant moves on the PLAYER.</summary>
    [Fact]
    public void A_crowns_grant_moves_on_the_player()
    {
        var state = OnCard(FixtureCards.Crowns);
        var before = state.Player.BalanceOf(CurrencyId.CROWNS);

        var result = Choose(state, 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(before + 40);
        result.NewState.Run!.Gold.ShouldBe(0, "a wallet grant never touches the run's Gold");
    }

    /// <summary>🔒 A <c>chapterScaled</c> amount is multiplied by <c>M(c)</c>.</summary>
    [Theory]
    [InlineData(1, 40L)]
    [InlineData(3, 80L)]
    [InlineData(8, 320L)]
    public void A_chapter_scaled_grant_is_multiplied_by_the_meta_scalar(int chapterId, long expected)
    {
        var state = OnCard(FixtureCards.Crowns, chapterId: chapterId);
        var before = state.Player.BalanceOf(CurrencyId.CROWNS);

        Choose(state, 0).NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(before + expected);
    }

    /// <summary>🔒 …and a flat one is NOT — the GOLD convention `19` Part A's transcription records.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void A_flat_grant_is_the_same_in_every_chapter(int chapterId)
    {
        Choose(OnCard(FixtureCards.Gold, chapterId: chapterId), 0)
            .NewState.Run!.Gold.ShouldBe(100);
    }

    // ------------------------------------------------------------------ costs

    /// <summary>🔒 A cost is charged before the outcome pays, and both rows are reported in order.</summary>
    [Fact]
    public void An_options_cost_is_charged_and_reported_before_its_payout()
    {
        var state = OnCard(FixtureCards.Costly, gold: 300);
        var crownsBefore = state.Player.BalanceOf(CurrencyId.CROWNS);

        var result = Choose(state, 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(200);
        result.NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(crownsBefore + 40);

        result.Events.Count.ShouldBe(2);
        var cost = result.Events[0].ShouldBeOfType<CurrencyChanged>();
        cost.Delta.ShouldBe(-100);
        cost.Reason.ShouldBe("event_choice_cost", "a cost is attributed apart from a payout (21 §8.3)");
        result.Events[1].ShouldBeOfType<CurrencyChanged>().Delta.ShouldBe(40);
    }

    /// <summary>
    /// 🔒 An unaffordable cost is REFUSED, not thrown — the player asked for something legal that
    /// they cannot afford.
    /// </summary>
    [Fact]
    public void An_unaffordable_option_is_rejected_as_insufficient_funds()
    {
        var result = Choose(OnCard(FixtureCards.Costly, gold: 99), 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
    }

    /// <summary>🔒 …and nothing is spent, drawn or cleared by the refusal.</summary>
    [Fact]
    public void An_unaffordable_option_spends_nothing_and_keeps_the_card()
    {
        var state = OnCard(FixtureCards.Costly, gold: 99);

        var result = Choose(state, 0);

        result.NewState.Run!.Gold.ShouldBe(99);
        result.NewState.Run!.ToSnapshot().PendingEventCardId.ShouldBe(FixtureCards.Costly);
        result.NewState.Run!.RngStreamPositions.ShouldBeEmpty(
            "a refused command draws nothing, so no 14 §8.1 counter moves");
    }

    /// <summary>…and the boundary: exactly enough IS enough.</summary>
    [Fact]
    public void Exactly_enough_gold_affords_the_option()
    {
        var result = Choose(OnCard(FixtureCards.Costly, gold: 100), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(0);
    }

    /// <summary>🔒 A META wallet cost is checked against the PLAYER's balance, not the run's Gold.</summary>
    [Fact]
    public void A_wallet_cost_is_checked_against_the_players_balance()
    {
        var state = OnCard(FixtureCards.CostlyMeta, gold: 100_000);

        var result = Choose(state, 0);

        if (state.Player.BalanceOf(CurrencyId.ENHANCE_STONES) >= 6)
        {
            result.Accepted.ShouldBeTrue();
            result.NewState.Player.BalanceOf(CurrencyId.ENHANCE_STONES)
                .ShouldBe(state.Player.BalanceOf(CurrencyId.ENHANCE_STONES) - 6);
        }
        else
        {
            result.Rejection.ShouldBe(
                RejectionReason.INSUFFICIENT_FUNDS,
                "a wallet cost is not affordable out of the run's Gold, however much of it there is");
        }
    }

    /// <summary>A free option costs nothing and reports no cost row.</summary>
    [Fact]
    public void A_free_option_reports_no_cost_row()
    {
        var result = Choose(OnCard(FixtureCards.Costly, gold: 500), 1);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(500);
        result.Events.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ HP effects

    /// <summary>An <c>HP_PCT</c> heal restores a share of Max HP.</summary>
    [Fact]
    public void An_hp_percent_heal_restores_a_share_of_max_hp()
    {
        var result = Choose(OnCard(FixtureCards.Vitals, currentHp: 50), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(75);
    }

    /// <summary>🔒 …and it is CLAMPED at Max HP rather than overhealing.</summary>
    [Fact]
    public void An_hp_percent_heal_is_clamped_at_max_hp()
    {
        var result = Choose(OnCard(FixtureCards.Vitals, currentHp: 90), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(100);
    }

    /// <summary>A negative <c>HP_PCT</c> costs a share of Max HP.</summary>
    [Fact]
    public void A_negative_hp_percent_costs_a_share_of_max_hp()
    {
        Choose(OnCard(FixtureCards.Vitals, currentHp: 50), 1)
            .NewState.Run!.CurrentHp.ShouldBe(40);
    }

    /// <summary>
    /// 🔒 …and it is clamped at ZERO rather than going negative — `02` §6's revive acts on a hero
    /// standing at zero, so zero is a legal state and the floor.
    /// </summary>
    [Fact]
    public void A_negative_hp_percent_is_clamped_at_zero()
    {
        var result = Choose(OnCard(FixtureCards.Vitals, currentHp: 30), 2);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(0);
    }

    /// <summary>An HP change produces no event — there is no HP domain event in this game.</summary>
    [Fact]
    public void An_hp_change_produces_no_event()
    {
        Choose(OnCard(FixtureCards.Vitals, currentHp: 50), 0).Events.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ the other three ops

    /// <summary>
    /// 🔒 A <c>CURSE_REWARD</c> pays the curse's `19` Part E reward — through the SAME table the
    /// curse tile pays from.
    /// </summary>
    [Fact]
    public void A_curse_reward_effect_pays_the_authored_reward()
    {
        var state = OnCard(FixtureCards.Curse);
        var before = state.Player.BalanceOf(CurrencyId.ENHANCE_STONES);

        var result = Choose(state, 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBe(before + 2);
    }

    /// <summary>
    /// 🔒 …and it does NOT persist the curse, which is the whole limit M3-11 owns: nothing on the run
    /// records that a curse was taken.
    /// </summary>
    [Fact]
    public void A_curse_reward_effect_persists_no_curse()
    {
        var result = Choose(OnCard(FixtureCards.Curse), 0);

        // The run round-trips to a snapshot identical to one that took no curse at all, save for the
        // cleared pending tile — because there is nowhere in RunSnapshot for a curse to live.
        var snapshot = result.NewState.Run!.ToSnapshot();
        snapshot.PendingTileKind.ShouldBe(-1);
        snapshot.PendingEventCardId.ShouldBe("");
    }

    /// <summary>A <c>NONE</c> effect moves nothing and still resolves the card.</summary>
    [Fact]
    public void A_none_effect_resolves_the_card_and_moves_nothing()
    {
        var state = OnCard(FixtureCards.Inert, gold: 250, currentHp: 60);

        var result = Choose(state, 0);

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
        result.NewState.Run!.Gold.ShouldBe(250);
        result.NewState.Run!.CurrentHp.ShouldBe(60);
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }

    /// <summary>
    /// 🔒 …and so does an <c>UNSUPPORTED</c> one. ⚠️ That is CORRECT rather than a missing branch: the
    /// effect names a mechanic Core cannot execute, and inventing a substitute payout would be
    /// indistinguishable downstream from a real reward.
    /// </summary>
    [Fact]
    public void An_unsupported_effect_moves_nothing_and_still_resolves_the_card()
    {
        var state = OnCard(FixtureCards.Inert, gold: 250, currentHp: 60);

        var result = Choose(state, 1);

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
        result.NewState.Run!.Gold.ShouldBe(250);
        result.NewState.Run!.CurrentHp.ShouldBe(60);
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }

    // ------------------------------------------------------------------ the weighted draw

    /// <summary>🔒 The outcome draw is deterministic for a fixed seed.</summary>
    [Fact]
    public void The_outcome_draw_is_deterministic_for_a_fixed_seed()
    {
        var first = Choose(OnCard(FixtureCards.Split, currentHp: 100), 0);
        var second = Choose(OnCard(FixtureCards.Split, currentHp: 100), 0);

        first.Events.Cast<CurrencyChanged>().Select(e => (e.Id, e.Delta))
            .ShouldBe(second.Events.Cast<CurrencyChanged>().Select(e => (e.Id, e.Delta)));
    }

    /// <summary>
    /// 🔒 …and BOTH branches of a 70/30 split are reachable, so the walk is a real weighted pick
    /// rather than a constant.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the mutation probe's target: shifting the cumulative walk's boundary, or pinning it
    /// to one end, collapses this to a single currency across every seed.
    /// </remarks>
    [Fact]
    public void Both_branches_of_a_weighted_split_are_reachable()
    {
        var currencies = Enumerable.Range(1, 40)
            .Select(seed => TileWorlds.OnTile(
                TileKind.Event, eventCardId: FixtureCards.Split, runSeed: (ulong)seed))
            .Select(state => Choose(state, 0))
            .SelectMany(r => r.Events.Cast<CurrencyChanged>())
            .Select(e => e.Id)
            .Distinct()
            .ToArray();

        currencies.ShouldContain(CurrencyId.ENHANCE_STONES, "the 70 branch");
        currencies.ShouldContain(CurrencyId.MERGE_DUST, "the 30 branch");
    }

    /// <summary>
    /// 🔒 …and the heavier branch really is heavier. Not an exact ratio — this is 40 samples, not a
    /// distribution test — but a strict majority, which a 70/30 split gives and a 50/50 or an
    /// inverted walk would not.
    /// </summary>
    [Fact]
    public void The_heavier_branch_of_a_seventy_thirty_split_wins_the_majority()
    {
        var draws = Enumerable.Range(1, 40)
            .Select(seed => TileWorlds.OnTile(
                TileKind.Event, eventCardId: FixtureCards.Split, runSeed: (ulong)seed))
            .Select(state => Choose(state, 0))
            .Select(r => r.Events.Cast<CurrencyChanged>().Single().Id)
            .ToArray();

        draws.Count(id => id == CurrencyId.ENHANCE_STONES)
            .ShouldBeGreaterThan(draws.Length / 2, "the 70-weighted branch is the majority");
    }

    /// <summary>🔒 A guaranteed effect applies on EVERY branch of the split — the flattened cost.</summary>
    /// <remarks>
    /// `19` Part A's "Reach in (−10% HP) → 70% / 30%" is modelled as two outcomes that each repeat
    /// the HP cost, rather than as a nested layer. This is what says that repetition is real.
    /// </remarks>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    public void A_guaranteed_effect_applies_on_every_branch(ulong seed)
    {
        var state = TileWorlds.OnTile(
            TileKind.Event, currentHp: 100, eventCardId: FixtureCards.Split, runSeed: seed);

        Choose(state, 0).NewState.Run!.CurrentHp.ShouldBe(90);
    }

    // ------------------------------------------------------------------ the round trip

    /// <summary>
    /// 🔒 The full `03` §5 loop: arrive at an event tile → <c>RESOLVE_TILE</c> draws →
    /// <c>EVENT_CHOOSE</c> resolves → the tile is cleared and cannot be chosen again.
    /// </summary>
    [Fact]
    public void An_event_resolves_across_two_commands_and_then_cannot_be_chosen_again()
    {
        var arrived = TileWorlds.OnTile(TileKind.Event, gold: 500);

        var drawn = SlayIdleRepeat.Core.GameRules.Apply(
            arrived, new ResolveTileCommand(), TileWorlds.Context);

        drawn.Accepted.ShouldBeTrue();
        var cardId = drawn.NewState.Run!.ToSnapshot().PendingEventCardId;
        cardId.ShouldNotBeNullOrEmpty();

        var chosen = Choose(drawn.NewState, 0);

        chosen.Accepted.ShouldBeTrue();
        chosen.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
        chosen.NewState.Run!.ToSnapshot().PendingEventCardId.ShouldBe("");

        var again = Choose(chosen.NewState, 0);
        again.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// 🔒 …and the whole loop is deterministic for a fixed seed: the same card, the same outcome, the
    /// same balances.
    /// </summary>
    [Fact]
    public void The_whole_event_loop_is_deterministic_for_a_fixed_seed()
    {
        static (string Card, long Gold, int Hp, IReadOnlyList<long> Deltas) Loop()
        {
            var drawn = SlayIdleRepeat.Core.GameRules.Apply(
                TileWorlds.OnTile(TileKind.Event, gold: 500, runSeed: 4242UL),
                new ResolveTileCommand(),
                TileWorlds.Context);

            var chosen = SlayIdleRepeat.Core.GameRules.Apply(
                drawn.NewState, new EventChooseCommand(0), TileWorlds.Context);

            return (
                drawn.NewState.Run!.ToSnapshot().PendingEventCardId,
                chosen.NewState.Run!.Gold,
                chosen.NewState.Run!.CurrentHp,
                chosen.Events.OfType<CurrencyChanged>().Select(e => e.Delta).ToArray());
        }

        Loop().ShouldBe(Loop());
    }

    /// <summary>
    /// 🔒 The two draws land on the <c>events</c> stream and it advances by exactly two across the
    /// pair — one for the card, one for the outcome.
    /// </summary>
    [Fact]
    public void The_two_event_draws_advance_the_events_stream_by_exactly_two()
    {
        var drawn = SlayIdleRepeat.Core.GameRules.Apply(
            TileWorlds.OnTile(TileKind.Event, gold: 500), new ResolveTileCommand(), TileWorlds.Context);

        drawn.NewState.Run!.StreamPosition(Core.Rng.RngStreams.Events).ShouldBe(1UL);

        var chosen = Choose(drawn.NewState, 0);

        chosen.NewState.Run!.StreamPosition(Core.Rng.RngStreams.Events).ShouldBe(2UL);
    }
}
