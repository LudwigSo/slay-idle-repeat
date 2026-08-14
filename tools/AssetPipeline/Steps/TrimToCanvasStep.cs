using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// `15` §B4 step 2: <em>"Trim to content -&gt; then pad to the target canvas with the subject
/// centered"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>"The target canvas" here is the WORKING canvas — the `15` §C <em>generation</em> canvas the
/// image arrived on (1024x1024, or 2048 for bosses and backgrounds) — and NOT the §C delivery size
/// the manifest carries.</b> `15` §B4 lists step 2 ("Trim to content -&gt; then pad to the target
/// canvas with the subject centered") and step 5 ("Resize -&gt; to the spec size in the manifest")
/// as two separate steps. Read step 2's canvas as the delivery size and step 5 becomes an identity
/// resample — its scale is exactly 1.0 on every asset, its Lanczos deviation is declared for a
/// resample that never resamples, and §B4's own step 5 is dead text. The only reading under which
/// all seven steps do work is this one: <b>step 2 re-frames, step 5 is the only step that changes
/// size.</b> It also puts steps 3 and 4 at generation resolution, which is the only place they can
/// work — repairing a 3-4 px outline after a downscale to 128x128 would be destructive.
/// </para>
/// <para>
/// So: trim to the alpha bounding box, then pad back out to the source image's own dimensions,
/// honouring <see cref="AssetSpec.Pivot"/> — horizontally centred always; vertically centred for
/// <see cref="Doc15Pivots.Center"/>, bottom-aligned for <see cref="Doc15Pivots.BottomCenter"/>.
/// The job is consistent framing, not resizing.
/// </para>
/// <para>
/// 🔒 <b>There is no oversize guard, because there is no oversize case.</b> Content trimmed out of
/// an image can never exceed that image, so padding back to the image's own dimensions always fits.
/// The guard this step used to carry compared the content against the <em>delivery</em> canvas and
/// refused a perfectly ordinary §C generation — a 1024x1024 render whose subject spans 900 px, for a
/// row delivering at 512x512 — before step 5, the step whose entire job is that downscale, ever ran.
/// </para>
/// <para>
/// 🔒 <b>The odd pixel goes right and bottom</b>, by integer division of the slack.
/// <see cref="Qa.Checks.CanvasAndPivotCheck"/> grades the delivered image against exactly that
/// convention, so the two must not drift apart.
/// </para>
/// </remarks>
public sealed class TrimToCanvasStep : IAssetStep
{
    /// <summary>The measurement key for the trimmed content's width.</summary>
    public const string ContentWidthMeasurement = "contentWidthPx";

    /// <summary>The measurement key for the trimmed content's height.</summary>
    public const string ContentHeightMeasurement = "contentHeightPx";

    /// <inheritdoc/>
    public int Number => 2;

    /// <inheritdoc/>
    public string Id => "trim-to-canvas";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 2";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var spec = input.Spec;
        var image = Raster.From(input.Image);
        var content = image.OpaqueBounds();

        if (content.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Asset '{spec.Id}' ({spec.Section}) has no visible pixel to trim to. `15` §B4 " +
                "step 2 centres the subject on the target canvas, and an image `15` §B4 step 1 " +
                "keyed away entirely has no subject to centre.");
        }

        var contentSize = new PixelSize(content.Width, content.Height);

        // 🔒 The working canvas: the `15` §C generation canvas the image arrived on, not
        // spec.TargetSize. This step re-frames at generation resolution; step 5 is the only step
        // that changes size. See the type's remarks for why the alternative reading makes §B4's own
        // step 5 dead text. Padding back to the image's own dimensions invents no number, so there
        // is no threshold here and nothing for steering rule S6 to catch.
        var target = new PixelSize(image.Width, image.Height);

        var left = (target.Width - contentSize.Width) / 2;
        var top = spec.Pivot switch
        {
            Doc15Pivots.Center => (target.Height - contentSize.Height) / 2,
            Doc15Pivots.BottomCenter => target.Height - contentSize.Height,
            _ => throw new InvalidOperationException(
                $"Asset '{spec.Id}' ({spec.Section}) carries the pivot '{spec.Pivot}'. `15` §C " +
                $"names {string.Join(" and ", Doc15Pivots.All)} and nothing else, so there is no " +
                "authorised placement for this one."),
        };

        var padded = Raster.Blank(target.Width, target.Height);
        for (var y = 0; y < contentSize.Height; y++)
        {
            for (var x = 0; x < contentSize.Width; x++)
            {
                padded.CopyPixelFrom(image, content.Left + x, content.Top + y, left + x, top + y);
            }
        }

        return new AssetStepResult(
            Number,
            Id,
            StepOutcome.Applied,
            padded.ToBitmap(),
            string.Empty,
            [
                new StepMeasurement(ContentWidthMeasurement, contentSize.Width, "px", DocReference),
                new StepMeasurement(ContentHeightMeasurement, contentSize.Height, "px", DocReference),
            ],
            []);
    }
}
