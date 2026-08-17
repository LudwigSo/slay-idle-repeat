using Shouldly;
using SlayIdleRepeat.Core.Rules.Forge;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Forge;

/// <summary>
/// One enhancement attempt: the ceiling, the stat ladder it climbs, and what an attempt leaves
/// behind whichever way it goes.
/// </summary>
public sealed class GearEnhancementTests
{
    /// <summary>An item below the ceiling can be attempted; one at it cannot.</summary>
    /// <param name="level">The level the item stands at.</param>
    /// <param name="atCeiling">Whether no attempt is possible.</param>
    [Theory]
    [InlineData(0, false)]
    [InlineData(14, false)]
    [InlineData(15, true)]
    public void An_item_at_the_ceiling_can_be_enhanced_no_further(int level, bool atCeiling)
    {
        GearEnhancement.IsAtCeiling(level, Forges.Tuning).ShouldBe(atCeiling);
    }

    /// <summary>
    /// 🔒 The stat ladder is additive at 7% a level, and the ceiling is worth exactly the published
    /// total. An off-by-one in the level arithmetic would land on 1.98 or 2.12, neither of which is
    /// the authored figure.
    /// </summary>
    /// <param name="level">The level the item stands at.</param>
    /// <param name="multiplier">What its base stats are worth there.</param>
    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 1.07)]
    [InlineData(10, 1.7)]
    // The ceiling's row is the document's own published total rather than a literal repeated here:
    // 08 §4.2 authors totalMultiplierAtMax beside the per-level bonus, so this row is the rule's
    // arithmetic checked against a number the design set states independently of it.
    [InlineData(15, Content.ForgeDocuments.ShippedTotalMultiplierAtMax)]
    public void The_stat_multiplier_climbs_seven_percent_a_level_to_the_published_total(
        int level, double multiplier)
    {
        GearEnhancement.StatMultiplier(level, Forges.Tuning).ShouldBe(multiplier);
    }

    /// <summary>A level outside the authored range has no multiplier to answer with.</summary>
    /// <param name="level">The level asked about.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    public void A_level_outside_the_authored_range_has_no_multiplier(int level)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => _ = GearEnhancement.StatMultiplier(level, Forges.Tuning));
    }

    /// <summary>
    /// The first five levels are certain, so an attempt there succeeds however the draw lands. Both
    /// ends of the draw are exercised by the two seeds.
    /// </summary>
    /// <param name="seed">The draw seed.</param>
    [Theory]
    [InlineData(1UL)]
    [InlineData(9_999UL)]
    public void An_attempt_inside_the_certain_band_always_lands(ulong seed)
    {
        var item = Inventories.Item("blade", enhanceLevel: 2);

        var (enhanced, succeeded, rate) =
            GearEnhancement.Attempt(item, GearEnhancement.NoLuckyBonus, Forges.Tuning, Forges.Mercy, Forges.Draws(seed));

        rate.ShouldBe(1.0);
        succeeded.ShouldBeTrue();
        enhanced.EnhanceLevel.ShouldBe(3);
    }

    /// <summary>
    /// 🔒 A failure costs the stones and nothing else: the level stands, the affixes stand, the
    /// lock stands, and only the mercy counter moves.
    /// </summary>
    [Fact]
    public void A_failed_attempt_changes_nothing_but_the_mercy_counter()
    {
        var item = Inventories.Item("blade", enhanceLevel: 14, locked: true);

        var (enhanced, succeeded, _) = Failing(item);

        succeeded.ShouldBeFalse();
        enhanced.EnhanceLevel.ShouldBe(14);
        enhanced.EnhanceFailures.ShouldBe(1);
        enhanced.Locked.ShouldBeTrue();
        enhanced.Quality.ShouldBe(item.Quality);
        enhanced.Rarity.ShouldBe(item.Rarity);
        enhanced.InstanceId.ShouldBe(item.InstanceId);
    }

    /// <summary>A success clears the run of failures the item had built up.</summary>
    [Fact]
    public void A_successful_attempt_clears_the_mercy_counter()
    {
        var item = Inventories.Item("blade", enhanceLevel: 0, enhanceFailures: 4);

        var (enhanced, succeeded, _) =
            GearEnhancement.Attempt(item, GearEnhancement.NoLuckyBonus, Forges.Tuning, Forges.Mercy, Forges.Draws());

        succeeded.ShouldBeTrue();
        enhanced.EnhanceFailures.ShouldBe(0);
    }

    /// <summary>
    /// The chance an attempt is drawn against is the level's authored one, raised by the item's own
    /// run of failures.
    /// </summary>
    [Fact]
    public void The_effective_rate_is_the_levels_own_chance_raised_by_the_items_mercy()
    {
        GearEnhancement.EffectiveRate(10, 0, GearEnhancement.NoLuckyBonus, Forges.Tuning, Forges.Mercy)
            .ShouldBe(0.5);

        GearEnhancement.EffectiveRate(10, 3, GearEnhancement.NoLuckyBonus, Forges.Tuning, Forges.Mercy)
            .ShouldBe(0.74, 1e-12);
    }

    /// <summary>The whole ladder is reachable — no level answers a chance the tuning does not author.</summary>
    /// <param name="level">The level standing before the attempt.</param>
    /// <param name="rate">The chance the attempt has with no mercy.</param>
    [Theory]
    [InlineData(4, 1.0)]
    [InlineData(5, 0.85)]
    [InlineData(9, 0.65)]
    [InlineData(10, 0.5)]
    [InlineData(11, 0.4375)]
    [InlineData(14, 0.25)]
    public void Every_level_below_the_ceiling_carries_the_chance_the_ladder_authors(
        int level, double rate)
    {
        GearEnhancement.EffectiveRate(level, 0, GearEnhancement.NoLuckyBonus, Forges.Tuning, Forges.Mercy)
            .ShouldBe(rate);
    }

    /// <summary>An item at the ceiling has no next level to price a chance for.</summary>
    [Fact]
    public void An_item_at_the_ceiling_has_no_chance_to_answer()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _ = GearEnhancement.EffectiveRate(
            15, 0, GearEnhancement.NoLuckyBonus, Forges.Tuning, Forges.Mercy));
    }

    /// <summary>An attempt at a level nothing can reach is a defect in the caller, not a rejection.</summary>
    [Fact]
    public void Attempting_past_the_ceiling_is_refused_loudly()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _ = GearEnhancement.Attempt(
            Inventories.Item("blade", enhanceLevel: 15),
            GearEnhancement.NoLuckyBonus,
            Forges.Tuning,
            Forges.Mercy,
            Forges.Draws()));
    }

    /// <summary>
    /// An attempt consumes exactly one draw index whichever way it goes.
    /// </summary>
    /// <remarks>
    /// 🔴 The two outcomes are pinned as well as the two positions. Two attempts that both LANDED
    /// would satisfy the position assertions identically, so without them this case would say
    /// nothing about the failure half its name claims.
    /// </remarks>
    [Fact]
    public void An_attempt_consumes_one_draw_index_whether_it_lands_or_not()
    {
        var certain = Forges.Draws();
        var landed = GearEnhancement.Attempt(
            Inventories.Item("blade", enhanceLevel: 0),
            GearEnhancement.NoLuckyBonus,
            Forges.Tuning,
            Forges.Mercy,
            certain);

        var risky = Forges.Draws(FailingSeed);
        var missed = GearEnhancement.Attempt(
            Inventories.Item("blade", enhanceLevel: 14),
            GearEnhancement.NoLuckyBonus,
            Forges.Tuning,
            Forges.Mercy,
            risky);

        landed.Succeeded.ShouldBeTrue("the first five levels are certain");
        missed.Succeeded.ShouldBeFalse("the seed was chosen precisely because this attempt fails");

        certain.Position.ShouldBe(1UL);
        risky.Position.ShouldBe(certain.Position);
    }

    /// <summary>
    /// A seed whose first draw lands above the hardest level's chance — walked for rather than
    /// guessed, so the cases that need a failure fail loudly instead of asserting over a success.
    /// </summary>
    private static ulong FailingSeed { get; } = FirstFailingSeed();

    /// <summary>
    /// One attempt at the hardest level, on a seed whose draw lands above the chance — found by
    /// walking seeds rather than asserted, so the case fails loudly if none of them fails.
    /// </summary>
    private static (Core.Model.Gear.GearInstance Item, bool Succeeded, double Rate) Failing(
        Core.Model.Gear.GearInstance item) =>
        GearEnhancement.Attempt(
            item, GearEnhancement.NoLuckyBonus, Forges.Tuning, Forges.Mercy, Forges.Draws(FailingSeed));

    private static ulong FirstFailingSeed()
    {
        var unenhanced = Inventories.Item("probe", enhanceLevel: 14);

        for (var seed = 1UL; seed < 200UL; seed++)
        {
            var attempt = GearEnhancement.Attempt(
                unenhanced,
                GearEnhancement.NoLuckyBonus,
                Forges.Tuning,
                Forges.Mercy,
                Forges.Draws(seed));

            if (!attempt.Succeeded)
            {
                return seed;
            }
        }

        throw new InvalidOperationException(
            "No seed under two hundred failed an attempt at the hardest level, whose authored chance " +
            "is one in four. Either the draw is not being consulted or the ladder has moved, and " +
            "either way the failure cases above would be asserting over a success.");
    }
}
