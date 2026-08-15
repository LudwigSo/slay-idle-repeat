using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>One processed asset, and everything the eleven checklist items need to look at it.</summary>
/// <remarks>
/// <para>
/// Run after the pipeline, not against the raw generation — <see cref="Image"/> is the last step's
/// output. Checking a raw generation would grade the generator, not the delivery.
/// </para>
/// <para>
/// Everything here is supplied by the caller: no check reads a file, re-parses the manifest, or
/// reruns a step, so a subject is a complete, inspectable record of what was judged.
/// </para>
/// </remarks>
/// <param name="Image">
/// The processed image, <see cref="SKColorType.Rgba8888"/> / <see cref="SKAlphaType.Unpremul"/>.
/// </param>
/// <param name="Spec">The manifest-derived spec the pipeline ran against.</param>
/// <param name="Asset">
/// The manifest row itself. Item 10 compares the delivered file's stem against
/// <see cref="ArtAsset.Id"/> — the register's id, not a value re-derived from the spec.
/// </param>
/// <param name="DeliveredFileName">
/// The name the asset would ship under, e.g. <c>icon_perk_executioner.png</c>. Item 10's subject.
/// </param>
/// <param name="AtlasPack">
/// The atlas-pack result the asset was packed by, or null when no pack has run. Null is not a pass:
/// item 10 reports <see cref="QaVerdict.Fail"/> for a row that should have an atlas and a
/// <see cref="QaVerdict.Pass"/> on naming alone for a row that should not (backgrounds).
/// </param>
/// <param name="Thresholds">The threshold set. An uncalibrated key is a verdict, not an exception.</param>
/// <param name="Registry">
/// The silhouettes already accepted in this asset's category. Pass
/// <see cref="SilhouetteRegistry.Empty"/> when nothing has been accepted yet.
/// </param>
public sealed record QaSubject(
    SKBitmap Image,
    AssetSpec Spec,
    ArtAsset Asset,
    string DeliveredFileName,
    AtlasPackResult? AtlasPack,
    ThresholdSet Thresholds,
    SilhouetteRegistry Registry);
