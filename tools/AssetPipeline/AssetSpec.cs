using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// Everything the seven steps need about one asset, read out of the manifest exactly once.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <see cref="Resolve"/> is the single place M8-09's register is turned into pipeline input.
/// Nothing else in this project reads a manifest row, and nothing at all re-parses
/// <c>game-data/assets/*.json</c>.
/// </para>
/// <para>
/// 🔒 A spec exists only for an asset the pipeline will actually process. <see cref="Resolve"/>
/// therefore refuses three rows loudly rather than filling a hole:
/// </para>
/// <list type="bullet">
///   <item>no delivery size — <see cref="ArtAsset.RequireDeliverySize"/> throws, and this type does
///   not catch it (144 of the 974 rows are in that state, tracked as DSC_MISSING_SIZES);</item>
///   <item>no pivot — `15` §C authorises a pivot for characters and for icons and for nothing
///   else, and 284 rows carry none;</item>
///   <item>cut by a ruling — <see cref="AssetPipeline.Run"/> skips those before a spec is ever
///   built, so resolving one means a caller has lost track of the ruling.</item>
/// </list>
/// </remarks>
/// <param name="Id">The `15` §D1 asset id.</param>
/// <param name="Section">The `15` §E-section the row was transcribed from.</param>
/// <param name="TargetSize">
/// `15` §C <b>delivery</b> size. Step 5 is the only step that resamples to it — step 2 re-frames on
/// the §C <em>generation</em> canvas the image arrived on and never reads this. See
/// <see cref="TrimToCanvasStep"/>.
/// </param>
/// <param name="Pivot">One of <see cref="Doc15Pivots.All"/>.</param>
/// <param name="Atlas">`15` §D2 atlas, or null where §D2 assigns none (backgrounds).</param>
/// <param name="Biome">The biome key where the asset is biome-scoped, else null.</param>
/// <param name="PaletteColours">The biome's locked six hues where biome-scoped, else null.</param>
public sealed record AssetSpec(
    string Id,
    string Section,
    PixelSize TargetSize,
    string Pivot,
    string? Atlas,
    string? Biome,
    Palette? PaletteColours)
{
    /// <summary>
    /// True when `15` §B4 step 3 applies: the row names a biome AND carries that biome's palette.
    /// </summary>
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

        // 🔒 Not caught. M8-09's RequireDeliverySize() throws rather than defaulting, and a
        // pipeline that swallowed it would resize 144 assets to a number nobody wrote down.
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
