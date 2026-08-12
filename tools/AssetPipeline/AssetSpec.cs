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
/// <param name="TargetSize">`15` §C delivery size — steps 2 and 5 pad and resample to it.</param>
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
    public static AssetSpec Resolve(ArtAsset asset) => throw new NotImplementedException();
}
