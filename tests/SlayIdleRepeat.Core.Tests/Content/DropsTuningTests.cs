using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The gear generation tables read out of <c>tuning/drops.json</c>: the rarity ladder, the
/// chapter-banded drop shares, the item-power coefficient and quality scales, the slot coefficients
/// and their percent-stat table, the affix pool with its two restrictions, and the set breakpoints.
/// </summary>
public sealed class DropsTuningTests
{
    /// <summary>Each band's multiplier on item power and the affixes it rolls.</summary>
    [Theory]
    [InlineData(Rarity.C, 1.0, 0)]
    [InlineData(Rarity.B, 1.55, 1)]
    [InlineData(Rarity.A, 2.4, 2)]
    [InlineData(Rarity.S, 3.8, 3)]
    [InlineData(Rarity.SS, 6.0, 4)]
    public void The_reader_answers_the_rarity_ladder_the_document_authors(
        Rarity rarity, double multiplier, int affixes)
    {
        var band = Tuning().Band(rarity);

        band.StatMultiplier.ShouldBe(multiplier);
        band.AffixCount.ShouldBe(affixes);
    }

    /// <summary>The ladder is five bands, and it is the five the vocabulary declares.</summary>
    [Fact]
    public void The_ladder_carries_one_row_per_declared_band()
    {
        var tuning = Tuning();

        tuning.BandCount.ShouldBe(
            Enum.GetValues<Rarity>().Length,
            "a band the drop table can produce and the ladder has no row for is an item the " +
            "generator cannot build at all.");
        tuning.Band(Rarity.SS).AffixCount.ShouldBe(GearDocuments.ShippedAffixCountSs);
    }

    /// <summary>A band with no row is refused rather than answered with a default.</summary>
    [Fact]
    public void Band_refuses_a_rarity_the_ladder_has_no_row_for()
    {
        Should.Throw<InvalidTunableException>(() => Tuning().Band((Rarity)9))
            .Reference.ShouldBe(DropsTuning.RaritiesReference);
    }

