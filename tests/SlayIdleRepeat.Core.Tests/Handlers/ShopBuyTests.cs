using Shouldly;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// SHOP_BUY over a real four-slot offer: legality, affordability, the grant, and the two rules that
/// stop a purchase being wasted.
/// </summary>
public sealed class ShopBuyTests
{
    /// <summary>Enough Gold that nothing in this suite is accidentally an affordability test.</summary>
    private const long Rich = 100_000;

    /// <summary>A run standing at a stocked shop, with the offer it is showing.</summary>
    private static (WorldSlice State, IReadOnlyList<RunShopSlot> Offer) AtAShop(
        long gold = Rich, int currentHp = 40, IReadOnlyList<string>? curses = null)
    {
        var arrived = TileWorlds.OnTile(
            TileKind.Shop, gold: gold, currentHp: currentHp, curses: curses);

        var open = SlayIdleRepeat.Core.GameRules.Apply(
            arrived, new ResolveTileCommand(), TileWorlds.Context).NewState;

        return (
            open,
            RunShopOffer.DrawAt(open.Run!, TileWorlds.Context.Content, open.Run!.ShopOfferDraw!.Value));
    }

    private static CommandResult Buy(WorldSlice state, int slotIndex) =>
        SlayIdleRepeat.Core.GameRules.Apply(state, new ShopBuyCommand(slotIndex), TileWorlds.Context);

