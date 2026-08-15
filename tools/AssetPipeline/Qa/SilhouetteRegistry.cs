using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>One silhouette a reviewer has already accepted, kept so the next one can be told apart from it.</summary>
/// <param name="AssetId">The accepted asset's id.</param>
/// <param name="Category">Its category prefix — see <see cref="AssetNaming.CategoryOf"/>.</param>
/// <param name="Mask">
/// Its 64x64 mask as <see cref="SilhouetteGate.Render"/> produced it. Stored rather than recomputed
/// so acceptance records what was actually approved, not what today's renderer would make of the
/// source again.
/// </param>
public sealed record AcceptedSilhouette(string AssetId, string Category, SKBitmap Mask);

/// <summary>The silhouettes already accepted, keyed by category.</summary>
/// <remarks>
/// <para>
/// This is the set a new asset's <see cref="SilhouetteMeasurement.Distinguishability"/> is measured
/// against, kept per category rather than per batch: a hero body and a currency icon being similar
/// in silhouette says nothing, and comparing across categories would drown a real collision in noise.
/// </para>
/// <para>
/// Immutable — <see cref="Accept"/> returns a new registry — so a check can never widen the set it
/// is being judged against as a side effect of judging.
/// </para>
/// </remarks>
public sealed class SilhouetteRegistry
{
    /// <summary>
    /// Everything accepted, in acceptance order. A flat list rather than a dictionary of lists
    /// since both "acceptance order" and "category order" stay cheaply derivable.
    /// </summary>
    private readonly IReadOnlyList<AcceptedSilhouette> accepted;

    private SilhouetteRegistry()
        : this([])
    {
    }

    private SilhouetteRegistry(IReadOnlyList<AcceptedSilhouette> accepted) => this.accepted = accepted;

    /// <summary>A registry holding nothing — the state before any asset has been accepted.</summary>
    /// <remarks>
    /// An empty category is not a failure: the first asset of a category has nothing to be confused
    /// with, and <see cref="SilhouetteGate.Measure"/> reports
    /// <see cref="SilhouetteGate.MaximumDistinguishability"/> for it.
    /// </remarks>
    public static SilhouetteRegistry Empty { get; } = new();

    /// <summary>How many silhouettes are held, across every category.</summary>
    public int Count => accepted.Count;

    /// <summary>Every category holding at least one silhouette, in ordinal order.</summary>
    public IReadOnlyList<string> Categories =>
    [
        .. accepted
            .Select(entry => entry.Category)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(category => category, StringComparer.Ordinal),
    ];

    /// <summary>This registry plus one accepted silhouette.</summary>
    /// <param name="assetId">The accepted asset's id.</param>
    /// <param name="mask">Its 64x64 mask, from <see cref="SilhouetteGate.Render"/>.</param>
    /// <returns>A new registry. This one is unchanged.</returns>
    public SilhouetteRegistry Accept(string assetId, SKBitmap mask)
    {
        ArgumentNullException.ThrowIfNull(assetId);
        ArgumentNullException.ThrowIfNull(mask);

        // Category is derived, never taken as a parameter, so a caller can't misfile a hero under
        // "icon" and have it measured against the wrong bucket forever.
        var entry = new AcceptedSilhouette(assetId, AssetNaming.CategoryOf(assetId), mask);
        return new SilhouetteRegistry([.. accepted, entry]);
    }

    /// <summary>Everything accepted in one category, in the order it was accepted.</summary>
    /// <param name="category">A category prefix. An unknown one yields an empty list.</param>
    public IReadOnlyList<AcceptedSilhouette> InCategory(string category)
    {
        ArgumentNullException.ThrowIfNull(category);

        return
        [
            .. accepted.Where(entry =>
                string.Equals(entry.Category, category, StringComparison.Ordinal)),
        ];
    }
}
