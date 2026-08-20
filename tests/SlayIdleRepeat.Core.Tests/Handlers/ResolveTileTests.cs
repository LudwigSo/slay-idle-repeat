using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>RESOLVE_TILE, driven through the production dispatch table by GameRules.Apply.</summary>
public sealed class ResolveTileTests
{
    private static CommandResult Resolve(WorldSlice state, GameContext? context = null) =>
        SlayIdleRepeat.Core.GameRules.Apply(
            state, new ResolveTileCommand(), context ?? TileWorlds.Context);

    /// <summary>
    /// A run's whole state as canonical bytes with the pending-tile columns neutralised, so two
    /// readings differ only if something OTHER than the tile being cleared moved.
    /// </summary>
    /// <remarks>
    /// Canonical bytes rather than record equality (steering S17): <c>RunSnapshot</c> holds
    /// <c>IReadOnlyDictionary</c> members, which a record's synthesized equality compares by reference.
    /// </remarks>
    private static byte[] BytesBesidesThePendingTile(WorldSlice state) =>
        CanonicalStateWriter.CanonicalBytes(state.Run!.ToSnapshot() with
        {
            PendingTileKind = RunSnapshots.NoPendingTile,
            PendingTileLinearIndex = 0,
            PendingTileStage = 0,
            PendingEventCardId = RunSnapshots.NoPendingEventCard,
        });

    // ------------------------------------------------------------------ the gate

