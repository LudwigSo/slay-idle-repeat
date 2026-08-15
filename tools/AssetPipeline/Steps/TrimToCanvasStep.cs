using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>Trim to content, then pad to the target canvas with the subject centered.</summary>
/// <remarks>
/// <para>
/// "The target canvas" here is the WORKING canvas — the generation canvas the image arrived on —
/// and NOT the delivery size the manifest carries. Reading it as the delivery size would make the
/// later resize step an identity resample on every asset, which cannot be the intended split: this
/// step re-frames, and the resize step is the only one that changes size. It also puts the outline
/// steps at generation resolution, which is the only place they can work — repairing a 3-4 px
/// outline after a downscale to 128x128 would be destructive.
/// </para>
/// <para>
/// So: trim to the alpha bounding box, then pad back out to the source image's own dimensions,
/// honouring <see cref="AssetSpec.Pivot"/> — horizontally centred always; vertically centred for
/// <see cref="Doc15Pivots.Center"/>, bottom-aligned for <see cref="Doc15Pivots.BottomCenter"/>.
/// The job is consistent framing, not resizing.
/// </para>
/// <para>
/// There is no oversize guard, because there is no oversize case: content trimmed out of an image
/// can never exceed that image, so padding back to the image's own dimensions always fits.
/// </para>
/// <para>
/// The odd pixel goes right and bottom, by integer division of the slack.
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

        // The working canvas: the generation canvas the image arrived on, not spec.TargetSize. See
        // the type's remarks for why. Padding back to the image's own dimensions invents no number,
        // so there is no threshold here to calibrate.
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
