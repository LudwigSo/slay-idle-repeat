using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Economy;

/// <summary>
/// `21` §3.1 — 🔒 every energy number is read from <c>game-data/tuning/</c>, never written as a C#
/// constant. This is the reader: which pointer each number comes from, and what happens when one of
/// them is missing, mistyped or deliberately unauthorised.
/// </summary>
public sealed class EnergyTuningTests
{
    /// <summary>The six pointers `10` §3 and `28` C2 author, in the order this suite states them.</summary>
    private static readonly string[] DocumentedPointers =
    {
        "tuning/progression.json#/energy/baseMax",
        "tuning/progression.json#/energy/perLegendLevel",
        "tuning/progression.json#/energy/maxCap",
        "tuning/progression.json#/energy/regenMinutesPerPoint",
        "tuning/progression.json#/energy/runCost",
        "tuning/progression.json#/energy/reserveMultipleOfMax",
    };

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
    /// 🔒 S2 — the pointer strings are the claim, so each one is pinned by name. A reader that
    /// quietly moved to <c>#/energy/max</c> would keep returning 120 from any fixture that authors
    /// both, and the number-by-number case above would not notice.
    /// </summary>
    [Fact]
    public void The_pointers_the_reader_reads_are_the_documented_ones()
    {
        var pointers = new[]
        {
            EnergyTuning.BaseMaxReference,
            EnergyTuning.PerLegendLevelReference,
            EnergyTuning.MaxCapReference,
            EnergyTuning.RegenMinutesPerPointReference,
            EnergyTuning.RunCostReference,
            EnergyTuning.ReserveMultipleOfMaxReference,
        };

        pointers.ShouldBe(
            DocumentedPointers,
            "these are the pointers game-data/tuning/progression.json authors. " +
            "EnergyTuningMatchesTuningDataTests in SlayIdleRepeat.Application.Tests asserts the real " +
            "file still holds them, and moving one here without moving it there splits the pin — " +
            "Core.Tests is hermetic and cannot see the file itself.");
    }

    /// <summary>
    /// 🔒 S6 — a <c>null</c> in the data files means "the design docs do not authorise a value
    /// here" (<c>game-data/README.md</c>). The reader must never coerce one to a default: it fails
    /// loudly, naming the pointer that was never authored.
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

        DocumentedPointers.ShouldContain(
            thrown.Reference,
            "the read failed on a pointer this suite does not know about, so the reader is reading " +
            "something the pin above does not cover.");
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
    /// `10` §3 authors whole Energy points. A fractional base, cap, per-level increment, run cost
    /// or reserve multiple would make Max Energy or the Reserve capacity fractional, and no
    /// document authors a rounding rule for either — so the read fails rather than inventing one
    /// (S6). The regeneration interval is deliberately absent from this list: minutes are a
    /// duration, and <c>4.5</c> is a perfectly meaningful one.
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

    /// <summary>`10` §3's cap of 200 equals the base of 120 plus 40 levels of +2; equal is legal.</summary>
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

    /// <summary>
    /// Zero is legal: it is the configuration in which Max Energy does not grow at all, which is
    /// what `10` §3's 📐 marker would produce if the +2 dial were turned to nothing.
    /// </summary>
    [Fact]
    public void A_per_legend_level_increment_of_zero_is_legal()
    {
        var tuning = EnergyTuning.Read(ProgressionDocuments.With(perLegendLevel: ContentValue.Number(0)));

        tuning.PerLegendLevel.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 S2 — three separate guards on <c>regenMinutesPerPoint</c> report the same
    /// <c>Reference</c>, so each case pins the message fragment unique to <em>its</em> branch.
    /// Asserting the reference alone, widening the <c>&lt;= 0</c> guard would make the other two
    /// dead code with all three cases still green.
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
    /// 🔒 The other end of the same span. Past <c>TimeSpan</c>'s ceiling the decimal multiply or the
    /// checked <c>(long)</c> cast throws <see cref="OverflowException"/>, which is outside the
    /// <see cref="ContentException"/> family a composition root catches to report a bad data set —
    /// so the read would fail in a way nobody is listening for.
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

    /// <summary>
    /// `28` C2's 📐 marker calls the 1× cap "the dial", so raising it must work — the reader may
    /// not have hard-coded the shipped 1.
    /// </summary>
    [Fact]
    public void The_reserve_multiple_is_a_dial_and_not_a_constant()
    {
        var tuning = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(3)));

        tuning.ReserveMultipleOfMax.ShouldBe(3);
    }

    /// <summary>
    /// Zero is the "no Reserve at all" configuration — what `10` §3 described before `28` C
    /// resolved the contradiction. Legal, and the accrual suite proves it discards overflow.
    /// </summary>
    [Fact]
    public void A_reserve_multiple_of_zero_is_legal_and_means_no_reserve()
    {
        var tuning = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(0)));

        tuning.ReserveMultipleOfMax.ShouldBe(0);
    }

    private static InvalidEnergyTuningException Refuses(string leaf, ContentValue authored) =>
        Should.Throw<InvalidEnergyTuningException>(() => EnergyTuning.Read(Replace(leaf, authored)));

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
