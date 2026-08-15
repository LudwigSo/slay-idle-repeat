using Shouldly;
using SlayIdleRepeat.AssetManifest;
using Xunit;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>Manifest holes propagate out of <see cref="AssetSpec.Resolve"/> loudly — never caught, never defaulted.</summary>
public sealed class AssetSpecTests
{
    private const int RowsWithoutDeliverySize = 144;
    private const int RowsWithoutPivot = 284;

    /// <summary>
    /// Guards the two cases below: if the shipped register ever fills these holes in, those cases
    /// must fail loudly rather than quietly test nothing.
    /// </summary>
    [Fact]
    public void The_shipped_register_still_carries_the_holes_these_cases_rely_on()
    {
        var rows = PipelineFiles.Shipped.Art.Assets;

        rows.Count(row => row.DeliverySize is null).ShouldBe(RowsWithoutDeliverySize);
        rows.Count(row => row.Pivot is null).ShouldBe(RowsWithoutPivot);
    }

    [Fact]
    public void Resolve_propagates_the_missing_delivery_size_instead_of_defaulting_it()
    {
        var row = ManifestRows.Require(ManifestRows.RowWithoutDeliverySize);
        row.DeliverySize.ShouldBeNull();
        row.Cut.ShouldBeNull("otherwise the refusal below could be about the ruling");

        var exception = Should.Throw<InvalidOperationException>(() => AssetSpec.Resolve(row));

        exception.Message.ShouldContain(row.Id, Case.Sensitive);
        exception.Message.ShouldContain("DSC_MISSING_SIZES", Case.Sensitive);
    }

    /// <summary>The row picked has a delivery size, so only the missing pivot can trigger the refusal.</summary>
    [Fact]
    public void Resolve_refuses_a_row_15_C_states_no_pivot_for_naming_the_asset_and_the_section()
    {
        var row = ManifestRows.Require(ManifestRows.RowWithoutPivot);
        row.Pivot.ShouldBeNull();
        row.DeliverySize.ShouldNotBeNull("otherwise the refusal below could be about the size");
        row.Cut.ShouldBeNull("otherwise the refusal below could be about the ruling");

        var exception = Should.Throw<InvalidOperationException>(() => AssetSpec.Resolve(row));

        exception.Message.ShouldContain(row.Id, Case.Sensitive);
        exception.Message.ShouldContain("§C", Case.Sensitive);
    }

    [Fact]
    public void Resolve_reads_a_complete_row_into_the_spec_the_steps_need()
    {
        var row = ManifestRows.Require(ManifestRows.BiomeScopedCharacter);

        var spec = AssetSpec.Resolve(row);

        spec.Id.ShouldBe(row.Id);
        spec.TargetSize.ShouldBe(new PixelSize(512, 512));
        spec.Pivot.ShouldBe(Doc15Pivots.BottomCenter);
        spec.Atlas.ShouldBe("atlas_biome_8");
        spec.IsBiomeScoped.ShouldBeTrue();
    }
}