    // ------------------------------------------------------------------------------- legality

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(1000)]
    public void An_out_of_range_slot_index_is_rejected(int slotIndex)
    {
        var (state, _) = AtAShop();

        var result = Buy(state, slotIndex);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>Buying the same slot twice is refused — the offer is marked, not re-drawn.</summary>
    [Fact]
    public void A_slot_can_only_be_bought_once()
    {
        var (state, _) = AtAShop();

        var first = Buy(state, RunShopOffer.HealSlot);
        var second = Buy(first.NewState, RunShopOffer.HealSlot);

        first.Accepted.ShouldBeTrue();
        second.Accepted.ShouldBeFalse();
        second.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>…and the other slots are still buyable, so the mark is per slot rather than per visit.</summary>
    [Fact]
    public void Buying_one_slot_leaves_the_others_open()
    {
        var (state, _) = AtAShop();

        var bought = Buy(state, RunShopOffer.HealSlot);

        Buy(bought.NewState, RunShopOffer.RunBuffSlot).Accepted.ShouldBeTrue();
    }

    [Fact]
    public void A_purchase_the_run_cannot_afford_answers_insufficient_funds()
    {
        var (state, offer) = AtAShop(gold: 0);

        var result = Buy(state, RunShopOffer.HealSlot);

        offer[RunShopOffer.HealSlot].Price.ShouldBeGreaterThan(0, "otherwise the slot is free.");
        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
        result.NewState.ShouldBeSameAs(state, "a rejection returns the caller's slice.");
    }

    // ------------------------------------------------------------------------------- the grants

    /// <summary>Slot 4 heals a share of Max HP and charges its price.</summary>
    [Fact]
    public void The_heal_slot_heals_and_charges()
    {
        var (state, offer) = AtAShop(currentHp: 40);
        var price = offer[RunShopOffer.HealSlot].Price;

        var result = Buy(state, RunShopOffer.HealSlot);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.CurrentHp.ShouldBeGreaterThan(40);
        result.NewState.Run!.Gold.ShouldBe(Rich - price);
        result.Events.ShouldHaveSingleItem()
            .ShouldBeOfType<CurrencyChanged>().Delta.ShouldBe(-price);
    }

    /// <summary>
    /// 🔒 …and is refused at full HP: `03` §7.1's "disabled at full HP — can never be wasted by a
    /// mis-tap". A shop that took the Gold and healed nothing is the failure that sentence is
    /// written against.
    /// </summary>
    [Fact]
    public void The_heal_slot_is_refused_at_full_health()
    {
        var (state, _) = AtAShop(currentHp: 100);

        var result = Buy(state, RunShopOffer.HealSlot);

        result.Accepted.ShouldBeFalse();
        result.Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
        result.NewState.Run!.Gold.ShouldBe(Rich, "and nothing was charged for it.");
    }

    /// <summary>Slot 3 joins the run's additive run-buff list.</summary>
    [Fact]
    public void The_run_buff_slot_records_the_buff()
    {
        var (state, offer) = AtAShop();

        var result = Buy(state, RunShopOffer.RunBuffSlot);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.RunBuffs.ShouldBe(new[] { offer[RunShopOffer.RunBuffSlot].ItemId! });
    }

    /// <summary>Slot 1 grants the perk into the same holding <c>PICK_PERK</c> writes.</summary>
    [Fact]
    public void The_perk_slot_grants_the_perk_at_tier_one()
    {
        var (state, offer) = AtAShop();
        var perkId = offer[RunShopOffer.PerkSlot].ItemId;

        perkId.ShouldNotBeNull("the shipped catalogue has a row of every rarity to draw.");

        var result = Buy(state, RunShopOffer.PerkSlot);

        result.Accepted.ShouldBeTrue();
        result.NewState.Run!.DraftedPerks.TierOf(perkId).ShouldBe(1);
    }

    /// <summary>
    /// Slot 2 either holds the consumable or converts it to a charge at the till — `03` §7.1's split,
    /// which is why the assertion is over both shapes rather than one.
    /// </summary>
    [Fact]
    public void The_consumable_slot_holds_or_converts_by_kind()
    {
        var (state, offer) = AtAShop();
        var id = offer[RunShopOffer.ConsumableSlot].ItemId!;

        var result = Buy(state, RunShopOffer.ConsumableSlot);
        var run = result.NewState.Run!;

        result.Accepted.ShouldBeTrue();

        if (Consumables.IsHeld(id))
        {
            run.ConsumableCount(id).ShouldBe(1);
            run.FreeDraftRerolls.ShouldBe(0);
        }
        else
        {
            run.Consumables.ShouldBeEmpty("a token is never held.");
            run.FreeDraftRerolls.ShouldBe(1, "…it converts to its charge at the till instead.");
        }
    }

    // ------------------------------------------------------------------------------- the offer

    /// <summary>
    /// 🔒 The offer a purchase is charged against is the one the screen re-derives, and both come off
    /// the recorded stream position. Nothing else in this suite could catch a shop that showed one
    /// thing and charged for another.
    /// </summary>
    [Fact]
    public void The_offer_is_stable_across_reads()
    {
        var (state, first) = AtAShop();

        var second = RunShopOffer.DrawAt(
            state.Run!, TileWorlds.Context.Content, state.Run!.ShopOfferDraw!.Value);

        second.ShouldBe(first);
    }

    /// <summary>Stocking spends exactly one offer's worth of the shop stream.</summary>
    /// <remarks>
    /// The constant and the draw have to agree or a refresh overlaps the offer it just replaced —
    /// see <c>RunShopOffer.DrawsPerOffer</c>.
    /// </remarks>
    [Fact]
    public void Stocking_spends_exactly_one_offers_worth_of_draws()
    {
        var (state, _) = AtAShop();

        state.Run!.StreamPosition(RngStreams.Shop).ShouldBe((ulong)RunShopOffer.DrawsPerOffer);
    }

    /// <summary>
    /// 🔒 `19` Part E's <c>CUR_MISERLY</c> raises every price by half — the one curse whose effect is
    /// a non-combat stat, and therefore the one the fight's own stat block could never carry.
    /// </summary>
    [Fact]
    public void A_price_curse_raises_every_price()
    {
        var (_, plain) = AtAShop();
        var (_, cursed) = AtAShop(curses: ["CUR_MISERLY"]);

        for (var slot = 0; slot < plain.Count; slot++)
        {
            if (plain[slot].Price == 0)
            {
                continue;
            }

            cursed[slot].Price.ShouldBeGreaterThan(
                plain[slot].Price,
                "slot " + slot + " cost the same with CUR_MISERLY active, so the curse is inert.");
        }
    }

    /// <summary>A row the draft withholds is never on the shelf either.</summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>The shelf is the second door a perk reaches a player through, and it was open.</b> A row
    /// whose effect reads run state no fight composition supplies is authored, schema-valid and
    /// unplayable: acquiring it ends the run at the next battle with an effect-context failure out of
    /// <c>GameRules.Apply</c>. The draft withholds such rows on their own pool tag; for a while the
    /// shop did not, so the crash was one purchase away with nothing red anywhere.
    /// </para>
    /// <para>
    /// 🔒 The withheld id is named, but the rule is not keyed on it: production asks
    /// <c>PerkDraftEngine.IsOfferable</c>, which reads the row's tag. This case names the id only
    /// because a test has to look for something specific — the day the row is retagged, this case is
    /// what says so, and it should then be pointed at whatever row is withheld instead.
    /// </para>
    /// <para>
    /// ⚠️ <b>Both floors are load-bearing.</b> "The shelf never held it" is satisfied perfectly by a
    /// shelf that held nothing at all, and equally by a sweep that never reached the band it is
    /// authored in — the withheld row is a Legendary, and a stage's Legendary weight is a twentieth
    /// of the table. So the sweep asserts it stocked perks at all, and that it stocked a
    /// <b>Legendary</b> one. The second floor reads the slot's own rarity rather than naming a row
    /// believed to be Legendary: the first draft of this case named a row that turned out to be a
    /// COMMON base perk, and the floor was worthless while reading as if it were not.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_shelf_never_stocks_a_row_withheld_from_the_draft()
    {
        // The withheld row today. Production keys on the row's pool tag, never on this id — the
        // name is here only because a test has to look for something specific.
        const string withheld = "PK_ENDLESS_BLIGHT";

        var (state, _) = AtAShop();

        var stocked = new List<string>();
        var legendaries = 0;

        // One offer per block of draws, walked forward over the shop stream rather than sampled at
        // one position: a single offer draws one perk row, so a single position could not reach a
        // band at all.
        for (ulong position = 0; position < 4000; position += RunShopOffer.DrawsPerOffer)
        {
            foreach (var slot in RunShopOffer.DrawAt(state.Run!, TileWorlds.Context.Content, position))
            {
                if (slot.Kind != ShopItemKind.PERK || slot.ItemId is not { } id)
                {
                    continue;
                }

                stocked.Add(id);

                if (slot.Rarity == ShopRarity.LEGENDARY)
                {
                    legendaries++;
                }
            }
        }

        stocked.Count.ShouldBeGreaterThan(
            100, "floor: a sweep that stocked no perk at all proves nothing about which it stocks");
        legendaries.ShouldBeGreaterThan(
            0,
            "floor: no Legendary row was stocked, so the band the withheld row lives in was never " +
            "drawn and its absence is not evidence");
        stocked.ShouldNotContain(
            withheld,
            "the row the draft withholds was on the shelf, and buying it ends the run at the next " +
            "battle — see PerkDraftEngine.IsOfferable");
    }

    /// <summary>A run-less slice still throws, the same guard every other run command hits.</summary>
    [Fact]
    public void A_run_less_slice_throws_rather_than_rejects()
    {
        Should.Throw<InvalidOperationException>(() =>
                SlayIdleRepeat.Core.GameRules.Apply(
                    Worlds.OutsideARun(), new ShopBuyCommand(0), Worlds.Context))
            .Message.ShouldContain("SHOP_BUY", Case.Sensitive);
    }
}
