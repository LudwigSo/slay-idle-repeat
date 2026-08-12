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
/// </remarks>
public sealed class ExportStep : IAssetStep
{
    /// <summary>The deviation id this step emits for the missing pngquant stage.</summary>
    public const string PngquantDeviationId = "DEV_PNGQUANT_UNAVAILABLE";

    /// <inheritdoc/>
    public int Number => 6;

    /// <inheritdoc/>
    public string Id => "export";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 6";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input) => throw new NotImplementedException();
}
