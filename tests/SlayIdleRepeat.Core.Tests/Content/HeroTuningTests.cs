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
        tuning.IsFreeSlot(5).ShouldBeTrue();
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

        tuning.IsFreeSlot(0).ShouldBeFalse();
        tuning.IsBeyondFreeAllowance(0).ShouldBeFalse(
            "slot 0 is malformed, not Plus-gated — the two refusals must stay tellable apart.");

        tuning.IsFreeSlot(tuning.FreeSlots + 1).ShouldBeFalse();
        tuning.IsBeyondFreeAllowance(tuning.FreeSlots + 1).ShouldBeTrue();
    }

    /// <summary>A missing document is refused rather than defaulted.</summary>
    [Fact]
    public void A_missing_ads_document_is_refused()
    {
        Should.Throw<MissingContentException>(
            () => PresetTuning.Read(ProgressionDocuments.Shipped));
    }
}
