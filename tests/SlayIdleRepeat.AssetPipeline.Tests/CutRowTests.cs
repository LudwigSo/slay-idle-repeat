using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>A row a ruling has cut is skipped explicitly, never by accident.</summary>
/// <remarks>
/// The cut VFX rows also happen to carry no pivot, so a pipeline that skipped them because
/// <see cref="AssetSpec.Resolve"/> refused them would look exactly like one that honoured the
/// ruling — right outcome, wrong reason, and silent the day a live row loses its pivot.
/// </remarks>
public sealed class CutRowTests
{
    private const int VfxRowCount = 32;

    [Fact]
    public void Every_15_E19_row_is_cut_so_the_case_below_cannot_have_picked_a_live_one()
    {
        var rows = PipelineFiles.Shipped.ArtInSection(ManifestRows.VfxSection).ToArray();

        rows.Length.ShouldBe(VfxRowCount);
        rows.ShouldAllBe(row => row.Cut != null);
        PipelineFiles.Shipped.AtlasMembers("atlas_vfx").ShouldBeEmpty();
    }

    [Fact]
    public void Run_skips_a_cut_row_naming_the_ruling_and_runs_no_step()
    {
        var row = ManifestRows.Require(ManifestRows.CutVfxSheet);
        row.Section.ShouldBe(ManifestRows.VfxSection);
        row.Cut.ShouldNotBeNull();
        var fixture = SyntheticAsset.Chibi();

        // Uncalibrated on purpose: a cut row must not need a single threshold since nothing touches it.
        var run = new AssetPipeline().Run(fixture.Image, row, ThresholdSet.Uncalibrated());

        run.AssetId.ShouldBe(row.Id);
        run.Outcome.ShouldBe(StepOutcome.SkippedCutByRuling);
        run.Reason.ShouldContain(row.Cut, Case.Sensitive);
        run.Steps.ShouldBeEmpty();
    }
}
