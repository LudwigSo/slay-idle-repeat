using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// `15` §B4 step 2: <em>"Trim to content -&gt; then pad to the target canvas with the subject
/// centered"</em>.
/// </summary>
/// <remarks>
/// <para>
/// Trim to the alpha bounding box, then pad to <see cref="AssetSpec.TargetSize"/> honouring
/// <see cref="AssetSpec.Pivot"/>: horizontally centred always; vertically centred for
/// <see cref="Doc15Pivots.Center"/>, bottom-aligned for <see cref="Doc15Pivots.BottomCenter"/>.
/// </para>
/// <para>
/// 🔒 Content larger than the target canvas is a loud failure, not a silent crop. Step 5 is where
/// size changes happen; a crop here would delete art nobody asked to delete, and the message names
/// the asset id and both sizes so the report says which asset and by how much.
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
        var target = spec.TargetSize;

        if (contentSize.Width > target.Width || contentSize.Height > target.Height)
        {
            throw new InvalidOperationException(
                $"Asset '{spec.Id}' ({spec.Section}) trims to {contentSize} of content, which does " +
                $"not fit its `15` §C delivery canvas of {target}. `15` §B4 puts size changes in " +
                "step 5, not step 2, so this is refused rather than cropped — cropping here would " +
                "delete art nobody asked to delete.");
        }

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
