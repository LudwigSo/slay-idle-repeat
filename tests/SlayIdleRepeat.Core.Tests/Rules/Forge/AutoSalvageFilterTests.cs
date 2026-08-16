using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Forge;

/// <summary>
/// Which of a player's stored items their auto-salvage filter sweeps — the design set's own example
/// read literally: <em>salvage all C and B below +3</em>.
/// </summary>
public sealed class AutoSalvageFilterTests
{
    /// <summary>The example filter, as the design set writes it.</summary>
    private static IReadOnlyList<AutoSalvageRule> AllCommonAndUncommonBelowThree { get; } =
        [new AutoSalvageRule(Rarity.C, 3), new AutoSalvageRule(Rarity.B, 3)];

    /// <summary>
    /// 🔒 An empty filter sweeps nothing. A quality-of-life feature that deleted a player's stock
    /// the moment it was switched on would not be one.
    /// </summary>
    [Fact]
    public void A_filter_with_no_rows_sweeps_nothing()
    {
        var stock = Inventories.Holding(
            Inventories.Item("a", rarity: Rarity.C),
            Inventories.Item("b", rarity: Rarity.SS));

        AutoSalvageFilter.Select(stock, []).ShouldBeEmpty();
    }

    /// <summary>The example filter sweeps the two bands it names and leaves the three it does not.</summary>
    [Fact]
    public void A_band_the_filter_carries_no_row_for_is_never_swept()
    {
        var stock = Inventories.Holding(
            Inventories.Item("common", rarity: Rarity.C),
            Inventories.Item("uncommon", rarity: Rarity.B),
            Inventories.Item("rare", rarity: Rarity.A),
            Inventories.Item("epic", rarity: Rarity.S),
            Inventories.Item("mythic", rarity: Rarity.SS));

        AutoSalvageFilter.Select(stock, AllCommonAndUncommonBelowThree)
            .Select(id => id.Value)
            .ShouldBe(["common", "uncommon"]);
    }

    /// <summary>
    /// 🔒 The ceiling is EXCLUSIVE. An item sitting exactly at the level a player chose as their
    /// keep-line survives; sweeping it would delete the item they had just enhanced to it.
    /// </summary>
    /// <param name="level">The level the item stands at.</param>
    /// <param name="swept">Whether the filter takes it.</param>
    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void An_item_at_the_ceiling_survives_and_one_below_it_does_not(int level, bool swept)
    {
        var stock = Inventories.Holding(
            Inventories.Item("item", rarity: Rarity.C, enhanceLevel: level));

        AutoSalvageFilter.Select(stock, AllCommonAndUncommonBelowThree)
            .Count.ShouldBe(swept ? 1 : 0);
    }

    /// <summary>🔒 A locked item is locked precisely to be excluded from this.</summary>
    [Fact]
    public void A_locked_item_is_never_swept()
    {
        var stock = Inventories.Holding(
            Inventories.Item("locked", rarity: Rarity.C, locked: true),
            Inventories.Item("loose", rarity: Rarity.C));

        AutoSalvageFilter.Select(stock, AllCommonAndUncommonBelowThree)
            .Select(id => id.Value)
            .ShouldBe(["loose"]);
    }

    /// <summary>
    /// 🔒 An item waiting in overflow is not acted on at all. Every other operation on a held item
    /// is refused, and a filter that quietly destroyed one would be the single exception.
    /// </summary>
    [Fact]
    public void An_item_waiting_in_overflow_is_never_swept()
    {
        var capacity = Inventories.Tuning.CapacityAt(0);
        var stock = Inventories.Empty();

        foreach (var item in Inventories.Fill(capacity, "stored"))
        {
            stock.Place(item, Inventories.Tuning);
        }

        stock.Place(Inventories.Item("held", rarity: Rarity.C), Inventories.Tuning);

        stock.Held.Count.ShouldBe(1, "the fixture has to actually overflow for this to assert anything");

        AutoSalvageFilter.Select(stock, [new AutoSalvageRule(Rarity.C, 1)])
            .ShouldNotContain(new GearInstanceId("held"));
    }

    /// <summary>A rule whose ceiling is zero matches nothing — how a player turns one band off.</summary>
    [Fact]
    public void A_row_with_a_zero_ceiling_sweeps_nothing()
    {
        var stock = Inventories.Holding(Inventories.Item("item", rarity: Rarity.C));

        AutoSalvageFilter.Select(stock, [new AutoSalvageRule(Rarity.C, 0)]).ShouldBeEmpty();
    }

    /// <summary>Two rows for one band both count: neither is silently ignored.</summary>
    [Fact]
    public void Two_rows_for_one_band_sweep_on_either_of_them()
    {
        var stock = Inventories.Holding(Inventories.Item("item", rarity: Rarity.C, enhanceLevel: 4));

        AutoSalvageFilter.Select(
                stock, [new AutoSalvageRule(Rarity.C, 1), new AutoSalvageRule(Rarity.C, 9)])
            .Count.ShouldBe(1);
    }

    /// <summary>An identity appears once however many rows match it.</summary>
    [Fact]
    public void An_item_two_rows_match_is_named_once()
    {
        var stock = Inventories.Holding(Inventories.Item("item", rarity: Rarity.C));

        AutoSalvageFilter.Select(
                stock, [new AutoSalvageRule(Rarity.C, 3), new AutoSalvageRule(Rarity.C, 5)])
            .Count.ShouldBe(1);
    }

    /// <summary>The answer is stock order, so the same stock and filter always produce the same list.</summary>
    [Fact]
    public void The_sweep_answers_in_stock_order()
    {
        var stock = Inventories.Holding(
            Inventories.Item("third", rarity: Rarity.C),
            Inventories.Item("first", rarity: Rarity.C),
            Inventories.Item("second", rarity: Rarity.C));

        AutoSalvageFilter.Select(stock, AllCommonAndUncommonBelowThree)
            .Select(id => id.Value)
            .ShouldBe(["third", "first", "second"]);
    }
}
