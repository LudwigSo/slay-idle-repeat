using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>One candidate member of an atlas: its manifest row and its processed image.</summary>
/// <param name="Asset">The manifest row — the packer reads its id, its atlas and its cut ruling.</param>
/// <param name="Image">The image as it came out of the export step.</param>
public sealed record AtlasPackEntry(ArtAsset Asset, SKBitmap Image);

/// <summary>What the atlas-packing step is given.</summary>
/// <param name="AtlasId">The atlas id, or the concrete reference a row carries.</param>
/// <param name="Entries">Every candidate member, in any order — the packer sorts them itself.</param>
/// <param name="Thresholds">The threshold set, for consistency with the per-asset steps.</param>
public sealed record AtlasPackInput(
    string AtlasId, IReadOnlyList<AtlasPackEntry> Entries, ThresholdSet Thresholds);

/// <summary>One asset the packer left out, and why.</summary>
/// <param name="AssetId">The excluded asset's id.</param>
/// <param name="Reason">The stated reason — a ruling, or the row being assigned no atlas.</param>
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

/// <summary>One atlas page, at or under the max single texture cap.</summary>
/// <param name="Index">Zero-based page index.</param>
/// <param name="Width">Page width, in pixels.</param>
/// <param name="Height">Page height, in pixels.</param>
/// <param name="Placements">Everything on this page, in the packer's deterministic order.</param>
public sealed record AtlasPage(
    int Index, int Width, int Height, IReadOnlyList<AtlasPlacement> Placements);

/// <summary>What the atlas-packing step returns.</summary>
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

/// <summary>Atlas pack: packs an atlas's members into the category atlas.</summary>
/// <remarks>
/// <para>
/// Deterministic: candidates are ordered by asset id, <b>ordinal</b>, before anything is placed,
/// so the same set in any input order produces byte-identical placements.
/// </para>
/// <para>
/// Two exclusions, each stated rather than implied: a row a ruling has cut, and a row assigned no
/// atlas (backgrounds are streamed full-screen instead).
/// </para>
/// <para>
/// The "one atlas per category" rule and the max-single-texture-size cap contradict each other for
/// a large enough category, making multi-page arithmetically unavoidable with no authorised paging
/// convention. The pages are emitted deterministically and the result carries
/// <see cref="PageCapContradictionId"/> so the collision reaches the report instead of being
/// resolved in silence by whoever wrote the packer.
/// </para>
/// </remarks>
public sealed class AtlasPackStep : IAtlasStep
{
    /// <summary>The contradiction id this step emits for the atlas/texture-cap conflict.</summary>
    public const string PageCapContradictionId = "CON_ATLAS_PAGE_CAP";

    /// <inheritdoc/>
    public int Number => 7;

    /// <inheritdoc/>
    public string Id => "atlas-pack";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 7";

    /// <inheritdoc/>
    public AtlasPackResult Run(AtlasPackInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var exclusions = new List<AtlasExclusion>();
        var members = new List<AtlasPackEntry>();

        // Ordinal, by asset id, before anything is placed, so the same set in any input order
        // produces byte-identical placements rather than different UVs on every run.
        foreach (var entry in input.Entries.OrderBy(entry => entry.Asset.Id, StringComparer.Ordinal))
        {
            var exclusion = ExclusionFor(entry.Asset);
            if (exclusion is not null)
            {
                exclusions.Add(exclusion);
                continue;
            }

            members.Add(entry);
        }

        var placements = Place(members);

        return new AtlasPackResult(
            input.AtlasId,
            Paginate(placements),
            placements,
            exclusions,
            [PageCapContradiction()],
            []);
    }

