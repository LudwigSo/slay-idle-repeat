using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Gear;

/// <summary>
/// <c>GearStatKinds</c> — the unit and the polarity of every stat a gear roll can write.
/// </summary>
/// <remarks>
/// Driven directly rather than through <c>InventoryView</c>: the public door reaches only the stats the
/// shipped affix pool writes, and the table has to be right for the arms no affix reaches yet — the
/// day one does, this is the case that says which way its roll is written.
/// </remarks>
public sealed class GearStatKindsTests
{
    [Theory]
    [InlineData(StatId.MAX_HP, false)]
    [InlineData(StatId.ATK, false)]
    [InlineData(StatId.DEF, false)]
    [InlineData(StatId.THORNS, false)]
    [InlineData(StatId.RARITY_SHIFT, false)]
    [InlineData(StatId.ASPD, true)]
    [InlineData(StatId.CRIT, true)]
    [InlineData(StatId.DR_PCT, true)]
    [InlineData(StatId.GOLD_PCT, true)]
    public void A_stat_is_an_amount_or_a_share(StatId stat, bool share) =>
        GearStatKinds.IsShare(stat).ShouldBe(
            share, "attack speed follows tuning/drops.json#/percentStatsByRarity, which lists it as a percent stat.");

    [Fact]
    public void Every_stat_is_classified() =>
        Should.NotThrow(() =>
        {
            foreach (var stat in Enum.GetValues<StatId>())
            {
                _ = GearStatKinds.IsShare(stat);
                _ = GearStatKinds.LowerIsBetter(stat);
            }
        });

    [Theory]
    [InlineData(StatId.DR_PCT, -0.05, true)]
    [InlineData(StatId.DR_PCT, 0.05, false)]
    [InlineData(StatId.SHOP_PRICE_PCT, -0.1, true)]
    [InlineData(StatId.CRIT, 0.05, true)]
    [InlineData(StatId.CRIT, -0.05, false)]
    public void A_roll_is_favourable_when_it_moves_the_stat_the_way_a_player_wants(StatId stat, double value, bool favourable) =>
        GearStatKinds.IsFavourable(stat, value).ShouldBe(favourable);

    [Fact]
    public void A_percent_bucket_is_a_share_whatever_the_stat() =>
        GearStatKinds.IsShare(StatId.MAX_HP, EffectOp.STAT_ADD_PCT).ShouldBeTrue();
}
