using SkiaSharp;
using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// `15` §B4 step 5: <em>"Resize -&gt; to the spec size in the manifest (Lanczos, then sharpen
/// 0.4)"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>There is no Lanczos resampler in SkiaSharp.</b> <c>SKFilterMode</c> offers Nearest and
/// Linear only; the high-quality option is <c>SKSamplingOptions(SKCubicResampler)</c> with
/// Mitchell (B=C=1/3) or CatmullRom. Shelling out to a native resampler is forbidden, so this step
/// resamples with the cubic resampler and emits <see cref="LanczosDeviationId"/> saying so.
/// </para>
/// <para>
/// 🔒 <b>Sharpen touches RGB only, never alpha.</b> Sharpening the alpha channel re-creates
/// precisely the semi-transparent fringe step 1 exists to remove. The amount is authorised (§B4:
/// 0.4, see <see cref="Doc15Authorised.SharpenAmount"/>); the radius is not, and lives as
/// <see cref="ThresholdKeys.ResizeSharpenRadius"/>.
/// </para>
/// </remarks>
public sealed class ResizeStep : IAssetStep
{
    /// <summary>The deviation id this step emits for the missing Lanczos resampler.</summary>
    public const string LanczosDeviationId = "DEV_LANCZOS_UNAVAILABLE";

    /// <summary>The measurement key for the scale factor applied to the width.</summary>
    public const string ScaleMeasurement = "resizeScale";

    /// <summary>
    /// The cubic resampler this step substitutes for Lanczos, named in the deviation so a reader
    /// never has to guess which one produced a given batch.
    /// </summary>
    private const string ResamplerName = "SKCubicResampler.Mitchell (B=C=1/3)";

    /// <inheritdoc/>
    public int Number => 5;

    /// <inheritdoc/>
    public string Id => "resize";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 5";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var radius = input.Thresholds.RequireNumber(ThresholdKeys.ResizeSharpenRadius);
        var target = input.Spec.TargetSize;

        using var resampled = Resample(input.Image, target);
        var sharpened = Sharpen(resampled, Doc15Authorised.SharpenAmount, radius);

        var scale = target.Width / (double)input.Image.Width;

