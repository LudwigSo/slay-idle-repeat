using Shouldly;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <c>MERGE</c> end to end: the identities, the legality, the price, and what the stock holds
/// afterwards.
/// </summary>
public sealed class MergeTests
{
    private static GameContext Context => Worlds.Drawing(ForgeWorlds.Seed);

    private static MergeCommand Command(params string[] ids) =>
        new(ids.Select(id => new GearInstanceId(id)).ToArray(), dustSubstituted: false);

    /// <summary>Three fusable items, optionally with one of them locked.</summary>
    /// <param name="rarity">The band all three share.</param>
    /// <param name="enhanceLevel">The level all three share.</param>
    /// <param name="lockedPosition">
    /// Which of the three carries a lock, counted from zero, or <c>-1</c> for none. A position rather
    /// than a flag: locking the first input alone is satisfied by a handler that reads
    /// <c>InputItemIds[0]</c> and stops.
    /// </param>
    private static GearInstance[] Triple(
        Rarity rarity = Rarity.C, int enhanceLevel = 0, int lockedPosition = -1) =>
    [
        Inventories.Item("a", rarity: rarity, enhanceLevel: enhanceLevel, locked: lockedPosition == 0),
        Inventories.Item("b", rarity: rarity, enhanceLevel: enhanceLevel, locked: lockedPosition == 1),
        Inventories.Item("c", rarity: rarity, enhanceLevel: enhanceLevel, locked: lockedPosition == 2),
    ];

    [Fact]
    public void A_legal_fusion_leaves_one_item_of_the_next_band_where_three_stood()
    {
        var result = GameRules.Apply(ForgeWorlds.Holding(Triple()), Command("a", "b", "c"), Context);

        result.Accepted.ShouldBeTrue();

        var stock = result.NewState.Player.Inventory.Stored;

        stock.Count.ShouldBe(1);
        stock[0].InstanceId.ShouldBe(new GearInstanceId("a"));
        stock[0].Rarity.ShouldBe(Rarity.B);
    }

    /// <remarks>
    /// The case wears <c>b</c>, not <c>a</c>: a fusion keeps the first input's identity, so a hero
    /// wearing <c>a</c> would pass against the defect. A failure looks like a throw out of Apply —
    /// <c>Player.RequireLoadoutResolves</c> treats a slot naming a destroyed item as a handler defect.
    /// </remarks>
    [Fact]
    public void Merging_away_a_worn_input_takes_it_off_the_hero()
    {
        var world = ForgeWorlds.Wearing((GearSlot.WEAPON, "b"), Triple());

        var result = GameRules.Apply(world, Command("a", "b", "c"), Context);

        result.Accepted.ShouldBeTrue(
            "fusing away an item the hero happens to be wearing is a legal action. A throw out of " +
            "Apply means the input was destroyed with the weapon slot still naming it.");

        result.NewState.Player.Loadout.TryGet(GearSlot.WEAPON, out _).ShouldBeFalse(
            "the consumed input was destroyed and the weapon slot still names it, so the hero is " +
            "wearing an item the stock no longer holds.");
    }

    /// <summary>
    /// The whole stock is compared by canonical bytes, not by record equality: a synthesized
    /// <c>Equals</c> compares a collection component by reference.
    /// </summary>
    /// <remarks>
    /// The expected affixes are spelled out rather than read off the answer — the affixes are the
    /// only thing <c>GearMerge.Fuse</c> draws, so an expectation read back off the handler restates
    /// its own answer to itself.
    /// </remarks>
    [Fact]
    public void The_stock_after_a_fusion_is_exactly_the_output_beside_what_was_untouched()
    {
        var before = ForgeWorlds.Holding(
            [.. Triple(), Inventories.Item("spectator", rarity: Rarity.S)]);

        var after = GameRules.Apply(before, Command("a", "b", "c"), Context);

        var expected = ForgeWorlds.Holding(
            Inventories.Item("a", rarity: Rarity.B, affixes: ExpectedFusionAffixes),
            Inventories.Item("spectator", rarity: Rarity.S));

        ForgeWorlds.StockBytes(after.NewState).ShouldBe(ForgeWorlds.StockBytes(expected));
    }

