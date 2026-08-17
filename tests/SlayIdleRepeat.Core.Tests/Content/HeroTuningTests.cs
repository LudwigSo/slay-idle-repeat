using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The two hero readers whose whole job is refusing a bad number: <see cref="LegendCurveTuning"/> and
/// <see cref="PresetTuning"/>.
/// </summary>
/// <remarks>
/// Every case drives one leaf to a value that is authorised and unusable, because that is the class
/// a schema cannot catch: a coefficient of zero, an exponent of zero and a free allowance of zero
/// are all valid JSON numbers and all three break a different promise.
/// </remarks>
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

    /// <summary>
    /// The simulator's sweep range is deliberately not read: it is a bound on experiments, not a
    /// number any rule depends on.
    /// </summary>
    /// <remarks>
    /// Asserted by absence — the reader names three pointers and this is not one of them — so that a
    /// later edit that started reading it is a visible change rather than a quiet coupling between
    /// the runtime and the simulator's configuration.
    /// </remarks>
    [Fact]
    public void The_sweep_range_is_not_one_of_the_curves_pointers()
    {
        new[]
        {
            LegendCurveTuning.CoefficientReference,
            LegendCurveTuning.ExponentReference,
            LegendCurveTuning.TalentPointsPerLevelReference,
        }.ShouldNotContain(reference => reference.Contains("SweepRange", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------- the preset allowance

    /// <summary>The free allowance is read from <c>ads.json</c> rather than restated in code.</summary>
    [Fact]
    public void The_free_preset_allowance_is_read_from_the_document()
    {
        PresetTuning.Read(TuningDocuments.AdsOnly()).FreeSlots
            .ShouldBe(TuningDocuments.ShippedFreePresets);
    }

    /// <summary>
    /// A different authored allowance changes the answer, which is what proves it is read.
    /// </summary>
    /// <remarks>
    /// The shipped 3 is also the literal anybody would write, so a reader that ignored the document
    /// would pass the case above. This is the discriminating one.
    /// </remarks>
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
    /// 🔒 "Below the floor" and "beyond the allowance" are different questions, and the second is not
    /// the negation of the first.
    /// </summary>
    /// <remarks>
    /// The distinction the handler's rejection code rests on: a slot below the first is a malformed
    /// payload and a slot above the allowance is an entitlement, and collapsing the two tells a
    /// player to subscribe because their client sent a zero.
    /// </remarks>
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
    /// answers.
    /// </summary>
    /// <remarks>
    /// 🔒 The discriminating half is what matters: these were two <c>const</c>s in
    /// <c>LoadoutPreset</c> until the M4 review, so a reader that kept answering 999 and 64 would
    /// pass any case written against the shipped numbers. Both replacements are deliberately
    /// <em>not</em> the shipped values, and they differ from each other so a reader that crossed the
    /// two pointers cannot pass either.
    /// </remarks>
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
    /// gives away for free out of reach, which is the one thing these bounds claim not to do.
    /// </summary>
    /// <remarks>
    /// The floor is the allowance rather than 1, so a bound of 2 is refused even though it names a
    /// perfectly storable slot — the point is not that the number is small, it is that the third free
    /// slot would stop being writable. A bound exactly equal to the allowance is accepted, and the
    /// case below is the boundary control on that.
    /// </remarks>
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
    /// <remarks>
    /// The boundary control on the case above. The refusal is "a free slot became unwritable", not
    /// "the number looks low", and only a case on each side of the boundary tells those apart.
    /// </remarks>
    [Fact]
    public void A_slot_bound_exactly_at_the_free_allowance_is_accepted()
    {
        PresetTuning.Read(TuningDocuments.AdsOnly(
                highestSlot: ContentValue.Number(TuningDocuments.ShippedFreePresets)))
            .HighestSlot.ShouldBe(TuningDocuments.ShippedFreePresets);
    }

    /// <summary>A name bound below one leaves no nameable preset at all, and is refused.</summary>
    /// <remarks>
    /// The negative control on the case above: the two bounds have different floors — the slot bound
    /// is floored at the free allowance and the name bound at one — so a reader that shared one floor
    /// between them would pass one case and fail the other.
    /// </remarks>
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
    /// A name bound of one is accepted — the floor is one, not the free allowance.
    /// </summary>
    /// <remarks>
    /// The boundary control that stops the two floors being collapsed into a single number: 1 is
    /// below <c>ShippedFreePresets</c>, so a reader that floored both bounds at the allowance would
    /// refuse this and fail.
    /// </remarks>
    [Fact]
    public void A_name_bound_of_one_is_accepted()
    {
        PresetTuning.Read(TuningDocuments.AdsOnly(longestName: ContentValue.Number(1)))
            .LongestNameTextElements.ShouldBe(1);
    }
}
