using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Every shop pricing number is read from <c>game-data/tuning/currencies.json</c>, never written as
/// a C# constant. This is the reader.
/// </summary>
public sealed class ShopTuningTests
{
    [Fact]
    public void Every_shop_number_comes_from_the_currencies_document()
    {
        var tuning = ShopTuning.Read(CurrenciesDocuments.Shipped);

        tuning.Slots.ShouldBe(CurrenciesDocuments.ShippedSlots);
        tuning.FreeRefreshesPerVisit.ShouldBe(CurrenciesDocuments.ShippedFreeRefreshesPerVisit);
        tuning.StagePriceStep.ShouldBe(CurrenciesDocuments.ShippedStagePriceStep);
        tuning.ChapterPriceScalar.ShouldBe(CurrenciesDocuments.ShippedChapterPriceScalar);
        tuning.HealBasePrice.ShouldBe(CurrenciesDocuments.ShippedHealBasePrice);
        tuning.HealPctMaxHp.ShouldBe(CurrenciesDocuments.ShippedHealPctMaxHp);
        tuning.LeftoverGoldAlarmShare.ShouldBe(CurrenciesDocuments.ShippedLeftoverGoldAlarmShare);

        tuning.PerkBasePrice(ShopRarity.COMMON).ShouldBe(180);
        tuning.PerkBasePrice(ShopRarity.RARE).ShouldBe(320);
        tuning.PerkBasePrice(ShopRarity.EPIC).ShouldBe(560);
        tuning.PerkBasePrice(ShopRarity.LEGENDARY).ShouldBe(950);

        tuning.ConsumableBasePrice("CON_HEALTH_DRAUGHT").ShouldBe(140);
        tuning.ConsumableBasePrice("CON_REROLL_TOKEN").ShouldBe(120);
        tuning.ConsumableBasePrice("CON_DRAFT_TOKEN").ShouldBe(160);
        tuning.ConsumableBasePrice("CON_ESCAPE_ROPE").ShouldBe(100);

        tuning.RunBuffBasePrice("WHETSTONE").ShouldBe(300);
        tuning.RunBuffBasePrice("HEARTROOT_TONIC").ShouldBe(300);
        tuning.RunBuffBasePrice("HAWKS_EYE").ShouldBe(280);

        tuning.RunBuffs.Count.ShouldBe(3);
        tuning.RunBuffs[0].Id.ShouldBe("WHETSTONE");
        tuning.RunBuffs[0].Stat.ShouldBe("ATK");
        tuning.RunBuffs[0].Base.ShouldBe(12m);
        tuning.RunBuffs[0].ChapterGrowth.ShouldBe(2.0m);
        tuning.RunBuffs[2].Id.ShouldBe("HAWKS_EYE");
        tuning.RunBuffs[2].Stat.ShouldBe("CRIT");
        tuning.RunBuffs[2].ChapterGrowth.ShouldBe(1.0m, "chapter-invariant: flat +4% at every chapter.");
    }

    [Fact]
    public void An_unknown_perk_rarity_is_refused()
    {
        var tuning = ShopTuning.Read(CurrenciesDocuments.Shipped);

        Should.Throw<ArgumentOutOfRangeException>(() => tuning.PerkBasePrice((ShopRarity)999));
    }

    [Fact]
    public void An_unknown_consumable_id_is_refused()
    {
        var tuning = ShopTuning.Read(CurrenciesDocuments.Shipped);

        Should.Throw<ArgumentException>(() => tuning.ConsumableBasePrice("CON_NOT_A_REAL_CONSUMABLE"));
    }

    [Fact]
    public void An_unknown_run_buff_id_is_refused()
    {
        var tuning = ShopTuning.Read(CurrenciesDocuments.Shipped);

        Should.Throw<ArgumentException>(() => tuning.RunBuffBasePrice("NOT_A_REAL_BUFF"));
    }