    /// <remarks>
    /// The same draw as the byte comparison above, in a readable shape: a change to the draw reports
    /// which affix moved and by how much, instead of a byte diff nobody can read.
    /// </remarks>
    [Fact]
    public void A_fusion_draws_the_output_bands_affixes_from_the_output_slots_pool()
    {
        var after = GameRules.Apply(ForgeWorlds.Holding(Triple()), Command("a", "b", "c"), Context);

        var fused = after.NewState.Player.Inventory.Stored[0];

        fused.Affixes.Select(affix => (affix.AffixId, affix.Value)).ShouldBe(
            ExpectedFusionAffixes.Select(affix => (affix.AffixId, affix.Value)),
            "the fusion is the only draw a MERGE makes, and this is what ForgeWorlds.Seed buys on the " +
            "B-band pool of the WEAPON slot.");

        fused.Affixes.Count.ShouldBe(
            Inventories.Drops.Band(Rarity.B).AffixCount,
            "the OUTPUT band's affix count, asked of the tables rather than restated — a fusion " +
            "rolling the input band's count is the defect the literal above would otherwise merely " +
            "record.");
    }

    /// <summary>
    /// What <c>GearMerge.Fuse</c> rolls for a C-band <c>BLADE</c> trio fusing onto <c>B</c>, on
    /// <see cref="ForgeWorlds.Seed"/>. Transcribed once; if the draw derivation legitimately moves,
    /// re-transcribe it — never read it back off <c>Apply</c>.
    /// </summary>
    private static readonly GearAffixRoll[] ExpectedFusionAffixes =
    [
        new("AFX_CRIT_CHANCE", 0.0491),
    ];

    [Theory]
    [InlineData(Rarity.C, 120)]
    [InlineData(Rarity.B, 600)]
    [InlineData(Rarity.A, 3000)]
    [InlineData(Rarity.S, 15000)]
    public void A_fusion_charges_the_Crown_price_of_the_band_it_lands_on(Rarity rarity, long crowns)
    {
        var result = GameRules.Apply(
            ForgeWorlds.Holding(Triple(rarity)), Command("a", "b", "c"), Context);

        ForgeWorlds.Moved(result.Events, CurrencyId.CROWNS).ShouldBe(-crowns);
    }

    [Fact]
    public void A_fusion_without_dust_moves_no_dust()
    {
        var result = GameRules.Apply(
            ForgeWorlds.Holding(Triple()), Command("a", "b", "c"), Context);

        ForgeWorlds.Moved(result.Events, CurrencyId.MERGE_DUST).ShouldBe(0);
    }

    /// <summary>The dust price is the INPUT band's, on top of the Crowns.</summary>
    [Fact]
    public void A_dust_filled_slot_charges_the_dust_price_of_the_input_band()
    {
        var world = ForgeWorlds.Holding(
            Inventories.Item("a", rarity: Rarity.A),
            Inventories.Item("b", rarity: Rarity.A));

        var command = new MergeCommand(
            [new GearInstanceId("a"), new GearInstanceId("b")], dustSubstituted: true);

        var result = GameRules.Apply(world, command, Context);

        result.Accepted.ShouldBeTrue();
        ForgeWorlds.Moved(result.Events, CurrencyId.MERGE_DUST).ShouldBe(-800);
        ForgeWorlds.Moved(result.Events, CurrencyId.CROWNS).ShouldBe(-3000);
        result.NewState.Player.Inventory.Stored.Count.ShouldBe(1);
    }

    [Fact]
    public void An_identity_the_player_does_not_own_is_refused_as_not_owned()
    {
        var result = GameRules.Apply(
            ForgeWorlds.Holding(Triple()), Command("a", "b", "nobody"), Context);

        result.Rejection.ShouldBe(RejectionReason.NOT_OWNED);
    }

    /// <remarks>
    /// Every position, because the first alone proves nothing: a handler inspecting
    /// <c>InputItemIds[0]</c> and stopping passes a lock on <c>"a"</c> while fusing away two locked
    /// items — and positions 1 and 2 are the consumed ones, where the loss is permanent.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_locked_input_is_refused_wherever_the_command_names_it(int lockedPosition)
    {
        var result = GameRules.Apply(
            ForgeWorlds.Holding(Triple(lockedPosition: lockedPosition)),
            Command("a", "b", "c"),
            Context);

        result.Rejection.ShouldBe(
            RejectionReason.ILLEGAL_STATE,
            $"input {lockedPosition} of three is locked and the fusion was accepted, so the lock " +
            "protects only the position the handler happens to look at.");
    }

