using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>EVENT_CHOOSE, driven through the production dispatch table by GameRules.Apply.</summary>
public sealed class EventChooseTests
{
    private static CommandResult Choose(WorldSlice state, int choiceIndex) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new EventChooseCommand(choiceIndex), TileWorlds.Context);

    private static WorldSlice OnCard(string cardId, long gold = 0L, int currentHp = 100, int chapterId = 1) =>
        TileWorlds.OnTile(
            TileKind.Event, chapterId: chapterId, gold: gold, currentHp: currentHp, eventCardId: cardId);

    /// <summary>
    /// 🔒 A <c>FIXED_DIE</c> outcome owes the player the number of CHOICES it authors — never a
    /// die. A card is drawn by weight and cannot ask anybody for a number (`04` §6.2), so what lands
    /// on the run is the debt, and <c>CHOOSE_FIXED_DIE</c> answers it.
    /// </summary>
    /// <remarks>
    /// Both options of the fixture card, because they grant DIFFERENT counts: a handler granting a
    /// flat single choice would satisfy the first forever.
    /// </remarks>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    public void An_event_that_grants_fixed_dice_owes_that_many_choices(int choiceIndex, int expected)
    {
        var result = Choose(OnCard(FixtureCards.FixedDice), choiceIndex);

        result.Accepted.ShouldBeTrue("EVENT_CHOOSE was refused " + result.Rejection);

        var run = result.NewState.Run!;

        run.PendingFixedDieChoices.ShouldBe(expected);
        run.FixedDice.ShouldBeEmpty(
            "the card owes a choice; it does not pick a number the player never saw.");
        run.HasPendingTile.ShouldBeFalse("the card is spent.");
    }

    [Fact]
    public void A_run_with_no_pending_tile_is_rejected()
    {
        var result = Choose(TileWorlds.OnNoTile(), 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

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

    [Fact]
    public void An_event_tile_with_no_drawn_card_is_rejected()
    {
        var result = Choose(TileWorlds.OnTile(TileKind.Event), 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

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

    [Fact]
    public void A_rejection_leaves_the_card_pending_and_the_gold_untouched()
    {
        var state = OnCard(FixtureCards.Gold, gold: 500);

        var result = Choose(state, 7);

        result.NewState.ShouldBeSameAs(state);
        result.NewState.Run!.Gold.ShouldBe(500);
        result.NewState.Run!.ToSnapshot().PendingEventCardId.ShouldBe(FixtureCards.Gold);
    }

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

    /// <remarks>
    /// round(40 · 1.35^(c-1)): 73 and 327 rather than the tidier 80 and 320, because M(c) is a real
    /// number and only the payout is quantised.
    /// </remarks>
    [Theory]
    [InlineData(1, 40L)]
    [InlineData(3, 73L)]
    [InlineData(8, 327L)]
    public void A_chapter_scaled_grant_is_multiplied_by_the_meta_scalar(int chapterId, long expected)
    {
        var state = OnCard(FixtureCards.Crowns, chapterId: chapterId);
        var before = state.Player.BalanceOf(CurrencyId.CROWNS);

        Choose(state, 0).NewState.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(before + expected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public void A_flat_grant_is_the_same_in_every_chapter(int chapterId)
    {
        Choose(OnCard(FixtureCards.Gold, chapterId: chapterId), 0)
            .NewState.Run!.Gold.ShouldBe(100);
    }

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
        cost.Reason.ShouldBe("event_choice_cost", "a cost is attributed apart from a payout");
        result.Events[1].ShouldBeOfType<CurrencyChanged>().Delta.ShouldBe(40);
    }

    /// <summary>An unaffordable cost is refused, and nothing is spent, drawn or cleared by the refusal.</summary>
    [Fact]
    public void An_unaffordable_option_is_rejected_and_spends_nothing()
    {
        var state = OnCard(FixtureCards.Costly, gold: 99);

        var result = Choose(state, 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
        result.NewState.Run!.Gold.ShouldBe(99);
        result.NewState.Run!.ToSnapshot().PendingEventCardId.ShouldBe(FixtureCards.Costly);
        result.NewState.Run!.RngStreamPositions.ShouldBeEmpty(
            "a refused command draws nothing, so no RNG stream counter moves");
    }

    [Fact]
    public void Exactly_enough_gold_affords_the_option()
    {
        var result = Choose(OnCard(FixtureCards.Costly, gold: 100), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(0);
    }

    /// <summary>
    /// A player fixture with <see cref="CurrencyId.ENHANCE_STONES"/> pinned to a known balance, so a
    /// wallet-cost test's outcome doesn't depend on a shared fixture's undocumented starting value.
    /// </summary>
    private static WorldSlice WithEnhanceStones(WorldSlice state, long enhanceStones) =>
        state with
        {
            Player = Worlds.Rehydrated(
                PlayerSnapshots.With(wallet: PlayerSnapshots.Wallet((CurrencyId.ENHANCE_STONES, enhanceStones)))),
        };

    [Fact]
    public void A_wallet_cost_is_affordable_when_the_players_balance_covers_it()
    {
        var state = WithEnhanceStones(OnCard(FixtureCards.CostlyMeta, gold: 100_000), enhanceStones: 6);

        var result = Choose(state, 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBe(0);
    }

    [Fact]
    public void A_wallet_cost_is_refused_when_the_players_balance_does_not_cover_it()
    {
        var state = WithEnhanceStones(OnCard(FixtureCards.CostlyMeta, gold: 100_000), enhanceStones: 5);

        var result = Choose(state, 0);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(
            RejectionReason.INSUFFICIENT_FUNDS,
            "a wallet cost is not affordable out of the run's Gold, however much of it there is");
    }

    [Fact]
    public void A_free_option_reports_no_cost_row()
    {
        var result = Choose(OnCard(FixtureCards.Costly, gold: 500), 1);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.Gold.ShouldBe(500);
        result.Events.ShouldBeEmpty();
    }

    [Fact]
    public void An_hp_percent_heal_restores_a_share_of_max_hp()
    {
        var result = Choose(OnCard(FixtureCards.Vitals, currentHp: 50), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(75);
    }

    [Fact]
    public void An_hp_percent_heal_is_clamped_at_max_hp()
    {
        var result = Choose(OnCard(FixtureCards.Vitals, currentHp: 90), 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(100);
    }

    [Fact]
    public void A_negative_hp_percent_costs_a_share_of_max_hp()
    {
        Choose(OnCard(FixtureCards.Vitals, currentHp: 50), 1)
            .NewState.Run!.CurrentHp.ShouldBe(40);
    }

    /// <summary>Zero is a legal state, not a death sentence the handler executes.</summary>
    [Fact]
    public void A_negative_hp_percent_is_clamped_at_zero()
    {
        var result = Choose(OnCard(FixtureCards.Vitals, currentHp: 30), 2);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBe(0);
    }

    /// <summary>There is no HP domain event in this game.</summary>
    [Fact]
    public void An_hp_change_produces_no_event()
    {
        Choose(OnCard(FixtureCards.Vitals, currentHp: 50), 0).Events.ShouldBeEmpty();
    }

    /// <summary>A CURSE_REWARD pays through the same table the curse tile pays from.</summary>
    [Fact]
    public void A_curse_reward_effect_pays_the_authored_reward()
    {
        var state = OnCard(FixtureCards.Curse);
        var before = state.Player.BalanceOf(CurrencyId.ENHANCE_STONES);

        var result = Choose(state, 0);

        result.Accepted.ShouldBeTrue();
        result.NewState.Player.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBe(before + 2);
    }

    [Fact]
    public void A_curse_reward_effect_persists_no_curse()
    {
        var result = Choose(OnCard(FixtureCards.Curse), 0);

        var snapshot = result.NewState.Run!.ToSnapshot();
        snapshot.PendingTileKind.ShouldBe(-1);
        snapshot.PendingEventCardId.ShouldBe("");
    }

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
    /// Correct, not a gap: an UNSUPPORTED effect names a mechanic Core cannot execute, and inventing
    /// a substitute payout would be indistinguishable downstream from a real reward.
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

    /// <remarks>
    /// Pinned to a band around 70% rather than a bare majority check: a swapped 70/30-to-30/70 weight
    /// still clears a majority check over a small sample but fails a band this tight. Both branches
    /// occurring at all is implied by the band.
    /// </remarks>
    [Fact]
    public void Each_branch_of_a_seventy_thirty_split_is_drawn_at_its_own_weight()
    {
        const int samples = 600;

        var draws = Enumerable.Range(1, samples)
            .Select(seed => TileWorlds.OnTile(
                TileKind.Event, eventCardId: FixtureCards.Split, runSeed: (ulong)seed))
            .Select(state => Choose(state, 0))
            .Select(r => r.Events.Cast<CurrencyChanged>().Single().Id)
            .ToArray();

        var heavyShare = draws.Count(id => id == CurrencyId.ENHANCE_STONES) / (double)samples;

        heavyShare.ShouldBeInRange(0.58, 0.82, "the 70-weighted branch is drawn at about 70%");
    }

    /// <summary>A guaranteed effect applies on every branch of the split, rather than as a nested layer.</summary>
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

    /// <summary>Same seed, same card, same outcome, same payout.</summary>
    /// <remarks>
    /// ⚠️ <b>The deltas are compared as TEXT, and that is a fix rather than a style.</b> They used
    /// to be an <c>IReadOnlyList&lt;long&gt;</c> inside the compared tuple, which a tuple's structural
    /// equality compares by REFERENCE — so the case only ever held while the seed happened to draw a
    /// card that paid nothing, because an empty <c>ToArray()</c> is the same shared instance every
    /// time. The first card added to the fixture pool moved the draw onto one that pays, and the case
    /// went red on two equal payouts. Joined text is compared by value, so it holds for either.
    /// </remarks>
    [Fact]
    public void The_whole_event_loop_is_deterministic_for_a_fixed_seed()
    {
        static (string Card, long Gold, int Hp, string Deltas) Loop()
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
                string.Join(
                    ",",
                    chosen.Events.OfType<CurrencyChanged>()
                        .Select(e => e.Delta.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        Loop().ShouldBe(Loop());
    }

    /// <summary>One draw for the card, one for the outcome.</summary>
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
