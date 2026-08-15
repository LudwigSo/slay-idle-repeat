using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// Everything the pipeline steps need about one asset, read out of the manifest exactly once.
/// </summary>
/// <remarks>
/// A spec exists only for an asset the pipeline will actually process, so <see cref="Resolve"/>
/// refuses loudly rather than filling a hole: missing delivery size, missing/unrecognised pivot,
/// or an asset cut by a ruling (which <see cref="AssetPipeline.Run"/> should already have skipped).
/// </remarks>
/// <param name="Id">The asset id.</param>
/// <param name="Section">The manifest section the row was transcribed from.</param>
/// <param name="TargetSize">
/// The delivery size. Only the resize step resamples to it; earlier steps re-frame on the
/// generation canvas the image arrived on. See <see cref="TrimToCanvasStep"/>.
/// </param>
/// <param name="Pivot">One of <see cref="Doc15Pivots.All"/>.</param>
/// <param name="Atlas">The atlas, or null where none is assigned (backgrounds).</param>
/// <param name="Biome">The biome key where the asset is biome-scoped, else null.</param>
/// <param name="PaletteColours">The biome's locked hues where biome-scoped, else null.</param>
public sealed record AssetSpec(
    string Id,
    string Section,
    PixelSize TargetSize,
    string Pivot,
    string? Atlas,
    string? Biome,
    Palette? PaletteColours)
{
    /// <summary>True when the row names a biome AND carries that biome's palette.</summary>
    public bool IsBiomeScoped => Biome is not null && PaletteColours is not null;

    /// <summary>Reads one manifest row into a spec, or fails loudly. See the type's remarks.</summary>
    /// <param name="asset">A row from <see cref="AssetManifestSet.Art"/>.</param>
    public static AssetSpec Resolve(ArtAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (asset.Cut is not null)
        {
            throw new InvalidOperationException(
                $"Asset '{asset.Id}' ({asset.Section}) was cut by a ruling: {asset.Cut}. " +
                $"{nameof(AssetPipeline)}.{nameof(AssetPipeline.Run)} skips a cut row before a " +
                "spec is ever built, so resolving one means a caller has lost track of the ruling.");
        }

        // Deliberately not caught: a missing size should refuse the row, never default it.
        var targetSize = asset.RequireDeliverySize();

        var pivot = asset.Pivot ?? throw new InvalidOperationException(
            $"Asset '{asset.Id}' ({asset.Section}) carries no pivot: `15` §C authorises one for " +
            "characters (bottom-center) and for icons (center) and for nothing else, and 284 of " +
            "the 974 rows carry none. `15` §B4 step 2 pads against the pivot, so there is nothing " +
            "to invent here — the row is refused instead.");

        if (!Doc15Pivots.All.Contains(pivot, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Asset '{asset.Id}' ({asset.Section}) carries the pivot '{pivot}'. `15` §C names " +
                $"{string.Join(" and ", Doc15Pivots.All)} and nothing else.");
        }

        return new AssetSpec(
            asset.Id,
            asset.Section,
            targetSize,
            pivot,
            asset.Atlas,
            asset.Biome,
            asset.PaletteColours);
    }
}