        return new AssetStepResult(
            Number,
            Id,
            StepOutcome.Applied,
            sharpened,
            $"Resampled with {ResamplerName} rather than `15` §B4 step 5's Lanczos, which " +
            "SkiaSharp does not offer.",
            [new StepMeasurement(ScaleMeasurement, scale, "ratio", DocReference)],
            [
                new DeclaredDeviation(
                    LanczosDeviationId,
                    DocReference,
                    "Resize to the spec size in the manifest (Lanczos, then sharpen 0.4).",
                    $"Resampled with {ResamplerName}, then unsharp-masked at the authorised amount " +
                    $"{Doc15Authorised.SharpenAmount} over RGB only.",
                    "SkiaSharp exposes no Lanczos kernel — SKFilterMode is Nearest and Linear only, " +
                    "and the highest-quality managed path is SKSamplingOptions with a cubic " +
                    "resampler. A native resampler would mean shelling out to a binary, which this " +
                    "toolchain forbids. Mitchell is not Lanczos: it is less sharp and rings less, so " +
                    "an asset resampled here is marginally softer than `15` §B4 step 5 describes."),
            ]);
    }

    /// <summary>
    /// The first half of the step on its own: resample to <paramref name="target"/> with no
    /// sharpening.
    /// </summary>
    /// <remarks>
    /// 🔒 Public because the two halves make two different claims and must be separable. "Sharpen
    /// does not touch alpha" is only checkable against the alpha the resample alone produced.
    /// </remarks>
    /// <param name="source">The image to resample.</param>
    /// <param name="target">The `15` §C delivery size.</param>
    public SKBitmap Resample(SKBitmap source, PixelSize target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        // 🔒 Rgba8888 / Unpremul on both sides. Letting Skia premultiply for the resample would
        // destroy colour in low-alpha pixels, which is the halo step 1 spent its run removing.
        var info = Raster.InfoFor(target.Width, target.Height);
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell))
               ?? throw new InvalidOperationException(
                   $"Skia could not resample a {source.Width}×{source.Height} image to " +
                   $"{target} with {ResamplerName}.");
    }

    /// <summary>
    /// The second half on its own: an unsharp mask over RGB, leaving every alpha byte untouched.
    /// </summary>
    /// <param name="source">The resampled image.</param>
    /// <param name="amount">`15` §B4's 0.4.</param>
    /// <param name="radius">The uncalibrated radius the caller has stated.</param>
    public SKBitmap Sharpen(SKBitmap source, double amount, double radius)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (radius <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius),
                radius,
                "An unsharp mask needs a blur to subtract, and a radius of zero blurs nothing.");
        }

        var image = Raster.From(source);
        var blurred = GaussianBlurRgb(image, radius);

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var index = image.IndexOf(x, y) * 3;
                var colour = image.ColourAt(x, y);

                // 🔒 RGB only. The alpha byte is copied through untouched by construction: nothing
                // below writes it.
                image.SetRgb(
                    x,
                    y,
                    Raster.ToChannel(colour.Red + ((colour.Red - blurred[index]) * amount)),
                    Raster.ToChannel(colour.Green + ((colour.Green - blurred[index + 1]) * amount)),
                    Raster.ToChannel(colour.Blue + ((colour.Blue - blurred[index + 2]) * amount)));
            }
        }

        return image.ToBitmap();
    }

    /// <summary>
    /// A separable Gaussian blur of the colour channels, weighted by alpha.
    /// </summary>
    /// <remarks>
    /// 🔒 Alpha-weighted so a transparent pixel contributes nothing. An unweighted blur would mix
    /// the RGB of the fully transparent frame — zero, in straight alpha — into the subject's edge
    /// and darken it, which is a halo of a different colour rather than no halo at all.
    /// </remarks>
    /// <param name="image">The image to blur.</param>
    /// <param name="sigma">The Gaussian's standard deviation, in pixels.</param>
    /// <returns>Blurred red, green and blue per pixel, row-major, three doubles each.</returns>
    private static double[] GaussianBlurRgb(Raster image, double sigma)
    {
        var kernel = GaussianKernel(sigma);
        var reach = (kernel.Length - 1) / 2;
        var pixels = image.PixelCount;

        var horizontalColour = new double[pixels * 3];
        var horizontalWeight = new double[pixels];

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var index = image.IndexOf(x, y);
                for (var tap = -reach; tap <= reach; tap++)
                {
                    var sampleX = Math.Clamp(x + tap, 0, image.Width - 1);
                    var colour = image.ColourAt(sampleX, y);
                    var weight = kernel[tap + reach] * (colour.Alpha / 255d);

                    horizontalColour[(index * 3) + 0] += colour.Red * weight;
                    horizontalColour[(index * 3) + 1] += colour.Green * weight;
                    horizontalColour[(index * 3) + 2] += colour.Blue * weight;
                    horizontalWeight[index] += weight;
                }
            }
        }

        var blurred = new double[pixels * 3];

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var index = image.IndexOf(x, y);
                var red = 0d;
                var green = 0d;
                var blue = 0d;
                var weight = 0d;

                for (var tap = -reach; tap <= reach; tap++)
                {
                    var sampleY = Math.Clamp(y + tap, 0, image.Height - 1);
                    var sample = image.IndexOf(x, sampleY);
                    var factor = kernel[tap + reach];

                    red += horizontalColour[(sample * 3) + 0] * factor;
                    green += horizontalColour[(sample * 3) + 1] * factor;
                    blue += horizontalColour[(sample * 3) + 2] * factor;
                    weight += horizontalWeight[sample] * factor;
                }

                var colour = image.ColourAt(x, y);
                var visible = weight > 0d;

                blurred[(index * 3) + 0] = visible ? red / weight : colour.Red;
                blurred[(index * 3) + 1] = visible ? green / weight : colour.Green;
                blurred[(index * 3) + 2] = visible ? blue / weight : colour.Blue;
            }
        }

        return blurred;
    }

    /// <summary>A normalised one-dimensional Gaussian kernel.</summary>
    /// <param name="sigma">The standard deviation, in pixels.</param>
    private static double[] GaussianKernel(double sigma)
    {
        // Three sigmas holds better than 99% of the kernel's mass; beyond that the taps round to
        // nothing and only cost time.
        var reach = Math.Max(1, (int)Math.Ceiling(sigma * 3d));
        var kernel = new double[(reach * 2) + 1];
        var total = 0d;

        for (var tap = -reach; tap <= reach; tap++)
        {
            var weight = Math.Exp(-(tap * (double)tap) / (2d * sigma * sigma));
            kernel[tap + reach] = weight;
            total += weight;
        }

        for (var index = 0; index < kernel.Length; index++)
        {
            kernel[index] /= total;
        }

        return kernel;
    }
}
