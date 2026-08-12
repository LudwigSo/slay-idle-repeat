using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline.Qa;

/// <summary>
/// One processed asset, and everything the eleven `15` Part F items need in order to look at it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Part F is run <b>after</b> `15` §B4 — <see cref="Image"/> is step 6's output, not the
/// generated source. Checking a raw generation against §C's canvas size would grade the generator,
/// not the delivery, and the checklist's own wording ("Correct canvas size and pivot per §C",
/// "packed into the correct atlas") is about what ships.
/// </para>
/// <para>
/// 🔒 Everything here is supplied by the caller. No check reads a file, re-parses the manifest, or
/// reruns a step: a subject is a complete, inspectable record of what was judged, so M8-10 can
/// serialise one beside a failing verdict and a human can see exactly what the machine saw.
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
/// The `15` §B4 step 7 result the asset was packed by, or null when no pack has run. Null is not a
/// pass: item 10 reports <see cref="QaVerdict.Fail"/> for a row §D2 assigns an atlas to and a
/// <see cref="QaVerdict.Pass"/> on naming alone for a row it does not (backgrounds).
/// </param>
/// <param name="Thresholds">The threshold set. An uncalibrated key is a verdict, not an exception.</param>
/// <param name="Registry">
/// The `15` §A4 silhouettes already accepted in this asset's category. Pass
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
