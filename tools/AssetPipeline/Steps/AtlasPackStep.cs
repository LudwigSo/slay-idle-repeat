using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>One candidate member of an atlas: its manifest row and its processed image.</summary>
/// <param name="Asset">The manifest row — the packer reads its id, its atlas and its cut ruling.</param>
/// <param name="Image">The image as it came out of `15` §B4 step 6.</param>
public sealed record AtlasPackEntry(ArtAsset Asset, SKBitmap Image);

/// <summary>What `15` §B4 step 7 is given.</summary>
/// <param name="AtlasId">A `15` §D2 atlas id, or the concrete reference a row carries.</param>
/// <param name="Entries">Every candidate member, in any order — the packer sorts them itself.</param>
/// <param name="Thresholds">The threshold set, for consistency with the per-asset steps.</param>
public sealed record AtlasPackInput(
    string AtlasId, IReadOnlyList<AtlasPackEntry> Entries, ThresholdSet Thresholds);

/// <summary>One asset the packer left out, and why.</summary>
/// <param name="AssetId">The excluded asset's id.</param>
/// <param name="Reason">The stated reason — a ruling, or `15` §D2 assigning no atlas.</param>
public sealed record AtlasExclusion(string AssetId, string Reason);

/// <summary>Where one asset landed on a page.</summary>
/// <param name="AssetId">The asset's id.</param>
/// <param name="PageIndex">Zero-based page.</param>
/// <param name="X">Left edge, in pixels.</param>
/// <param name="Y">Top edge, in pixels.</param>
/// <param name="Width">Placed width, in pixels.</param>
/// <param name="Height">Placed height, in pixels.</param>
public sealed record AtlasPlacement(
    string AssetId, int PageIndex, int X, int Y, int Width, int Height);

/// <summary>One atlas page, at or under `15` §C's 2048x2048 cap.</summary>
/// <param name="Index">Zero-based page index.</param>
/// <param name="Width">Page width, in pixels.</param>
/// <param name="Height">Page height, in pixels.</param>
/// <param name="Placements">Everything on this page, in the packer's deterministic order.</param>
public sealed record AtlasPage(
    int Index, int Width, int Height, IReadOnlyList<AtlasPlacement> Placements);

/// <summary>What `15` §B4 step 7 returns.</summary>
/// <param name="AtlasId">The atlas that was packed.</param>
/// <param name="Pages">The pages, in index order.</param>
/// <param name="Placements">Every placement across every page, in the packer's order.</param>
/// <param name="Exclusions">Every candidate left out, each with a stated reason.</param>
/// <param name="Contradictions">Doc contradictions this pack ran into. See the step's remarks.</param>
/// <param name="Deviations">Deviations this pack knowingly took.</param>
public sealed record AtlasPackResult(
    string AtlasId,
    IReadOnlyList<AtlasPage> Pages,
    IReadOnlyList<AtlasPlacement> Placements,
    IReadOnlyList<AtlasExclusion> Exclusions,
    IReadOnlyList<DocContradiction> Contradictions,
    IReadOnlyList<DeclaredDeviation> Deviations);

/// <summary>
/// `15` §B4 step 7: <em>"Atlas pack -&gt; into the category atlas (see §D2)"</em>.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic: candidates are ordered by asset id, <b>ordinal</b>, before anything is placed,
/// so the same set in any input order produces byte-identical placements.
/// </para>
/// <para>
/// Two exclusions, each stated rather than implied: a row a ruling has cut, and a row `15` §D2
/// assigns no atlas ("Backgrounds are not atlased (they are full-screen and streamed per biome)").
/// </para>
/// <para>
/// ⚠️ <b>§D2 and §C contradict each other and this step says so.</b> §D2 names <em>one</em> atlas
/// per category while §C caps a single texture at 2048x2048. <c>atlas_hero</c> alone is 64 rows at
/// 512x512 — 16.8 M px against a 4.2 M px cap — so multi-page is arithmetically unavoidable, and
/// no doc authorises a paging convention. The pages are emitted deterministically and the result
/// carries <see cref="PageCapContradictionId"/> so the collision reaches the report instead of
/// being resolved in silence by whoever wrote the packer.
/// </para>
/// </remarks>
public sealed class AtlasPackStep : IAtlasStep
{
    /// <summary>The contradiction id this step emits for §D2 against §C.</summary>
    public const string PageCapContradictionId = "CON_ATLAS_PAGE_CAP";

    /// <inheritdoc/>
    public int Number => 7;

    /// <inheritdoc/>
    public string Id => "atlas-pack";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 7";

    /// <inheritdoc/>
    public AtlasPackResult Run(AtlasPackInput input) => throw new NotImplementedException();
}
