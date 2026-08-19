using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The two hero readers whose whole job is refusing a bad number: <see cref="LegendCurveTuning"/> and
/// <see cref="PresetTuning"/>. Each case drives one leaf to a value that is authorised and unusable —
/// the class a schema cannot catch.
/// </summary>
public sealed class HeroTuningTests
{
    // ------------------------------------------------------------------------- the Legend curve

    /// <summary>The shipped curve is read exactly as the document authors it.</summary>
    [Fact]
    public void The_shipped_curve_is_read_from_the_document()
    {
        var curve = LegendCurveTuning.Read(ProgressionDocuments.Shipped);

        curve.Coefficient.ShouldBe(ProgressionDocuments.ShippedLegendXpCoefficient);
        curve.Exponent.ShouldBe((double)ProgressionDocuments.ShippedLegendXpExponent);
        curve.TalentPointsPerLevel.ShouldBe(ProgressionDocuments.ShippedTalentPointsPerLevel);
    }

    /// <summary>A free level-up is refused: it is an unbounded loop the moment any XP is banked.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-120)]
    public void A_coefficient_that_makes_a_level_free_is_refused(int coefficient)
    {
        Should.Throw<InvalidTunableException>(
                () => LegendCurveTuning.Read(
                    ProgressionDocuments.With(legendXpCoefficient: ContentValue.Number(coefficient))))
            .Message.ShouldContain(LegendCurveTuning.CoefficientReference);
    }

    /// <summary>An exponent that stops the curve rising is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_exponent_that_stops_the_curve_rising_is_refused(int exponent)
    {
        Should.Throw<InvalidTunableException>(
                () => LegendCurveTuning.Read(
                    ProgressionDocuments.With(legendXpExponent: ContentValue.Number(exponent))))
            .Message.ShouldContain(LegendCurveTuning.ExponentReference);
    }

    /// <summary>A negative Talent Point rate is refused: nothing takes a point back.</summary>
    [Fact]
    public void A_negative_Talent_Point_rate_is_refused()
    {
        Should.Throw<InvalidTunableException>(
                () => LegendCurveTuning.Read(
                    ProgressionDocuments.With(talentPointsPerLevel: ContentValue.Number(-1))))
            .Message.ShouldContain(LegendCurveTuning.TalentPointsPerLevelReference);
    }

    /// <summary>A deliberate hole is refused as the hole it is, not defaulted to zero.</summary>
    [Fact]
    public void An_unauthorised_curve_leaf_is_refused_as_a_hole()
    {
        Should.Throw<UnauthorisedTunableException>(
            () => LegendCurveTuning.Read(
                ProgressionDocuments.With(legendXpExponent: ContentValue.Unauthorised)));
    }

    // ---------------------------------------------------------------------- the preset allowance

    /// <summary>
    /// A different authored allowance changes the answer, which is what proves it is read — the
    /// shipped 3 is also the literal anybody would write.
    /// </summary>
    [Fact]
    public void A_different_authored_allowance_is_the_one_that_answers()
    {
        var tuning = PresetTuning.Read(TuningDocuments.AdsOnly(ContentValue.Number(5)));

        tuning.FreeSlots.ShouldBe(5);
        tuning.IsBeyondFreeAllowance(5).ShouldBeFalse();
        tuning.IsBeyondFreeAllowance(6).ShouldBeTrue();
    }

    /// <summary>An allowance of zero would put a core free feature behind Plus, and is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_allowance_that_gates_the_whole_feature_is_refused(int freeSlots)
    {
        Should.Throw<InvalidTunableException>(
                () => PresetTuning.Read(TuningDocuments.AdsOnly(ContentValue.Number(freeSlots))))
            .Message.ShouldContain(PresetTuning.FreePresetsReference);
    }