    /// <summary>A band authored twice is refused: which multiplier wins would be a reader accident.</summary>
    [Fact]
    public void A_band_authored_twice_is_refused()
    {
        var rarities = ContentValue.Array(
        [
            Row("C", 1.0m, 0),
            Row("C", 2.0m, 1),
        ]);

        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(rarities: rarities)))
            .Message.ShouldContain("twice", Case.Sensitive);
    }

    /// <summary>A band whose multiplier is not positive is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    public void A_band_whose_multiplier_is_not_positive_is_refused(double multiplier)
    {
        var rarities = ContentValue.Array([Row("C", (decimal)multiplier, 0)]);

        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(rarities: rarities)))
            .Reference.ShouldBe(DropsTuning.RaritiesReference + "/0/statMultiplier");
    }

    // ---------------------------------------------------------------- the chapter bands

    /// <summary>
    /// Every chapter band's shares add up to a hundred. The band count and the row count come first,
    /// because a sum over an empty band is zero and would satisfy this quantifying over nothing.
    /// </summary>
    [Fact]
    public void Every_chapter_bands_shares_add_up_to_a_hundred()
    {
        var tuning = Tuning();

        tuning.ChapterBandCount.ShouldBe(GearDocuments.ShippedChapterBands.Count);

        foreach (var chapter in EveryAuthoredChapter())
        {
            var shares = tuning.SharesFor(chapter);

            shares.Count.ShouldBe(
                Enum.GetValues<Rarity>().Length,
                $"chapter {chapter} must price every band, including the ones it prices at zero — a " +
                "band left out of the table is a rarity that silently cannot drop.");
            DeterminismRounding.Round(shares.Sum(share => share.Share)).ShouldBe(
                DropsTuning.ShareTotal,
                $"chapter {chapter}'s shares are percentages of one draw. A band that does not add up " +
                "still draws, at odds nobody authored.");
        }
    }

    /// <summary>Each chapter reads the band the document put it in.</summary>
    [Theory]
    [InlineData(1, 60.0, 0.3)]
    [InlineData(2, 60.0, 0.3)]
    [InlineData(3, 40.0, 0.6)]
    [InlineData(4, 40.0, 0.6)]
    [InlineData(5, 15.0, 2.0)]
    [InlineData(6, 15.0, 2.0)]
    [InlineData(7, 0.0, 5.0)]
    [InlineData(8, 0.0, 5.0)]
    public void SharesFor_answers_the_band_the_document_puts_a_chapter_in(
        int chapter, double bottom, double top)
    {
        var shares = Tuning().SharesFor(chapter);

        Share(shares, Rarity.C).ShouldBe(bottom);
        Share(shares, Rarity.SS).ShouldBe(top);
    }

    /// <summary>The last band prices the bottom rarity out entirely rather than omitting it.</summary>
    [Fact]
    public void The_last_chapter_band_prices_the_bottom_rarity_at_zero_rather_than_dropping_the_row()
    {
        var shares = Tuning().SharesFor(8);

        shares.Select(share => share.Rarity).ShouldContain(Rarity.C);
        Share(shares, Rarity.C).ShouldBe(0.0);
    }

    /// <summary>A chapter no band covers is refused by number rather than extrapolated.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-3)]
    public void SharesFor_refuses_a_chapter_no_band_covers(int chapter)
    {
        var thrown = Should.Throw<InvalidTunableException>(() => Tuning().SharesFor(chapter));

        thrown.Reference.ShouldBe(DropsTuning.DropShareReference);
        thrown.Message.ShouldContain(
            chapter.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Case.Sensitive,
            "extending the bands here would mean choosing that chapter's legendary rate on the " +
            "author's behalf, so the refusal has to say which chapter was asked for.");
    }

    /// <summary>A band that does not add up is refused at the read, with its total in the message.</summary>
    [Fact]
    public void A_chapter_band_that_does_not_add_up_is_refused()
    {
        var shares = ContentValue.Array(
        [
            GearDocuments.ShareBand(
                ContentValue.Number(1),
                ContentValue.Number(8),
                ("C", 59m), ("B", 27m), ("A", 10m), ("S", 2.7m), ("SS", 0.3m)),
        ]);

        var thrown = Should.Throw<InvalidTunableException>(
            () => DropsTuning.Read(GearDocuments.With(dropShares: shares)));

        thrown.Message.ShouldContain("99", Case.Sensitive);
        thrown.Message.ShouldContain(
            "disclosure",
            Case.Sensitive,
            "the total is checked because it is the one property of the table nothing else can " +
            "catch; the message has to say why rather than merely that the sum was wrong.");
    }

    /// <summary>A band covering no chapter at all is refused.</summary>
    [Fact]
    public void A_chapter_band_that_covers_no_chapter_is_refused()
    {
        var shares = ContentValue.Array(
        [
            GearDocuments.ShareBand(
                ContentValue.Number(4),
                ContentValue.Number(2),
                ("C", 100m)),
        ]);

        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(dropShares: shares)))
            .Message.ShouldContain("covers no chapter", Case.Sensitive);
    }

    // ---------------------------------------------------------------- item generation

    /// <summary>The item-power coefficient and the two asymmetric quality scales, as authored.</summary>
    /// <remarks>
    /// The asymmetry is the point: the secondary spans further than the primary, which is the whole
    /// reason two items of the same base, band and chapter are not the same item.
    /// </remarks>
    [Fact]
    public void The_reader_answers_the_item_generation_block_the_document_authors()
    {
        var tuning = Tuning();

        tuning.ItemPowerCoefficient.ShouldBe((double)GearDocuments.ShippedItemPowerCoefficient);
        tuning.Quality.ShouldBe(new QualityScales(
            (double)GearDocuments.ShippedQualityMinimum,
            (double)GearDocuments.ShippedQualityMaximum,
            (double)GearDocuments.ShippedPrimaryScaleBase,
            (double)GearDocuments.ShippedPrimaryScaleSpan,
            (double)GearDocuments.ShippedSecondaryScaleBase,
            (double)GearDocuments.ShippedSecondaryScaleSpan));
    }

    /// <summary>The quality scales run from 0.90 to 1.10 primary and 0.85 to 1.15 secondary.</summary>
    [Theory]
    [InlineData(0.0, 0.9, 0.85)]
    [InlineData(0.5, 1.0, 1.0)]
    [InlineData(1.0, 1.1, 1.15)]
    public void The_quality_scales_span_further_on_the_secondary_stat(
        double quality, double primary, double secondary)
    {
        var scales = Tuning().Quality;

        DeterminismRounding.Round(scales.Primary(quality)).ShouldBe(primary);
        DeterminismRounding.Round(scales.Secondary(quality)).ShouldBe(
            secondary,
            "a high-quality item leans hardest into its secondary stat, and a symmetric pair of " +
            "scales would make the quality bar mean the same thing on both.");
    }

    /// <summary>An item-power coefficient that is not positive is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    public void An_item_power_coefficient_that_is_not_positive_is_refused(double coefficient)
    {
        Should.Throw<InvalidTunableException>(() => DropsTuning.Read(
                GearDocuments.With(itemPowerCoefficient: ContentValue.Number((decimal)coefficient))))
            .Reference.ShouldBe(DropsTuning.ItemPowerCoefficientReference);
    }

    /// <summary>A quality range that spans nothing is refused.</summary>
    [Fact]
    public void A_quality_range_that_spans_nothing_is_refused()
    {
        Should.Throw<InvalidTunableException>(() => DropsTuning.Read(GearDocuments.With(
                qualityMinimum: ContentValue.Number(1.0m),
                qualityMaximum: ContentValue.Number(1.0m))))
            .Message.ShouldContain(
                "decoration",
                Case.Sensitive,
                "every item would roll the same quality; the refusal names that rather than merely " +
                "reporting a bad number.");
    }

    // ---------------------------------------------------------------- the slot table

    /// <summary>Each slot's two stats, and which of them is a percentage.</summary>
    /// <remarks>
    /// A null coefficient is not a missing value — it is the authored statement that the stat scales
    /// with rarity alone and never with chapter, because it feeds a capped percentage.
    /// </remarks>
    [Theory]
    [InlineData(GearSlot.WEAPON, "ATK", 0.2, "CRIT", null)]
    [InlineData(GearSlot.HELMET, "DEF", 0.18, "MAX_HP", 0.55)]
    [InlineData(GearSlot.ARMOR, "MAX_HP", 1.1, "DEF", 0.12)]
    [InlineData(GearSlot.BOOTS, "ASPD", null, "DODGE", null)]
    [InlineData(GearSlot.RING, "CRIT", null, "PEN", null)]
    [InlineData(GearSlot.AMULET, "MAX_HP", 0.45, "LIFESTEAL", null)]
    public void The_reader_answers_the_slot_row_the_document_authors(
        GearSlot slot, string primary, double? primaryCoefficient, string secondary, double? secondaryCoefficient)
    {
        var row = Tuning().Coefficients(slot);

        row.Slot.ShouldBe(slot);
        row.PrimaryStat.ShouldBe(primary);
        row.PrimaryCoefficient.ShouldBe(primaryCoefficient);
        row.SecondaryStat.ShouldBe(secondary);
        row.SecondaryCoefficient.ShouldBe(secondaryCoefficient);
    }

    /// <summary>A slot with no row is refused: an item in it would have no stats to derive at all.</summary>
    [Fact]
    public void Coefficients_refuses_a_slot_the_table_has_no_row_for()
    {
        Should.Throw<InvalidTunableException>(() => Tuning().Coefficients((GearSlot)7))
            .Reference.ShouldBe(DropsTuning.SlotCoefficientsReference);
    }

    /// <summary>A flat coefficient that is not positive is refused, and says how a percentage is authored.</summary>
    [Fact]
    public void A_flat_coefficient_that_is_not_positive_is_refused()
    {
        var rows = ContentValue.Array(
        [
            SlotRow("WEAPON", "ATK", ContentValue.Number(0m), "CRIT", ContentValue.Unauthorised),
        ]);

        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(slotCoefficients: rows)))
            .Message.ShouldContain(
                "read the percent table",
                Case.Sensitive,
                "a stat that scales with rarity alone is authored as null, and the refusal has to say " +
                "so — otherwise the fix looks like 'pick a coefficient'.");
    }

    // ---------------------------------------------------------------- the percent-stat table

    /// <summary>
    /// The lookup resolves on both axes: rows chosen so no two share a stat or a band, and DODGE at
    /// SS (0.055) is a value no other cell holds.
    /// </summary>
    [Theory]
    [InlineData("CRIT", Rarity.C, 0.015)]
    [InlineData("ASPD", Rarity.A, 0.045)]
    [InlineData("PEN", Rarity.S, 0.08)]
    [InlineData("DODGE", Rarity.SS, 0.055)]
    public void The_reader_answers_the_percent_stat_cell_the_document_authors(
        string stat, Rarity rarity, double expected)
    {
        Tuning().PercentStat(stat, rarity).ShouldBe(expected);
    }

    /// <summary>A pairing the table has no cell for is refused rather than read as zero.</summary>
    [Theory]
    [InlineData("ATK", Rarity.S)]
    [InlineData("crit", Rarity.S)]
    public void PercentStat_refuses_a_pairing_the_table_has_no_cell_for(string stat, Rarity rarity)
    {
        var thrown = Should.Throw<InvalidTunableException>(() => Tuning().PercentStat(stat, rarity));

        thrown.Reference.ShouldBe(DropsTuning.PercentStatsReference);
        thrown.Message.ShouldContain(stat, Case.Sensitive);
    }

    /// <summary>The underscore-prefixed comment member is skipped, not read as a stat.</summary>
    [Fact]
    public void A_comment_member_of_the_percent_table_is_not_a_stat()
    {
        Should.Throw<InvalidTunableException>(() => Tuning().PercentStat("_doc", Rarity.C));
    }

    /// <summary>A null stat is refused before the lookup.</summary>
    [Fact]
    public void PercentStat_refuses_a_null_stat()
    {
        Should.Throw<ArgumentNullException>(() => Tuning().PercentStat(null!, Rarity.C))
            .ParamName.ShouldBe("stat");
    }

    // ---------------------------------------------------------------- the affix pool

    /// <summary>The pool is fourteen affixes, and it is the pool the document lists.</summary>
    [Fact]
    public void The_pool_carries_the_affixes_the_document_lists()
    {
        var tuning = Tuning();

        tuning.AffixCount.ShouldBe(GearDocuments.ShippedAffixPoolSize);

        var affix = tuning.Affix("AFX_CRIT_CHANCE");

        affix.AffixId.ShouldBe("AFX_CRIT_CHANCE");
        affix.Stat.ShouldBe(StatId.CRIT);
        affix.Op.ShouldBe(EffectOp.STAT_ADD_FLAT);
        affix.Minimum.ShouldBe(0.02);
        affix.Maximum.ShouldBe(0.08);
        affix.Slots.ShouldBe(new[] { GearSlot.WEAPON, GearSlot.RING, GearSlot.HELMET });
    }

    /// <summary>An affix authoring neither half is a real pool member that writes nothing.</summary>
    [Fact]
    public void An_affix_authoring_no_stat_at_all_loads_and_writes_nothing()
    {
        Tuning().Affix("AFX_DAMAGE_VS_ELITES").WritesAStat.ShouldBeFalse();
    }

    /// <summary>An affix the pool does not declare is refused.</summary>
    [Theory]
    [InlineData("AFX_NOT_AUTHORED")]
    [InlineData("afx_crit_chance")]
    public void Affix_refuses_an_id_the_pool_does_not_declare(string affixId)
    {
        var thrown = Should.Throw<InvalidTunableException>(() => Tuning().Affix(affixId));

        thrown.Reference.ShouldBe(DropsTuning.AffixesReference);
        thrown.Message.ShouldContain(affixId, Case.Sensitive);
    }

    /// <summary>A null affix id is refused before the lookup.</summary>
    [Fact]
    public void Affix_refuses_a_null_id()
    {
        Should.Throw<ArgumentNullException>(() => Tuning().Affix(null!)).ParamName.ShouldBe("affixId");
    }

    /// <summary>An eligible pool carries only affixes the slot authorises.</summary>
    /// <remarks>
    /// The slot restriction is what keeps lifesteal off boots. The count floor comes first, because
    /// <c>ShouldAllBe</c> over an empty pool passes and a reader that filtered everything out would
    /// otherwise satisfy the arm below quantifying over nothing.
    /// </remarks>
    [Theory]
    [InlineData(GearSlot.WEAPON, 6, "AFX_LIFESTEAL", "AFX_MAX_HP")]
    [InlineData(GearSlot.HELMET, 4, "AFX_BLOCK", "AFX_DODGE")]
    [InlineData(GearSlot.ARMOR, 4, "AFX_DAMAGE_REDUCTION", "AFX_CRIT_CHANCE")]
    [InlineData(GearSlot.BOOTS, 3, "AFX_DODGE", "AFX_LIFESTEAL")]
    [InlineData(GearSlot.RING, 6, "AFX_GOLD_GAIN", "AFX_DEF")]
    [InlineData(GearSlot.AMULET, 6, "AFX_PET_AURA_POWER", "AFX_ATTACK_SPEED")]
    public void EligibleAffixes_carries_only_the_affixes_the_slot_authorises(
        GearSlot slot, int eligible, string present, string absent)
    {
        var pool = Tuning().EligibleAffixes(slot, Rarity.A);

        pool.Count.ShouldBe(eligible);
        pool.Select(affix => affix.AffixId).ShouldContain(present);
        pool.Select(affix => affix.AffixId).ShouldNotContain(
            absent,
            $"{absent} does not author {slot} among its slots, and an affix rolled onto a slot the " +
            "pool never offered it on is a stat the item screen cannot explain.");
    }

    /// <summary>The floored affix is absent below its band and present at and above it.</summary>
    /// <remarks>
    /// The floor is applied where the pool is read rather than at the roll: a roller that had to
    /// remember it would eventually forget, and the affix is a reroll charge — the one that would be
    /// most visible on an item that should not have it.
    /// </remarks>
    [Theory]
    [InlineData(Rarity.C, false)]
    [InlineData(Rarity.B, false)]
    [InlineData(Rarity.A, false)]
    [InlineData(Rarity.S, true)]
    [InlineData(Rarity.SS, true)]
    public void EligibleAffixes_applies_the_authored_rarity_floor(Rarity rarity, bool eligible)
    {
        var pool = Tuning().EligibleAffixes(GearSlot.RING, rarity)
            .Select(affix => affix.AffixId)
            .ToArray();

        pool.Contains(GearDocuments.ShippedFlooredAffixId, StringComparer.Ordinal).ShouldBe(eligible);
        pool.ShouldContain(
            "AFX_CRIT_CHANCE",
            "the floor moves one affix, not the pool: an unfloored affix must be eligible at every " +
            "band, or this theory would pass over an empty pool at the bottom rows.");
    }

    /// <summary>An affix range a roll cannot land inside is refused.</summary>
    [Theory]
    [InlineData(0.5, 0.2)]
    [InlineData(-0.1, 0.2)]
    public void An_affix_range_a_roll_cannot_land_inside_is_refused(double minimum, double maximum)
    {
        var affixes = ContentValue.Array(
        [
            GearDocuments.AffixRow(new AuthoredAffix(
                "AFX_BROKEN", "ATK", "STAT_ADD_FLAT", (decimal)minimum, (decimal)maximum, ["WEAPON"], null)),
        ]);

        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(affixes: affixes)))
            .Message.ShouldContain("not a range a roll can land inside", Case.Sensitive);
    }

    // ------------------------------------------------- what an affix writes, and how

    /// <summary>An affix naming a stat with no bucket, or a bucket with no stat, is refused.</summary>
    /// <remarks>
    /// Both directions, because refusing only one leaves the other as a row that reads as a real
    /// contribution and silently applies nothing.
    /// </remarks>
    [Theory]
    [InlineData("CRIT", null)]
    [InlineData(null, "STAT_ADD_FLAT")]
    public void An_affix_authoring_half_of_its_contribution_is_refused(string? stat, string? op)
    {
        var affixes = ContentValue.Array(
        [
            GearDocuments.AffixRow(new AuthoredAffix("AFX_HALF", stat, op, 0.1m, 0.2m, ["WEAPON"], null)),
        ]);

        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(affixes: affixes)))
            .Message.ShouldContain("or neither", Case.Sensitive);
    }

    /// <summary>An affix writing through anything but the two additive buckets is refused.</summary>
    /// <remarks>
    /// Two probes of different shapes: a real effect op an affix has no business carrying, and a
    /// token that is no op at all. A guard that only rejected nonsense would let an affix become a
    /// multiplier — the one op the design set reserves for Legendary perks.
    /// </remarks>
    [Theory]
    [InlineData("STAT_MULT", "writes through")]
    [InlineData("NOT_AN_OP", "not the flat or the percent additive bucket")]
    public void An_affix_writing_through_anything_but_an_additive_bucket_is_refused(
        string op, string expected)
    {
        var affixes = ContentValue.Array(
        [
            GearDocuments.AffixRow(new AuthoredAffix("AFX_ODD", "ATK", op, 0.1m, 0.2m, ["WEAPON"], null)),
        ]);

        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(affixes: affixes)))
            .Message.ShouldContain(expected, Case.Sensitive);
    }

    /// <summary>An affix naming a stat the vocabulary does not have is refused.</summary>
    /// <remarks>
    /// The comma hole in particular: a token list is combined bitwise even for a non-flags enum, so
    /// an authored <c>"CRIT, DEF"</c> would otherwise load as a third member nobody wrote.
    /// </remarks>
    [Theory]
    [InlineData("NOT_A_STAT")]
    [InlineData("CRIT, DEF")]
    [InlineData("5")]
    public void An_affix_naming_something_that_is_not_a_stat_is_refused(string stat)
    {
        var affixes = ContentValue.Array(
        [
            GearDocuments.AffixRow(
                new AuthoredAffix("AFX_ODD", stat, "STAT_ADD_FLAT", 0.1m, 0.2m, ["WEAPON"], null)),
        ]);

        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(affixes: affixes)))
            .Message.ShouldContain("is not one of the stats", Case.Sensitive);
    }

    // ---------------------------------------------------------------- the set breakpoints

    /// <summary>The breakpoints are the authored ascending ladder.</summary>
    [Fact]
    public void The_reader_answers_the_set_breakpoints_the_document_authors()
    {
        Tuning().SetBreakpoints.ShouldBe(GearDocuments.ShippedSetBreakpoints);
    }

    /// <summary>A non-ascending breakpoint ladder is refused.</summary>
    /// <remarks>
    /// A later tier firing before an earlier one is not visible in any single value, so the reader
    /// checks the ladder rather than the numbers.
    /// </remarks>
    [Theory]
    [InlineData(new[] { 2, 4, 4 })]
    [InlineData(new[] { 2, 6, 4 })]
    [InlineData(new[] { 0, 4, 6 })]
    public void A_non_ascending_breakpoint_ladder_is_refused(int[] breakpoints)
    {
        var authored = ContentValue.Array(breakpoints.Select(pieces => ContentValue.Number(pieces)));

        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(setBreakpoints: authored)))
            .Reference.ShouldStartWith(DropsTuning.SetBreakpointsReference, Case.Sensitive);
    }

    // ---------------------------------------------------------------- the ways data can fail

    /// <summary>A missing document is a <c>MissingContentException</c>, not an empty set of tables.</summary>
    [Fact]
    public void A_missing_document_throws_rather_than_defaulting()
    {
        Should.Throw<MissingContentException>(
                () => DropsTuning.Read(GearDocuments.Without(GearDocuments.DropsDocumentPath)))
            .Reference.ShouldBe(DropsTuning.DocumentPath);
    }

    /// <summary>A deliberate <c>null</c> is an <c>UnauthorisedTunableException</c> — the hole stays a hole.</summary>
    [Fact]
    public void An_unauthorised_null_throws_rather_than_defaulting()
    {
        Should.Throw<UnauthorisedTunableException>(() => DropsTuning.Read(
                GearDocuments.With(itemPowerCoefficient: ContentValue.Unauthorised)))
            .Reference.ShouldBe(DropsTuning.ItemPowerCoefficientReference);
    }

    /// <summary>A leaf of the wrong kind is a <c>ContentTypeMismatchException</c>.</summary>
    [Fact]
    public void A_leaf_of_the_wrong_kind_is_refused()
    {
        Should.Throw<ContentTypeMismatchException>(() => DropsTuning.Read(
                GearDocuments.With(itemPowerCoefficient: ContentValue.Text("a tenth"))))
            .Reference.ShouldBe(DropsTuning.ItemPowerCoefficientReference);
    }

    /// <summary>An empty array where a table belongs is refused rather than read as no rows.</summary>
    [Fact]
    public void An_empty_table_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => DropsTuning.Read(GearDocuments.With(rarities: ContentValue.EmptyArray)))
            .Reference.ShouldBe(DropsTuning.RaritiesReference);
    }

    /// <summary>The reader refuses a null content set rather than dereferencing it.</summary>
    [Fact]
    public void A_null_content_set_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => DropsTuning.Read(null!)).ParamName.ShouldBe("content");
    }

    private static DropsTuning Tuning() => DropsTuning.Read(GearDocuments.Shipped);

    /// <summary>Every chapter the shipped bands cover, from the fixture's own band boundaries.</summary>
    private static IEnumerable<int> EveryAuthoredChapter() =>
        GearDocuments.ShippedChapterBands.SelectMany(
            band => Enumerable.Range(band.From, band.To - band.From + 1));

    private static double Share(
        IReadOnlyList<(Rarity Rarity, double Share)> shares, Rarity rarity) =>
        shares.Single(share => share.Rarity == rarity).Share;

    private static ContentValue Row(string id, decimal statMultiplier, int affixCount) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["id"] = ContentValue.Text(id),
            ["statMultiplier"] = ContentValue.Number(statMultiplier),
            ["affixCount"] = ContentValue.Number(affixCount),
        });

    private static ContentValue SlotRow(
        string slot,
        string primaryStat,
        ContentValue primaryCoefficient,
        string secondaryStat,
        ContentValue secondaryCoefficient) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["slot"] = ContentValue.Text(slot),
            ["primaryStat"] = ContentValue.Text(primaryStat),
            ["primaryCoef"] = primaryCoefficient,
            ["secondaryStat"] = ContentValue.Text(secondaryStat),
            ["secondaryCoef"] = secondaryCoefficient,
        });
}
