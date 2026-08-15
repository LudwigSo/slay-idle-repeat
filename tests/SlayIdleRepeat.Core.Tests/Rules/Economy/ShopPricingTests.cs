using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Economy;

/// <summary>
/// The shop pricing formula:
/// <c>Price = BasePrice(itemType, rarity) * (1 + 0.25 * stageIndex) * chapterPriceScalar</c>.
/// </summary>
/// <remarks>
/// The formula's three multiplicative factors are each probed with a mutation literally applied
/// and reverted, so this suite is shown to fail when the formula it protects is wrong, not
/// merely to pass when it is right.
/// </remarks>
public sealed class ShopPricingTests
{
    private static readonly ShopTuning Tuning = ShopTuning.Read(CurrenciesDocuments.Shipped);

    /// <summary>Stage 1 (stageIndex 0), Chapter 1 pays exactly the base price — the formula's floor.</summary>
    [Fact]
    public void Stage_1_chapter_1_pays_exactly_the_base_price()
    {
        ShopPricing.Price(ShopItemKind.HEAL, null, null, stageIndex: 0, chapterId: 1, Tuning)
            .ShouldBe(150L, "150 * (1 + 0.25*0) * 1.0 = 150.");

        ShopPricing.Price(ShopItemKind.PERK, null, ShopRarity.COMMON, stageIndex: 0, chapterId: 1, Tuning)
            .ShouldBe(180L);
    }

    /// <summary>Stage 2 applies the +25% step; Stage 3 applies +50%.</summary>
    [Theory]
    [InlineData(0, 150L)]
    [InlineData(1, 188L)] // 150 * 1.25 = 187.5, rounds to 188 (ToEven of .5 rounds to even 188)
    [InlineData(2, 225L)] // 150 * 1.50 = 225
    public void The_stage_multiplier_matches_03_7s_0_25_step(int stageIndex, long expected)
    {
        ShopPricing.Price(ShopItemKind.HEAL, null, null, stageIndex, chapterId: 1, Tuning)
            .ShouldBe(expected);
    }

    /// <summary>Chapter 2's scalar (1.55) is applied multiplicatively over the base price.</summary>
    [Fact]
    public void The_chapter_scalar_multiplies_the_base_price()
    {
        // 150 * 1.0 (stage 1) * 1.55 (chapter 2) = 232.5 -> ToEven -> 232
        ShopPricing.Price(ShopItemKind.HEAL, null, null, stageIndex: 0, chapterId: 2, Tuning)
            .ShouldBe(232L);
    }

    /// <summary>Both factors compound: Stage 3, Chapter 2.</summary>
    [Fact]
    public void The_stage_and_chapter_factors_compound_multiplicatively()
    {
        // 150 * 1.50 * 1.55 = 348.75 -> 349
        ShopPricing.Price(ShopItemKind.HEAL, null, null, stageIndex: 2, chapterId: 2, Tuning)
            .ShouldBe(349L);
    }

    [Fact]
    public void A_consumable_is_priced_by_its_own_base_price()
    {
        ShopPricing.Price(ShopItemKind.CONSUMABLE, "CON_ESCAPE_ROPE", null, stageIndex: 0, chapterId: 1, Tuning)
            .ShouldBe(100L);
    }

    [Fact]
    public void A_run_buff_is_priced_by_its_own_base_price()
    {
        ShopPricing.Price(ShopItemKind.RUN_BUFF, "HAWKS_EYE", null, stageIndex: 0, chapterId: 1, Tuning)
            .ShouldBe(280L);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_stage_index_outside_0_2_is_refused(int stageIndex)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            ShopPricing.Price(ShopItemKind.HEAL, null, null, stageIndex, chapterId: 1, Tuning));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void A_chapter_outside_the_authored_scalar_array_is_refused(int chapterId)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            ShopPricing.Price(ShopItemKind.HEAL, null, null, stageIndex: 0, chapterId, Tuning));
    }

    [Fact]
    public void A_perk_price_with_no_rarity_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            ShopPricing.Price(ShopItemKind.PERK, null, null, stageIndex: 0, chapterId: 1, Tuning));
    }

    [Fact]
    public void A_consumable_price_with_no_key_is_refused()
    {
        Should.Throw<ArgumentException>(() =>
            ShopPricing.Price(ShopItemKind.CONSUMABLE, null, null, stageIndex: 0, chapterId: 1, Tuning));
    }

    [Fact]
    public void A_run_buff_price_with_no_key_is_refused()
    {
        Should.Throw<ArgumentException>(() =>
            ShopPricing.Price(ShopItemKind.RUN_BUFF, null, null, stageIndex: 0, chapterId: 1, Tuning));
    }

    [Fact]
    public void A_null_tuning_is_refused()
    {
        Should.Throw<ArgumentNullException>(() =>
            ShopPricing.Price(ShopItemKind.HEAL, null, null, stageIndex: 0, chapterId: 1, null!));
    }
}