    /// <summary>
    /// 🔒 "Below the floor" and "beyond the allowance" are different questions — collapsing the two
    /// tells a player to subscribe because their client sent a zero.
    /// </summary>
    [Fact]
    public void A_slot_below_the_floor_is_not_an_entitlement_question()
    {
        var tuning = PresetTuning.Read(TuningDocuments.AdsOnly());

        tuning.IsBeyondFreeAllowance(0).ShouldBeFalse(
            "slot 0 is malformed, not Plus-gated — the two refusals must stay tellable apart.");
        tuning.IsBeyondFreeAllowance(PresetTuning.FirstSlot - 1).ShouldBeFalse();

        tuning.IsBeyondFreeAllowance(tuning.FreeSlots).ShouldBeFalse();
        tuning.IsBeyondFreeAllowance(tuning.FreeSlots + 1).ShouldBeTrue();
    }

    /// <summary>A missing document is refused rather than defaulted.</summary>
    [Fact]
    public void A_missing_ads_document_is_refused()
    {
        Should.Throw<MissingContentException>(
            () => PresetTuning.Read(ProgressionDocuments.Shipped));
    }

    // ------------------------------------------------------------------- the two storage bounds

    /// <summary>
    /// Both storage bounds are read from the document, and a different authored pair is the one that
    /// answers. The replacements differ from the shipped values and from each other, so a reader that
    /// hardcoded either bound or crossed the two pointers fails.
    /// </summary>
    [Fact]
    public void A_different_authored_pair_of_storage_bounds_is_the_one_that_answers()
    {
        PresetTuning.Read(TuningDocuments.AdsOnly()).HighestSlot
            .ShouldBe(TuningDocuments.ShippedHighestPresetSlot);
        PresetTuning.Read(TuningDocuments.AdsOnly()).LongestNameTextElements
            .ShouldBe(TuningDocuments.ShippedLongestPresetName);

        var retuned = PresetTuning.Read(TuningDocuments.AdsOnly(
            highestSlot: ContentValue.Number(120), longestName: ContentValue.Number(31)));

        retuned.HighestSlot.ShouldBe(120);
        retuned.LongestNameTextElements.ShouldBe(31);
    }

    /// <summary>
    /// A slot bound at or below the free allowance is refused: it would put slots <c>09</c> §2.1
    /// gives away for free out of reach. The floor is the allowance, not 1.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(TuningDocuments.ShippedFreePresets - 1)]
    public void A_slot_bound_that_reaches_into_the_free_allowance_is_refused(int highestSlot)
    {
        var thrown = Should.Throw<InvalidTunableException>(() => PresetTuning.Read(
            TuningDocuments.AdsOnly(highestSlot: ContentValue.Number(highestSlot))));

        thrown.Reference.ShouldBe(
            PresetTuning.HighestSlotReference,
            "which bound, not merely that some bound was refused — this reader raises the same type " +
            "for the name bound and for the free allowance itself.");
        thrown.Message.ShouldContain("slot number", Case.Sensitive);
    }

    /// <summary>A slot bound exactly at the free allowance is accepted: every free slot is writable.</summary>
    [Fact]
    public void A_slot_bound_exactly_at_the_free_allowance_is_accepted()
    {
        PresetTuning.Read(TuningDocuments.AdsOnly(
                highestSlot: ContentValue.Number(TuningDocuments.ShippedFreePresets)))
            .HighestSlot.ShouldBe(TuningDocuments.ShippedFreePresets);
    }

    /// <summary>A name bound below one leaves no nameable preset at all, and is refused.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_name_bound_below_one_is_refused(int longestName)
    {
        var thrown = Should.Throw<InvalidTunableException>(() => PresetTuning.Read(
            TuningDocuments.AdsOnly(longestName: ContentValue.Number(longestName))));

        thrown.Reference.ShouldBe(PresetTuning.LongestNameReference);
        thrown.Message.ShouldContain("name length", Case.Sensitive);
    }

    /// <summary>
    /// A name bound of one is accepted — the floor is one, not the free allowance. 1 is below
    /// <c>ShippedFreePresets</c>, so a reader that floored both bounds at the allowance fails here.
    /// </summary>
    [Fact]
    public void A_name_bound_of_one_is_accepted()
    {
        PresetTuning.Read(TuningDocuments.AdsOnly(longestName: ContentValue.Number(1)))
            .LongestNameTextElements.ShouldBe(1);
    }
}