    /// <summary>A run standing on no tile has nothing to resolve.</summary>
    [Fact]
    public void A_run_with_no_pending_tile_is_rejected()
    {
        var result = Resolve(TileWorlds.OnNoTile());

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A rejected command leaves the caller's own slice untouched.</summary>
    [Fact]
    public void A_rejection_leaves_the_run_untouched()
    {
        var state = TileWorlds.OnNoTile(gold: 500);

        var result = Resolve(state);

        result.NewState.ShouldBeSameAs(state);
        result.NewState.Run!.Gold.ShouldBe(500);
    }

    /// <summary>A run-less slice throws, the same guard every other run command hits.</summary>
    [Fact]
    public void A_run_less_slice_throws_rather_than_rejects()
    {
        Should.Throw<InvalidOperationException>(() =>
            SlayIdleRepeat.Core.GameRules.Apply(
                Worlds.OutsideARun(), new ResolveTileCommand(), TileWorlds.Context));
    }

    // ------------------------------------------------------------------ resolved and cleared

    /// <summary>The breather tile resolves to nothing and clears.</summary>
    [Fact]
    public void An_empty_tile_resolves_to_nothing_and_clears()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Empty));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }

    /// <summary>A treasure tile pays its drawn profile and clears.</summary>
    [Fact]
    public void A_treasure_tile_pays_and_clears()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Treasure));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldNotBeEmpty();
        result.Events.ShouldAllBe(e => e is CurrencyChanged);
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }

    /// <summary>…and every row it pays is a meta wallet currency, never the run's own GOLD.</summary>
    [Fact]
    public void A_treasure_tile_pays_no_run_gold()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Treasure, gold: 100));

        result.NewState.Run!.Gold.ShouldBe(100);
        result.Events.Cast<CurrencyChanged>().ShouldAllBe(e => e.Id != CurrencyId.GOLD);
    }

    /// <summary>A zero column is skipped rather than emitted as a zero-delta row.</summary>
    [Fact]
    public void A_zero_payout_column_emits_no_event()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Treasure));

        result.Events.Cast<CurrencyChanged>().ShouldAllBe(e => e.Delta != 0);
        result.Events.Count.ShouldBeLessThan(3, "no authored profile pays all three columns");
    }

    /// <summary>A cache tile pays Beast Feed and clears.</summary>
    [Fact]
    public void A_cache_tile_pays_beast_feed_and_clears()
    {
        // The shipped egg rate is set to 0 so this test names the feed branch deterministically.
        var noEggs = TileWorlds.ContextOver(
            InRunIncomeDocuments.With(eggChance: ContentValue.Number(0m)));

        var result = Resolve(TileWorlds.OnTile(TileKind.Cache), noEggs);

        result.Accepted.ShouldBeTrue();
        var paid = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>();
        paid.Id.ShouldBe(CurrencyId.BEAST_FEED);
        paid.Delta.ShouldBe(InRunIncomeDocuments.ShippedBeastFeedBase);
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }

    /// <summary>
    /// …and on the egg branch it pays nothing at all: a Pet Egg is a container, deferred to a later
    /// milestone.
    /// </summary>
    [Fact]
    public void A_cache_tile_that_rolls_an_egg_pays_nothing()
    {
        var allEggs = TileWorlds.ContextOver(
            InRunIncomeDocuments.With(eggChance: ContentValue.Number(1m)));

        var result = Resolve(TileWorlds.OnTile(TileKind.Cache), allEggs);

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }

    /// <summary>The cache's Beast Feed scales by <c>M(c)</c>.</summary>
    /// <remarks>
    /// Expectations are round(25 · 1.35^(c-1)): the amount scaled then rounded, not the amount times
    /// a rounded scalar — which is why they read 46 and 204 rather than the tidier 50 and 200.
    /// </remarks>
    [Theory]
    [InlineData(1, 25L)]
    [InlineData(3, 46L)]
    [InlineData(8, 204L)]
    public void The_cache_payout_scales_by_the_chapter_scalar(int chapterId, long expected)
    {
        var noEggs = TileWorlds.ContextOver(
            InRunIncomeDocuments.With(eggChance: ContentValue.Number(0m)));

        var result = Resolve(TileWorlds.OnTile(TileKind.Cache, chapterId: chapterId), noEggs);

        result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>().Delta.ShouldBe(expected);
    }

    /// <summary>
    /// A shrine is ACKNOWLEDGED here and finished by <c>SHRINE_CHOOSE</c> — the player picks one of
    /// the two options, so <c>RESOLVE_TILE</c> cannot be the command that applies one.
    /// </summary>
    /// <remarks>
    /// Produces no events either way: a shrine moves no currency and HP has no domain event. What
    /// this pins is that the tile is STILL PENDING, which is what makes the choose command reachable.
    /// </remarks>
    [Fact]
    public void A_shrine_tile_is_acknowledged_and_left_for_the_choice()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Shrine, currentHp: 50));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe((int)TileKind.Shrine);
    }

    /// <summary>A curse tile pays a curse's reward and clears.</summary>
    [Fact]
    public void A_curse_tile_pays_a_reward_and_clears()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Curse));

        result.Accepted.ShouldBeTrue();
        var paid = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>();
        paid.Delta.ShouldBeGreaterThan(0);
        paid.Reason.ShouldBe("curse_tile_reward");
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(-1);
    }

    /// <summary>
    /// 🔒 …and it APPLIES the curse, which is what stops the tile being strictly good.
    /// </summary>
    /// <remarks>
    /// The reward assertion above cannot see this: a resolver that paid and applied nothing
    /// satisfies it completely, which is exactly what this tile did before the run could hold a
    /// curse at all.
    /// </remarks>
    [Fact]
    public void A_curse_tile_applies_the_curse_it_drew()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Curse));

        result.NewState.Run!.Curses.Count.ShouldBe(
            1, "the run took the reward and suffered nothing.");
    }

    /// <summary>
    /// A curse already carried is not applied twice (`19` Part E gives curses no stacking) — and the
    /// reward is still paid.
    /// </summary>
    /// <remarks>
    /// Paying anyway is the reading that keeps the tile's bargain honest: the alternative is a tile
    /// that sometimes does nothing at all, decided by a draw the player cannot see or influence.
    /// </remarks>
    [Fact]
    public void A_curse_already_carried_is_not_stacked_and_still_pays()
    {
        var first = Resolve(TileWorlds.OnTile(TileKind.Curse)).NewState;
        var carried = first.Run!.Curses[0];

        var again = Resolve(TileWorlds.OnTile(TileKind.Curse, curses: [carried]));

        again.Accepted.ShouldBeTrue();
        again.NewState.Run!.Curses.ShouldBe(new[] { carried }, "no second copy.");
        again.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>()
            .Delta.ShouldBeGreaterThan(0, "the reward is paid whether or not the debuff landed.");
    }

    /// <summary>…and the reward it pays is one of the chapter-1 curses CurseRewards can pay, whatever the seed.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(999UL)]
    [InlineData(0xFFFFFFFFFFFFFFFFUL)]
    public void A_chapter_one_curse_tile_only_ever_pays_an_authored_reward(ulong seed)
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Curse, runSeed: seed));

        var paid = result.Events.ShouldHaveSingleItem().ShouldBeOfType<CurrencyChanged>();

        CurseRewards.PayableIds
            .Select(CurseRewards.For)
            .ShouldContain((paid.Id, paid.Delta));
    }

    /// <summary>A GOLD reward moves on the run and a wallet reward on the player.</summary>
    [Fact]
    public void A_curse_reward_moves_on_the_right_aggregate()
    {
        var state = TileWorlds.OnTile(TileKind.Curse);
        var goldBefore = state.Run!.Gold;
        var stonesBefore = state.Player.BalanceOf(CurrencyId.ENHANCE_STONES);

        var result = Resolve(state);
        var paid = result.Events.Cast<CurrencyChanged>().Single();

        if (paid.Id == CurrencyId.GOLD)
        {
            result.NewState.Run!.Gold.ShouldBe(goldBefore + paid.Delta);
            result.NewState.Player.BalanceOf(CurrencyId.ENHANCE_STONES).ShouldBe(stonesBefore);
        }
        else
        {
            result.NewState.Run!.Gold.ShouldBe(goldBefore);
            result.NewState.Player.BalanceOf(paid.Id).ShouldBe(stonesBefore + paid.Delta);
        }
    }

    // ------------------------------------------------------------------ the visits that grant nothing

    /// <summary>A shop visit stocks the offer and leaves the tile open for the player to shop at.</summary>
    [Fact]
    public void A_shop_tile_stocks_an_offer_and_stays_open()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Shop));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty("stocking announces nothing; nothing has been bought yet");
        result.NewState.Run!.HasOpenShop.ShouldBeTrue("the visit is what stocks the four slots");
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(
            (int)TileKind.Shop,
            "SHOP_LEAVE is the shop's clearing step — a shop that cleared on arrival could never " +
            "sell anything, which is exactly what it used to do.");
    }

    /// <summary>
    /// 🔒 A second <c>RESOLVE_TILE</c> at an open shop is refused rather than re-stocking: a free
    /// re-roll of the offer would sidestep the refresh economy entirely.
    /// </summary>
    [Fact]
    public void A_second_visit_to_an_open_shop_is_refused()
    {
        var open = Resolve(TileWorlds.OnTile(TileKind.Shop)).NewState;

        var again = Resolve(open);

        again.Accepted.ShouldBeFalse();
        again.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>A Dice Forge visit is acknowledged and CLEARS the tile — the forge offers nothing.</summary>
    /// <remarks>
    /// ⚠️ It used to be left pending for <c>DICE_FORGE_CHOOSE</c>. The forge installed replacement
    /// die faces, the die has no faces, and that command is gone — so leaving the tile pending would
    /// wedge the run on it with nothing able to clear it. The kind is kept for a later repurposing.
    /// </remarks>
    [Fact]
    public void A_dice_forge_tile_is_acknowledged_and_clears_itself()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.DiceForge));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty("walking into a forge grants nothing");
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe(
            RunSnapshots.NoPendingTile, "a tile nothing can resolve must not stay pending.");
    }

    /// <summary>
    /// …and the run can actually LEAVE a shop: the roll coming back accepted AND the position
    /// changing is the claim "the tile is no longer pending" alone does not make (steering S24).
    /// </summary>
    [Fact]
    public void A_run_rolls_off_a_shop_it_has_left()
    {
        var open = Resolve(TileWorlds.OnTile(TileKind.Shop)).NewState;

        var left = SlayIdleRepeat.Core.GameRules.Apply(
            open, new ShopLeaveCommand(), TileWorlds.Context);

        left.Accepted.ShouldBeTrue("SHOP_LEAVE was refused " + left.Rejection);

        var rolled = SlayIdleRepeat.Core.GameRules.Apply(
            left.NewState, new RollDiceCommand(), TileWorlds.Context);

        rolled.Accepted.ShouldBeTrue(
            "ROLL_DICE was refused " + rolled.Rejection + " from a shop the run had left, so the " +
            "shop is still pending and the run cannot leave it.");
        rolled.NewState.Run!.Position.ShouldBeGreaterThan(
            left.NewState.Run!.Position, "the roll was accepted and the run stood still.");
    }

    /// <summary>
    /// …and so can it off a Dice Forge, which RESOLVE_TILE now clears by itself.
    /// </summary>
    /// <remarks>
    /// 🔒 The load-bearing half is that the tile is no longer pending. The forge's mechanic is gone
    /// and so is DICE_FORGE_CHOOSE, so nothing else can clear it — a forge that stayed pending would
    /// wedge the run on the tile with no command able to move it.
    /// </remarks>
    [Fact]
    public void A_dice_forge_clears_itself_and_the_run_rolls_off_it()
    {
        var open = Resolve(TileWorlds.OnTile(TileKind.DiceForge)).NewState;

        open.Run!.HasPendingTile.ShouldBeFalse("a forge offers nothing, so it does not stay pending.");

        var rolled = SlayIdleRepeat.Core.GameRules.Apply(
            open, new RollDiceCommand(), TileWorlds.Context);

        rolled.Accepted.ShouldBeTrue(
            "ROLL_DICE was refused " + rolled.Rejection + " from a forge the run had landed on.");
    }

    /// <summary>
    /// 🔒 A shop visit spends no Gold and grants nothing — walking IN is not buying. What it does
    /// move is the shop stream and the visit's own three fields, which is why this reads the run's
    /// wallet and hit points rather than its whole bytes.
    /// </summary>
    [Fact]
    public void A_shop_visit_buys_nothing_by_itself()
    {
        var state = TileWorlds.OnTile(TileKind.Shop, gold: 500, currentHp: 40);

        var result = Resolve(state);

        result.Events.ShouldBeEmpty(
            "a shop visit announced something. Stocking is not a purchase, and a purchase is the " +
            "only thing at a shop that moves a currency.");
        result.NewState.Run!.Gold.ShouldBe(500);
        result.NewState.Run!.CurrentHp.ShouldBe(40);
        result.NewState.Run!.ShrineBuffs.ShouldBeEmpty();
        result.NewState.Run!.RunBuffs.ShouldBeEmpty();
        result.NewState.Run!.Consumables.ShouldBeEmpty();
    }

    /// <summary>A Dice Forge visit modifies no die face — the CHOICE does, and it has not been made.</summary>
    /// <remarks>
    /// Stated as "nothing at all moved" rather than as a list of what a forge must not touch: an
    /// upgrade landing on the mere acknowledgement turns this red.
    /// </remarks>
    [Fact]
    public void A_dice_forge_visit_owes_a_fixed_die_choice_and_moves_nothing_else()
    {
        var state = TileWorlds.OnTile(TileKind.DiceForge, gold: 500, currentHp: 40);

        var result = Resolve(state);
        var run = result.NewState.Run!;

        result.Events.ShouldBeEmpty(
            "the forge owes a choice rather than granting anything, so there is nothing to announce");

        run.PendingFixedDieChoices.ShouldBe(
            Core.Handlers.ResolveTile.DiceForgeFixedDiceGranted,
            "a forge visit that owed nothing would be a tile the player walks over for free.");

        run.FixedDice.ShouldBeEmpty(
            "the forge owes a choice; it does not pick a number for the player.");

        // 🔒 The wallet and the hero are the negative half, and they are read explicitly rather than
        // through a whole-bytes comparison: the pending-choice counter is EXPECTED to move now, so a
        // bytes pin would have to exclude it and would then stop watching it.
        run.BalanceOf(CurrencyId.GOLD).ShouldBe(500L, "a forge visit costs nothing and pays nothing.");
        run.CurrentHp.ShouldBe(40, "a forge visit does not touch the hero.");
        run.HasPendingTile.ShouldBeFalse("nothing can clear the tile after this, so it clears itself.");
    }

    /// <summary>
    /// 🔒 A purchase is legal only at an OPEN shop: not before the visit stocked one, and not after
    /// the run has left it.
    /// </summary>
    /// <remarks>
    /// The "after" half is the one worth having. A shop offer is derived from a recorded stream
    /// position, so a purchase mask or an offer position that outlived the visit would let the run
    /// go on buying from a shop several nodes behind it — and no assertion about the tile alone can
    /// see that.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_purchase_is_legal_only_while_the_shop_is_open(int slotIndex)
    {
        // Hurt, because slot 4 is the Heal and 03 §7.1 refuses one at full HP — a fixture at full
        // health would make the acceptance below unreachable for that slot alone.
        var standingOn = TileWorlds.OnTile(TileKind.Shop, gold: 100_000, currentHp: 40);

        var early = SlayIdleRepeat.Core.GameRules.Apply(
            standingOn, new ShopBuyCommand(slotIndex), TileWorlds.Context);

        early.Accepted.ShouldBeFalse(
            "slot " + slotIndex + " was purchasable before RESOLVE_TILE stocked an offer — so the " +
            "run bought from a shop whose contents nothing had drawn yet.");
        early.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);

        var open = Resolve(standingOn).NewState;

        SlayIdleRepeat.Core.GameRules.Apply(open, new ShopBuyCommand(slotIndex), TileWorlds.Context)
            .Accepted.ShouldBeTrue(
                "slot " + slotIndex + " was refused at an open shop with 100,000 Gold in the run. " +
                "Negative control for the two refusals either side of it: without this, both would " +
                "pass on a shop that sells nothing at all.");

        var left = SlayIdleRepeat.Core.GameRules.Apply(
            open, new ShopLeaveCommand(), TileWorlds.Context).NewState;

        var bought = SlayIdleRepeat.Core.GameRules.Apply(
            left, new ShopBuyCommand(slotIndex), TileWorlds.Context);

        bought.Accepted.ShouldBeFalse(
            "slot " + slotIndex + " was still purchasable after SHOP_LEAVE, so the run can shop at " +
            "a tile it has already walked away from.");
        bought.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>…and a slot outside the four SHOP_BUY names is refused for the index alone.</summary>
    /// <remarks>
    /// Its own case rather than a fifth row above: the slot guard outlives a stocked offer, so this
    /// claim cannot expire with the pin's rows.
    /// </remarks>
    [Fact]
    public void A_shop_slot_outside_the_four_is_refused()
    {
        var bought = SlayIdleRepeat.Core.GameRules.Apply(
            TileWorlds.OnTile(TileKind.Shop), new ShopBuyCommand(4), TileWorlds.Context);

        bought.Accepted.ShouldBeFalse("SHOP_BUY names four slots, 0..3, and answered a fifth.");
        bought.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    // ------------------------------------------------------------------ advanced, not cleared

    /// <summary>An event tile draws a card and keeps the tile pending for EVENT_CHOOSE.</summary>
    [Fact]
    public void An_event_tile_draws_a_card_and_stays_pending()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Event));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty("drawing a card moves no currency");

        var run = result.NewState.Run!;
        run.ToSnapshot().PendingTileKind.ShouldBe((int)TileKind.Event);
        run.ToSnapshot().PendingEventCardId.ShouldNotBeNullOrEmpty();
    }

    /// <summary>
    /// …and resubmitting is refused rather than re-drawing: a client that disliked its card must not
    /// be able to roll again.
    /// </summary>
    [Fact]
    public void Resubmitting_a_drawn_event_tile_is_rejected()
    {
        var drawn = Resolve(TileWorlds.OnTile(TileKind.Event)).NewState;

        var again = Resolve(drawn);

        again.Accepted.ShouldBeFalse();
        again.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        again.NewState.Run!.ToSnapshot().PendingEventCardId
            .ShouldBe(drawn.Run!.ToSnapshot().PendingEventCardId, "the first card survives");
    }

    /// <summary>The card draw is deterministic for a fixed seed.</summary>
    [Fact]
    public void The_event_card_draw_is_deterministic_for_a_fixed_seed()
    {
        var first = Resolve(TileWorlds.OnTile(TileKind.Event, runSeed: 12345UL));
        var second = Resolve(TileWorlds.OnTile(TileKind.Event, runSeed: 12345UL));

        first.NewState.Run!.ToSnapshot().PendingEventCardId
            .ShouldBe(second.NewState.Run!.ToSnapshot().PendingEventCardId);
    }

    /// <summary>…and it draws only cards this run's chapter can see: chapter 1 never draws a chapter-5 card.</summary>
    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(4UL)]
    [InlineData(5UL)]
    [InlineData(6UL)]
    [InlineData(7UL)]
    [InlineData(8UL)]
    [InlineData(9UL)]
    [InlineData(10UL)]
    public void The_event_card_draw_respects_the_chapter_band(ulong seed)
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Event, chapterId: 1, runSeed: seed));

        result.NewState.Run!.ToSnapshot().PendingEventCardId.ShouldNotBe(FixtureCards.LateOnly);
    }

    /// <summary>…and the negative control: a chapter-5 run CAN draw it.</summary>
    [Fact]
    public void A_chapter_five_run_can_draw_the_late_banded_card()
    {
        var drawn = Enumerable.Range(1, 60)
            .Select(seed => Resolve(TileWorlds.OnTile(TileKind.Event, chapterId: 5, runSeed: (ulong)seed))
                .NewState.Run!.ToSnapshot().PendingEventCardId)
            .ToArray();

        drawn.ShouldContain(FixtureCards.LateOnly);
    }

    /// <summary>A campfire accepts and stays pending for CAMPFIRE_CHOOSE.</summary>
    [Fact]
    public void A_campfire_tile_stays_pending()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Campfire));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe((int)TileKind.Campfire);
    }

    /// <summary>…and it draws nothing, so no RNG stream counter moves.</summary>
    [Fact]
    public void A_campfire_tile_consumes_no_rng_draw()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Campfire));

        result.NewState.Run!.RngStreamPositions.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------ acknowledged, not cleared

    /// <summary>
    /// The battle tiles are acknowledged and left pending — the seam START_BATTLE and the
    /// post-battle draft both read.
    /// </summary>
    [Theory]
    [InlineData((int)TileKind.Enemy)]
    [InlineData((int)TileKind.Elite)]
    [InlineData((int)TileKind.Boss)]
    public void A_battle_tile_is_acknowledged_and_left_pending_for_M3_05(int kind)
    {
        var result = Resolve(TileWorlds.OnTile((TileKind)kind, linearIndex: 19, stage: 2));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();

        // All three facts survive: START_BATTLE needs them to feed EnemyPowerFormula.Compute.
        var snapshot = result.NewState.Run!.ToSnapshot();
        snapshot.PendingTileKind.ShouldBe(kind);
        snapshot.PendingTileLinearIndex.ShouldBe(19);
        snapshot.PendingTileStage.ShouldBe(2);
    }

    /// <summary>
    /// The Minigame is the one tile left that resolves through its own command — MINIGAME_SUBMIT —
    /// so it is acknowledged and left pending for it. Portal is deliberately not among these; see
    /// <see cref="A_portal_tile_resolves_the_jump_immediately"/>.
    /// </summary>
    [Fact]
    public void A_minigame_tile_is_acknowledged_and_left_pending_for_MINIGAME_SUBMIT()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Minigame));

        result.Accepted.ShouldBeTrue();
        result.Events.ShouldBeEmpty();
        result.NewState.Run!.ToSnapshot().PendingTileKind.ShouldBe((int)TileKind.Minigame);
    }

    /// <summary>
    /// The Portal tile fully resolves within RESOLVE_TILE itself: the run either lands on a new node
    /// or pauses at a junction with a genuine pending fork, but is never left stuck pending on the
    /// Portal tile it started from.
    /// </summary>
    [Fact]
    public void A_portal_tile_resolves_the_jump_immediately()
    {
        var result = Resolve(TileWorlds.OnTile(TileKind.Portal));

        result.Accepted.ShouldBeTrue();

        var snapshot = result.NewState.Run!.ToSnapshot();

        (snapshot.PendingTileKind != (int)TileKind.Portal || snapshot.PendingForkJunctionPosition.HasValue)
            .ShouldBeTrue("a Portal jump must not leave the run stuck on the Portal tile it started from");
    }

    // ------------------------------------------------------------------ the vocabulary floor

    /// <summary>Every one of the fourteen tile kinds has a branch — none reaches the default arm.</summary>
    /// <remarks>
    /// Stated over the enum itself rather than as a list of fourteen cases, so a fifteenth member
    /// fails this test on the commit that adds it.
    /// </remarks>
    [Fact]
    public void Every_authored_tile_kind_has_a_branch()
    {
        var kinds = Enum.GetValues<TileKind>();

        kinds.Length.ShouldBe(14, "fourteen tile kinds are authored");

        foreach (var kind in kinds)
        {
            Should.NotThrow(
                () => Resolve(TileWorlds.OnTile(kind)),
                kind + " reached RESOLVE_TILE's default arm");
        }
    }

    /// <summary>…and a kind outside the fourteen throws rather than being silently accepted.</summary>
    [Fact]
    public void A_kind_outside_the_vocabulary_throws()
    {
        var alien = new WorldSlice(
            Worlds.NewPlayer(),
            Worlds.NewRun(RunSnapshots.OnPendingTile(tileKind: 99)));

        Should.Throw<InvalidOperationException>(() => Resolve(alien));
    }
}
