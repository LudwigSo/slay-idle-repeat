using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Every energy number is read from tuning data, never written as a C# constant. This is the
/// reader: which pointer each number comes from, and what happens when one is missing, mistyped,
/// or deliberately unauthorised.
/// </summary>
public sealed class EnergyTuningTests
{
    [Fact]
    public void Every_energy_number_comes_from_the_progression_document()
    {
        var tuning = EnergyTuning.Read(ProgressionDocuments.Shipped);

        tuning.BaseMax.ShouldBe(120);
        tuning.PerLegendLevel.ShouldBe(2);
        tuning.MaxCap.ShouldBe(200);
        tuning.RegenInterval.ShouldBe(TimeSpan.FromMinutes(4));
        tuning.RunCost.ShouldBe(20);
        tuning.ReserveMultipleOfMax.ShouldBe(1);
    }

    /// <summary>
    /// A <c>null</c> in the data files means "the design docs do not authorise a value here"
    /// (<c>game-data/README.md</c>) — never coerced to a default.
    /// </summary>
    [Theory]
    [InlineData("baseMax")]
    [InlineData("perLegendLevel")]
    [InlineData("maxCap")]
    [InlineData("regenMinutesPerPoint")]
    [InlineData("runCost")]
    [InlineData("reserveMultipleOfMax")]
    public void An_unauthorised_hole_is_never_read_as_a_default(string leaf)
    {
        var content = Replace(leaf, ContentValue.Unauthorised);

        var thrown = Should.Throw<UnauthorisedTunableException>(() => EnergyTuning.Read(content));

        thrown.Reference.ShouldBe($"tuning/progression.json#/energy/{leaf}");
        thrown.Message.ShouldContain("do not authorise a value here", Case.Sensitive);
    }

    [Fact]
    public void An_absent_energy_block_fails_loudly_rather_than_reading_zero()
    {
        var content = ProgressionDocuments.Document(ContentValue.EmptyObject);

        var thrown = Should.Throw<MissingContentException>(() => EnergyTuning.Read(content));

        thrown.Reference.ShouldStartWith("tuning/progression.json#/energy/", Case.Sensitive);
    }

    [Fact]
    public void An_absent_progression_document_fails_loudly()
    {
        var content = new ContentSnapshot(
            ContentVersion.FromHex(new string('b', ContentVersion.HexLength)),
            []);

        var thrown = Should.Throw<MissingContentException>(() => EnergyTuning.Read(content));

        thrown.Reference.ShouldBe(ProgressionDocuments.DocumentPath);
    }

    /// <summary>
    /// Energy points are whole numbers: a fractional base, cap, per-level increment, run cost, or
    /// reserve multiple would make Max Energy or Reserve capacity fractional with no rounding rule
    /// to apply, so the read fails rather than inventing one. The regeneration interval is exempt —
    /// minutes are a duration, and <c>4.5</c> is meaningful.
    /// </summary>
    [Theory]
    [InlineData("baseMax", 120.5)]
    [InlineData("perLegendLevel", 2.5)]
    [InlineData("maxCap", 200.5)]
    [InlineData("runCost", 20.5)]
    [InlineData("reserveMultipleOfMax", 1.5)]
    public void A_fractional_whole_number_tunable_fails_rather_than_rounding(string leaf, double authored)
    {
        var content = Replace(leaf, ContentValue.Number((decimal)authored));

        var thrown = Should.Throw<ContentTypeMismatchException>(() => EnergyTuning.Read(content));

        thrown.Reference.ShouldBe($"tuning/progression.json#/energy/{leaf}");
    }

