using Shouldly;
using SkiaSharp;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

public sealed class PaletteQuantiseStepTests
{
    /// <summary>Stated here so the snap assertion below compares against a literal, not the palette the fixture was built from.</summary>
    private const string AstralBaseHue = "#7A5AD8";

    /// <summary>The astral base nudged 16 units up in every channel: 27.7 RGB units from base, 91 from the next-nearest hue.</summary>
    private const string OffPaletteHue = "#8A6AE8";

    /// <summary>A step that quietly ran on non-biome rows would recolour the entire UI kit to whatever biome happened to be in scope.</summary>
    [Fact]
    public void Run_is_a_pixel_identical_no_op_on_a_row_that_names_no_biome()
    {
        var row = ManifestRows.Require(ManifestRows.NonBiomeUiIcon);
        row.Biome.ShouldBeNull("this case is about a row 15 §A5 gives no palette");
        row.Atlas.ShouldBe("atlas_ui");
        var spec = TestSpecs.FromRow(ManifestRows.NonBiomeUiIcon);
        spec.IsBiomeScoped.ShouldBeFalse();

        var fixture = SyntheticAsset.Chibi();
        var before = Pixels.AllPixels(fixture.Image);
        before.Count.ShouldBe(SyntheticAsset.Canvas * SyntheticAsset.Canvas);
        Pixels.DistinctColourCount(fixture.Image).ShouldBeGreaterThan(
            2, "a flat or empty image would be identical to anything and prove nothing");

        var result = new PaletteQuantiseStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        result.Outcome.ShouldBe(StepOutcome.SkippedNotApplicable);
        result.Reason.ShouldNotBeNullOrWhiteSpace();
        Pixels.AllPixels(result.Image).ShouldBe(before);
    }

    [Fact]
    public void Run_snaps_a_known_off_palette_pixel_to_the_15_A5_hue_it_is_nearest()
    {
        var row = ManifestRows.Require(ManifestRows.BiomeScopedCharacter);
        row.Biome.ShouldBe("astral");
        row.PaletteColours.ShouldNotBeNull();
        row.PaletteColours.Base.ShouldBe(AstralBaseHue);

        var fixture = SyntheticAsset.BiomePalette(row.PaletteColours);
        fixture.OffPaletteColour.ShouldBe(SKColor.Parse(OffPaletteHue));

        // The row's real 512x512 delivery size rides along unused: step 3 recolours, it does not
        // resize, so a 32x32 fixture is the whole of what it needs.
        var spec = TestSpecs.FromRow(ManifestRows.BiomeScopedCharacter);
        spec.IsBiomeScoped.ShouldBeTrue();

        var result = new PaletteQuantiseStep().Run(
            new AssetStepInput(fixture.Image, spec, StatedThresholds.ForSyntheticFixtures()));

        result.Outcome.ShouldBe(StepOutcome.Applied);
        result.Image.GetPixel(fixture.OffPalettePixel.X, fixture.OffPalettePixel.Y)
            .ShouldBe(SKColor.Parse(AstralBaseHue));
    }
}
