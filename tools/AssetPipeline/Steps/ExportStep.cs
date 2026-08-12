using SkiaSharp;

namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// `15` §B4 step 6: <em>"Export -&gt; PNG-32, then compress with pngquant (quality 80-95)"</em>.
/// </summary>
/// <remarks>
/// <para>
/// The authorised, lossless half always runs: encode PNG-32 with straight (unpremultiplied) alpha,
/// which is `15` §C's delivery format and which round-trips bit-for-bit through Skia.
/// </para>
/// <para>
/// 🔒 <b>There is no pngquant and no PNG-8 palette writer here.</b> pngquant is a native binary and
/// <c>SKPngEncoderOptions</c> controls only the zlib level and the row filter, both lossless. The
/// managed stand-in is a median-cut to <see cref="ThresholdKeys.ExportColourBudget"/> colours,
/// accepted only if the measured mean per-channel error is within
/// <see cref="ThresholdKeys.ExportMaxMeanError"/>. Both are uncalibrated, so when they are absent
/// the step emits <see cref="PngquantDeviationId"/> stating that the compression half did not run
/// and why — rather than skipping it silently and reporting a clean pass.
/// </para>
/// <para>
/// 🔒 The deviation is emitted on <b>both</b> paths, because pngquant runs on neither. Its text
/// distinguishes them: on the uncalibrated path it names the two holes that stopped the compression
/// stage; on the calibrated one it says the colours were reduced by a managed substitute that does
/// not reproduce pngquant's perceptual quality metric and still writes a PNG-32 container.
/// </para>
/// </remarks>
public sealed class ExportStep : IAssetStep
{
    /// <summary>The deviation id this step emits for the missing pngquant stage.</summary>
    public const string PngquantDeviationId = "DEV_PNGQUANT_UNAVAILABLE";

    /// <summary>The measurement key for the encoded file's size.</summary>
    public const string EncodedBytesMeasurement = "encodedBytes";

    /// <summary>The measurement key for how many distinct colours the encoded image holds.</summary>
    public const string DistinctColourMeasurement = "distinctColours";

    /// <summary>The measurement key for the median cut's mean per-channel error.</summary>
    public const string MeanColourErrorMeasurement = "meanColourError";

    /// <summary>What `15` §B4 step 6 asks for, quoted in every deviation this step emits.</summary>
    private const string Requirement = "Export to PNG-32, then compress with pngquant (quality 80-95).";

    /// <inheritdoc/>
    public int Number => 6;

    /// <inheritdoc/>
    public string Id => "export";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 6";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var calibrated = input.Thresholds.IsCalibrated(ThresholdKeys.ExportColourBudget)
                         && input.Thresholds.IsCalibrated(ThresholdKeys.ExportMaxMeanError);

        var image = Raster.From(input.Image);
        var measurements = new List<StepMeasurement>();
        var reduced = false;

        if (calibrated)
        {
            var budget = (int)Math.Round(
                input.Thresholds.RequireNumber(ThresholdKeys.ExportColourBudget),
                MidpointRounding.AwayFromZero);
            var maxError = input.Thresholds.RequireNumber(ThresholdKeys.ExportMaxMeanError);

            var candidate = MedianCut.Reduce(image, budget);
            measurements.Add(new StepMeasurement(
                MeanColourErrorMeasurement, candidate.MeanError, "channelUnits", DocReference));

            if (candidate.MeanError <= maxError)
            {
                image = candidate.Image;
                reduced = true;
            }
        }

        var encoded = EncodePng32(image);
        measurements.Add(
            new StepMeasurement(EncodedBytesMeasurement, encoded.Length, "bytes", "15 §C"));
        measurements.Add(new StepMeasurement(
            DistinctColourMeasurement, MedianCut.DistinctColourCount(image), "count", DocReference));

        var deviation = calibrated ? ReducedByMedianCut(reduced) : CompressionDidNotRun();

        return new AssetStepResult(
            Number,
            Id,
            StepOutcome.Applied,
            image.ToBitmap(),
            deviation.Taken,
            measurements,
            [deviation],
            encoded);
    }

    /// <summary>
    /// Encodes PNG-32 with straight alpha — `15` §C's delivery format, and the half of §B4 step 6
    /// the doc authorises outright.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    private static byte[] EncodePng32(Raster image)
    {
        using var bitmap = image.ToBitmap();

        // 🔒 SKBitmap.Encode, not SKImage.FromBitmap(...).Encode: the bitmap carries
        // SKAlphaType.Unpremul and encoding it directly writes those bytes as they stand, which is
        // what makes the round trip bit-exact for a semi-transparent pixel.
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException(
                $"Skia encoded no PNG for a {image.Width}×{image.Height} image.");

        return data.ToArray();
    }

    /// <summary>The deviation for a run in which the two export holes are still null.</summary>
    private static DeclaredDeviation CompressionDidNotRun() => new(
        PngquantDeviationId,
        "15 §B4 step 6",
        Requirement,
        "Only the authorised, lossless half ran: the image was encoded PNG-32 with straight alpha " +
        "and no colour reduction was attempted.",
        "pngquant is a native binary and this toolchain shells out to none; SKPngEncoderOptions " +
        "controls only the zlib level and the row filter, both lossless, and SkiaSharp cannot write " +
        $"a PNG-8 palette at all. The managed substitute is a median cut, and both numbers it needs " +
        $"— '{ThresholdKeys.ExportColourBudget}' and '{ThresholdKeys.ExportMaxMeanError}' — are " +
        "uncalibrated, so the compression stage did not run. `15` authorises neither number, and " +
        "steering rule S6 forbids inventing one.");

    /// <summary>The deviation for a run in which the managed substitute did run.</summary>
    /// <param name="accepted">Whether the reduced image was within the stated error floor.</param>
    private static DeclaredDeviation ReducedByMedianCut(bool accepted) => new(
        PngquantDeviationId,
        "15 §B4 step 6",
        Requirement,
        accepted
            ? "The image was encoded PNG-32 with straight alpha after a managed median-cut colour " +
              "reduction to the stated budget, which came in within the stated mean-error floor."
            : "The image was encoded PNG-32 with straight alpha. A managed median-cut colour " +
              "reduction was attempted and rejected: its mean per-channel error exceeded the " +
              "stated floor, so the unreduced pixels were kept.",
        "pngquant is a native binary and this toolchain shells out to none, so a median cut stands " +
        "in for it. The substitution is not equivalent in two ways. pngquant's 'quality 80-95' is " +
        "its own perceptual metric over its own remapping and dithering, which a mean per-channel " +
        "error does not reproduce — the two numbers are not comparable and a run that passes here " +
        "says nothing about what pngquant would have scored. And the reduced pixels still land in " +
        "a PNG-32 container, because SkiaSharp cannot write a PNG-8 palette, so the file-size win " +
        "is only what zlib gains from fewer distinct colours rather than the four-to-one a real " +
        "palette would give.");
}