    [Fact]
    public void An_absent_shop_block_fails_loudly_rather_than_reading_zero()
    {
        var content = CurrenciesDocuments.Document(ContentValue.EmptyObject);

        var thrown = Should.Throw<MissingContentException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.SlotsReference);
    }

    [Fact]
    public void An_absent_currencies_document_fails_loudly()
    {
        var content = new ContentSnapshot(
            ContentVersion.FromHex(new string('d', ContentVersion.HexLength)),
            []);

        var thrown = Should.Throw<MissingContentException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.DocumentPath);
    }

    [Fact]
    public void An_unauthorised_slots_hole_is_never_read_as_a_default()
    {
        var content = CurrenciesDocuments.With(slots: ContentValue.Unauthorised);

        var thrown = Should.Throw<UnauthorisedTunableException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.SlotsReference);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_slot_count_is_refused(int authored)
    {
        var content = CurrenciesDocuments.With(slots: ContentValue.Number(authored));

        var thrown = Should.Throw<InvalidTunableException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.SlotsReference);
    }

    [Fact]
    public void A_negative_free_refresh_count_is_refused()
    {
        var content = CurrenciesDocuments.With(freeRefreshesPerVisit: ContentValue.Number(-1));

        var thrown = Should.Throw<InvalidTunableException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.FreeRefreshesPerVisitReference);
    }

    [Fact]
    public void A_zero_free_refresh_count_is_legal()
    {
        var tuning = ShopTuning.Read(CurrenciesDocuments.With(freeRefreshesPerVisit: ContentValue.Number(0)));

        tuning.FreeRefreshesPerVisit.ShouldBe(0);
    }

    [Fact]
    public void A_negative_stage_price_step_is_refused()
    {
        var content = CurrenciesDocuments.With(stagePriceStep: ContentValue.Number(-0.1m));

        var thrown = Should.Throw<InvalidTunableException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.StagePriceStepReference);
    }

    [Fact]
    public void A_zero_stage_price_step_is_legal()
    {
        var tuning = ShopTuning.Read(CurrenciesDocuments.With(stagePriceStep: ContentValue.Number(0)));

        tuning.StagePriceStep.ShouldBe(0m);
    }

    [Fact]
    public void An_empty_chapter_price_scalar_array_is_refused()
    {
        var content = CurrenciesDocuments.With(chapterPriceScalar: ContentValue.EmptyArray);

        var thrown = Should.Throw<InvalidTunableException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.ChapterPriceScalarReference);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_chapter_price_scalar_entry_is_refused(int authored)
    {
        var content = CurrenciesDocuments.With(
            chapterPriceScalar: ContentValue.Array(new[] { ContentValue.Number(authored) }));

        Should.Throw<InvalidTunableException>(() => ShopTuning.Read(content));
    }

    [Fact]
    public void A_negative_heal_base_price_is_refused()
    {
        var content = CurrenciesDocuments.With(healBasePrice: ContentValue.Number(-1));

        var thrown = Should.Throw<InvalidTunableException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.HealBasePriceReference);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1.5)]
    public void A_heal_percentage_outside_the_open_zero_to_one_range_is_refused(double authored)
    {
        var content = CurrenciesDocuments.With(healPctMaxHp: ContentValue.Number((decimal)authored));

        var thrown = Should.Throw<InvalidTunableException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.HealPctMaxHpReference);
    }

    [Fact]
    public void A_heal_percentage_of_exactly_one_is_legal()
    {
        var tuning = ShopTuning.Read(CurrenciesDocuments.With(healPctMaxHp: ContentValue.Number(1m)));

        tuning.HealPctMaxHp.ShouldBe(1m);
    }

    [Fact]
    public void An_empty_run_buffs_array_is_refused()
    {
        var content = CurrenciesDocuments.With(runBuffs: ContentValue.EmptyArray);

        var thrown = Should.Throw<InvalidTunableException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.RunBuffsReference);
    }

    [Fact]
    public void An_empty_perk_base_price_table_is_refused()
    {
        var content = CurrenciesDocuments.With(perkBasePrice: ContentValue.EmptyObject);

        Should.Throw<MissingContentException>(() => ShopTuning.Read(content));
    }

    [Fact]
    public void A_negative_consumable_base_price_is_refused()
    {
        var content = CurrenciesDocuments.With(consumableBasePrice: ContentValue.Object(
            new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["CON_HEALTH_DRAUGHT"] = ContentValue.Number(-5),
            }));

        var thrown = Should.Throw<InvalidTunableException>(() => ShopTuning.Read(content));

        thrown.Reference.ShouldBe(ShopTuning.ConsumableBasePriceReference + "/CON_HEALTH_DRAUGHT");
    }
}
