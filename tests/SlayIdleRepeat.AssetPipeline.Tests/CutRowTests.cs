using Shouldly;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// C10 — a row a ruling has cut is skipped <em>explicitly</em>, never by accident.
/// </summary>
/// <remarks>
/// Ruling O8 (2026-08-12) cut all 32 `15` §E19 VFX sprite-sheet rows: VFX are procedural in-engine.
/// Those rows also happen to carry no pivot, so a pipeline that skipped them because
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

        // Deliberately the uncalibrated set: a cut row must not need a single threshold, because
        // nothing is supposed to touch it.
        var run = new AssetPipeline().Run(fixture.Image, row, ThresholdSet.Uncalibrated());

        run.AssetId.ShouldBe(row.Id);
        run.Outcome.ShouldBe(StepOutcome.SkippedCutByRuling);
        run.Reason.ShouldContain(row.Cut, Case.Sensitive);
        run.Steps.ShouldBeEmpty();
    }
}