    /// <summary>
    /// An overflow item is neither unknown nor locked; collapsing it onto either code would show the
    /// player the wrong thing to do about it.
    /// </summary>
    [Fact]
    public void An_input_waiting_in_overflow_is_refused_as_inventory_full()
    {
        var world = ForgeWorlds.Overflowing(Inventories.Item("held"));

        var result = GameRules.Apply(world, Command("held"), Context);

        result.Rejection.ShouldBe(RejectionReason.INVENTORY_FULL);
    }

    [Theory]
    [InlineData("a,b")]
    [InlineData("a,b,b")]
    [InlineData("a,b,c,d")]
    public void A_selection_that_is_not_a_legal_fusion_is_refused(string ids)
    {
        var world = ForgeWorlds.Holding([.. Triple(), Inventories.Item("d")]);

        GameRules.Apply(world, Command(ids.Split(',')), Context)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    [Fact]
    public void A_wallet_that_does_not_cover_the_price_is_refused_as_a_shortfall()
    {
        var world = ForgeWorlds.Holding(
            PlayerSnapshots.Wallet((CurrencyId.CROWNS, 119)), Triple());

        GameRules.Apply(world, Command("a", "b", "c"), Context)
            .Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
    }

    [Fact]
    public void Dust_the_player_does_not_hold_is_refused_as_a_shortfall()
    {
        var world = ForgeWorlds.Holding(
            PlayerSnapshots.Wallet((CurrencyId.CROWNS, 1_000_000), (CurrencyId.MERGE_DUST, 49)),
            Inventories.Item("a"),
            Inventories.Item("b"));

        var command = new MergeCommand(
            [new GearInstanceId("a"), new GearInstanceId("b")], dustSubstituted: true);

        GameRules.Apply(world, command, Context).Rejection.ShouldBe(RejectionReason.INSUFFICIENT_FUNDS);
    }

    /// <remarks>
    /// The expected bytes are taken BEFORE Apply: it hands the caller's own slice back on a
    /// rejection, so bytes read afterwards would compare the slice against itself.
    /// </remarks>
    [Fact]
    public void A_refused_fusion_leaves_the_stock_and_the_wallet_exactly_where_they_stood()
    {
        var world = ForgeWorlds.Holding(
            PlayerSnapshots.Wallet((CurrencyId.CROWNS, 119)), Triple());

        var before = ForgeWorlds.StockBytes(world);

        var result = GameRules.Apply(world, Command("a", "b", "c"), Context);

        result.Accepted.ShouldBeFalse();
        result.Events.ShouldBeEmpty();
        ForgeWorlds.StockBytes(result.NewState).ShouldBe(before);
        world.Player.BalanceOf(CurrencyId.CROWNS).ShouldBe(119);
    }

    [Fact]
    public void A_fusion_at_the_top_band_is_refused()
    {
        GameRules.Apply(
                ForgeWorlds.Holding(Triple(Rarity.SS)), Command("a", "b", "c"), Context)
            .Rejection.ShouldBe(RejectionReason.ILLEGAL_STATE);
    }

    /// <summary>
    /// Removing all three inputs and filing the output afterwards is not the same edit: the opened
    /// slots reclaim from overflow first and the paid-for item would land in overflow itself. The
    /// fixture holds three waiting items precisely so the two spellings part.
    /// </summary>
    [Fact]
    public void A_fusion_out_of_a_full_stock_leaves_its_output_stored_rather_than_in_overflow()
    {
        var world = ForgeWorlds.FullWithOverflow(held: 3, Triple());

        var result = GameRules.Apply(world, Command("a", "b", "c"), Context);

        result.Accepted.ShouldBeTrue();

        var stock = result.NewState.Player.Inventory;

        stock.Stored.ShouldContain(item => item.InstanceId == new GearInstanceId("a"));
        stock.Held.ShouldNotContain(item => item.InstanceId == new GearInstanceId("a"));
        stock.Stored.Count.ShouldBe(Inventories.Tuning.MaxCapacity);
        stock.Held.Count.ShouldBe(1, "two slots opened and two of the three waiting items took them");
    }

    [Fact]
    public void The_same_fusion_over_the_same_seed_produces_the_same_stock()
    {
        var first = GameRules.Apply(ForgeWorlds.Holding(Triple()), Command("a", "b", "c"), Context);
        var second = GameRules.Apply(ForgeWorlds.Holding(Triple()), Command("a", "b", "c"), Context);

        ForgeWorlds.StockBytes(second.NewState).ShouldBe(ForgeWorlds.StockBytes(first.NewState));
    }
}