    [Fact]
    public void A_fractional_regeneration_interval_is_legal_because_minutes_are_a_duration()
    {
        var tuning = EnergyTuning.Read(
            ProgressionDocuments.With(regenMinutesPerPoint: ContentValue.Number(4.5m)));

        tuning.RegenInterval.ShouldBe(TimeSpan.FromMinutes(4.5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_base_max_is_refused(int authored)
    {
        var thrown = Refuses("baseMax", ContentValue.Number(authored));

        thrown.Reference.ShouldBe(EnergyTuning.BaseMaxReference);
        thrown.Message.ShouldMatchWildcard("*10 §3*120*");
    }

    [Fact]
    public void A_max_cap_below_the_base_max_is_refused()
    {
        var thrown = Refuses("maxCap", ContentValue.Number(119));

        thrown.Reference.ShouldBe(EnergyTuning.MaxCapReference);
        thrown.Message.ShouldMatchWildcard("*119*120*");
    }

    /// <summary>200 equals 120 plus 40 levels of +2; equal to the base max is legal.</summary>
    [Fact]
    public void A_max_cap_equal_to_the_base_max_is_legal()
    {
        var tuning = EnergyTuning.Read(ProgressionDocuments.With(maxCap: ContentValue.Number(120)));

        tuning.MaxCap.ShouldBe(120);
    }

    [Fact]
    public void A_negative_per_legend_level_increment_is_refused()
    {
        var thrown = Refuses("perLegendLevel", ContentValue.Number(-1));

        thrown.Reference.ShouldBe(EnergyTuning.PerLegendLevelReference);
        thrown.Message.ShouldMatchWildcard("*shrink*Legend Level*");
    }

    /// <summary>Zero is legal: Max Energy simply does not grow if the +2 dial is turned to nothing.</summary>
    [Fact]
    public void A_per_legend_level_increment_of_zero_is_legal()
    {
        var tuning = EnergyTuning.Read(ProgressionDocuments.With(perLegendLevel: ContentValue.Number(0)));

        tuning.PerLegendLevel.ShouldBe(0);
    }

    /// <summary>
    /// Three separate guards on <c>regenMinutesPerPoint</c> report the same <c>Reference</c>, so
    /// each case pins the message fragment unique to its branch.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void A_non_positive_regeneration_interval_is_refused(int minutes)
    {
        var thrown = Refuses("regenMinutesPerPoint", ContentValue.Number(minutes));

        thrown.Reference.ShouldBe(EnergyTuning.RegenMinutesPerPointReference);
        thrown.Message.ShouldMatchWildcard("*must be a positive span*");
    }

    /// <summary>
    /// A sub-tick interval truncates to zero ticks. Refused at the read, because every accrual
    /// afterwards divides an elapsed span by it.
    /// </summary>
    [Fact]
    public void A_regeneration_interval_shorter_than_one_tick_is_refused()
    {
        var thrown = Refuses("regenMinutesPerPoint", ContentValue.Number(0.0000000001m));

        thrown.Reference.ShouldBe(EnergyTuning.RegenMinutesPerPointReference);
        thrown.Message.ShouldMatchWildcard("*shorter than one tick*");
    }

    /// <summary>
    /// Past <c>TimeSpan</c>'s ceiling the raw conversion throws <see cref="OverflowException"/>,
    /// which is outside the <see cref="ContentException"/> family a composition root listens for.
    /// </summary>
    [Fact]
    public void A_regeneration_interval_longer_than_the_runtime_can_represent_is_refused()
    {
        var thrown = Refuses("regenMinutesPerPoint", ContentValue.Number(1_000_000_000_000_000m));

        thrown.Reference.ShouldBe(EnergyTuning.RegenMinutesPerPointReference);
        thrown.Message.ShouldMatchWildcard("*longer than any span the runtime can represent*");
        thrown.ShouldBeAssignableTo<ContentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_run_cost_is_refused(int authored)
    {
        var thrown = Refuses("runCost", ContentValue.Number(authored));

        thrown.Reference.ShouldBe(EnergyTuning.RunCostReference);
    }

    [Fact]
    public void A_negative_reserve_multiple_is_refused()
    {
        var thrown = Refuses("reserveMultipleOfMax", ContentValue.Number(-1));

        thrown.Reference.ShouldBe(EnergyTuning.ReserveMultipleOfMaxReference);
        thrown.Message.ShouldMatchWildcard("*28*C2*");
    }

    /// <summary>The 1× cap is a dial, not a hard-coded constant — raising it must work.</summary>
    [Fact]
    public void The_reserve_multiple_is_a_dial_and_not_a_constant()
    {
        var tuning = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(3)));

        tuning.ReserveMultipleOfMax.ShouldBe(3);
    }

    /// <summary>Zero means no Reserve at all; legal, and the accrual suite proves it discards overflow.</summary>
    [Fact]
    public void A_reserve_multiple_of_zero_is_legal_and_means_no_reserve()
    {
        var tuning = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(0)));

        tuning.ReserveMultipleOfMax.ShouldBe(0);
    }

    private static InvalidTunableException Refuses(string leaf, ContentValue authored) =>
        Should.Throw<InvalidTunableException>(() => EnergyTuning.Read(Replace(leaf, authored)));

    private static ContentSnapshot Replace(string leaf, ContentValue value) => leaf switch
    {
        "baseMax" => ProgressionDocuments.With(baseMax: value),
        "perLegendLevel" => ProgressionDocuments.With(perLegendLevel: value),
        "maxCap" => ProgressionDocuments.With(maxCap: value),
        "regenMinutesPerPoint" => ProgressionDocuments.With(regenMinutesPerPoint: value),
        "runCost" => ProgressionDocuments.With(runCost: value),
        "reserveMultipleOfMax" => ProgressionDocuments.With(reserveMultipleOfMax: value),
        _ => throw new ArgumentOutOfRangeException(
            nameof(leaf), leaf, "This case names an energy tunable the fixture does not author."),
    };
}
