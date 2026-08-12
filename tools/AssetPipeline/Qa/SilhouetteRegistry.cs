using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>One silhouette a reviewer has already accepted, kept so the next one can be told apart from it.</summary>
/// <param name="AssetId">The accepted asset's `15` §D1 id.</param>
/// <param name="Category">Its §D1 category prefix — see <see cref="AssetNaming.CategoryOf"/>.</param>
/// <param name="Mask">
/// Its 64x64 mask as <see cref="SilhouetteGate.Render"/> produced it. Stored rather than recomputed
/// so that acceptance records what was actually approved, not what today's renderer would make of
/// the source again.
/// </param>
public sealed record AcceptedSilhouette(string AssetId, string Category, SKBitmap Mask);

/// <summary>
/// The `15` §A4 silhouettes already accepted, keyed by `15` §D1 category.
/// </summary>
/// <remarks>
/// <para>
/// §A4's bar is <em>"If you cannot tell which character it is"</em> — a statement about telling one
/// asset apart from another, which needs the others. This is the "others": the set a new asset's
/// <see cref="SilhouetteMeasurement.Distinguishability"/> is measured against.
/// </para>
/// <para>
/// 🔒 <b>Per category, not per batch.</b> Part F item 11 says "in the same category" for the same
/// reason: a hero body and a currency icon being similar in silhouette says nothing, and comparing
/// across categories would drown a real collision between two enemies in noise.
/// </para>
/// <para>
/// 🔒 Immutable. <see cref="Accept"/> returns a new registry, the same way
/// <see cref="ThresholdSet.With"/> does, so a check can never widen the set it is being judged
/// against as a side effect of judging.
/// </para>
/// </remarks>
public sealed class SilhouetteRegistry
{
    /// <summary>A registry holding nothing — the state before any asset has been accepted.</summary>
    /// <remarks>
    /// An empty category is not a failure: the first asset of a category has nothing to be confused
    /// with, and <see cref="SilhouetteGate.Measure"/> reports
    /// <see cref="SilhouetteGate.MaximumDistinguishability"/> for it.
    /// </remarks>
    public static SilhouetteRegistry Empty { get; } = new();

    /// <summary>How many silhouettes are held, across every category.</summary>
    public int Count => throw new NotImplementedException();

    /// <summary>Every category holding at least one silhouette, in ordinal order.</summary>
    public IReadOnlyList<string> Categories => throw new NotImplementedException();

    /// <summary>This registry plus one accepted silhouette.</summary>
    /// <param name="assetId">The accepted asset's `15` §D1 id.</param>
    /// <param name="mask">Its 64x64 mask, from <see cref="SilhouetteGate.Render"/>.</param>
    /// <returns>A new registry. This one is unchanged.</returns>
    public SilhouetteRegistry Accept(string assetId, SKBitmap mask) =>
        throw new NotImplementedException();

    /// <summary>Everything accepted in one category, in the order it was accepted.</summary>
    /// <param name="category">A `15` §D1 category prefix. An unknown one yields an empty list.</param>
    public IReadOnlyList<AcceptedSilhouette> InCategory(string category) =>
        throw new NotImplementedException();
}