    /// <summary>Why a candidate is left out, or null when it belongs in the atlas.</summary>
    /// <param name="asset">The candidate's manifest row.</param>
    private static AtlasExclusion? ExclusionFor(ArtAsset asset)
    {
        if (asset.Cut is not null)
        {
            return new AtlasExclusion(
                asset.Id,
                $"A ruling cut this row: {asset.Cut}. A cut asset is not generated, so there is " +
                "nothing to pack.");
        }

        return asset.Atlas is null
            ? new AtlasExclusion(
                asset.Id,
                "`15` §D2 assigns this row no atlas: \"Backgrounds are not atlased (they are " +
                "full-screen and streamed per biome).\"")
            : null;
    }

    /// <summary>
    /// Shelf-packs the members left to right, top to bottom, opening a page at the texture cap.
    /// </summary>
    /// <param name="members">The members, already in the packer's order.</param>
    private static IReadOnlyList<AtlasPlacement> Place(IReadOnlyList<AtlasPackEntry> members)
    {
        var placements = new List<AtlasPlacement>(members.Count);
        var page = 0;
        var x = 0;
        var y = 0;
        var shelfHeight = 0;

        foreach (var member in members)
        {
            var width = member.Image.Width;
            var height = member.Image.Height;

            if (width > Doc15Authorised.MaxSingleTextureWidth
                || height > Doc15Authorised.MaxSingleTextureHeight)
            {
                throw new InvalidOperationException(
                    $"Asset '{member.Asset.Id}' is {width}×{height}, which does not fit `15` §C's " +
                    $"maximum single texture of {Doc15Authorised.MaxSingleTextureWidth}×" +
                    $"{Doc15Authorised.MaxSingleTextureHeight}. Splitting an asset across pages " +
                    "is not something any section of `15` authorises.");
            }

            if (x + width > Doc15Authorised.MaxSingleTextureWidth)
            {
                x = 0;
                y += shelfHeight;
                shelfHeight = 0;
            }

            if (y + height > Doc15Authorised.MaxSingleTextureHeight)
            {
                page++;
                x = 0;
                y = 0;
                shelfHeight = 0;
            }

            placements.Add(new AtlasPlacement(member.Asset.Id, page, x, y, width, height));
            x += width;
            shelfHeight = Math.Max(shelfHeight, height);
        }

        return placements;
    }

    /// <summary>Groups placements into pages, each at the texture cap.</summary>
    /// <param name="placements">Every placement, in the packer's order.</param>
    private static IReadOnlyList<AtlasPage> Paginate(IReadOnlyList<AtlasPlacement> placements) =>
    [
        .. placements
            .GroupBy(placement => placement.PageIndex)
            .OrderBy(page => page.Key)
            .Select(page => new AtlasPage(
                page.Key,
                Doc15Authorised.MaxSingleTextureWidth,
                Doc15Authorised.MaxSingleTextureHeight,
                [.. page])),
    ];

    /// <summary>
    /// The atlas/texture-cap conflict, with the arithmetic, so the collision reaches the report
    /// rather than being resolved in silence by whoever wrote the packer.
    /// </summary>
    /// <remarks>
    /// Emitted on every pack, not only on the ones that overflow: the contradiction is in the rules,
    /// not in the batch, so a small atlas that happens to fit on one page does not make them agree.
    /// </remarks>
    private static DocContradiction PageCapContradiction() => new(
        PageCapContradictionId,
        "15 §D2",
        "15 §C",
        "§D2 names one atlas per category; §C caps a single texture at " +
        $"{Doc15Authorised.MaxSingleTextureWidth}×{Doc15Authorised.MaxSingleTextureHeight}, which " +
        $"is {(long)Doc15Authorised.MaxSingleTextureWidth * Doc15Authorised.MaxSingleTextureHeight} " +
        "pixels. atlas_hero alone is 64 rows at 512×512 — 16,777,216 pixels against a 4,194,304 " +
        "pixel cap — so multi-page is arithmetically unavoidable and the two sections cannot both " +
        "be satisfied. No section of `15` authorises a paging convention, so the pages emitted here " +
        "are this packer's deterministic choice (ordinal by asset id, shelf-packed) and not a " +
        "convention the doc states.");
}
