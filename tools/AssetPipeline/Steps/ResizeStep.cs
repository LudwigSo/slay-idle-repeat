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

    /// <inheritdoc/>
    public int Number => 5;

    /// <inheritdoc/>
    public string Id => "resize";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 5";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input) => throw new NotImplementedException();

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
    public SKBitmap Resample(SKBitmap source, PixelSize target) => throw new NotImplementedException();

    /// <summary>
    /// The second half on its own: an unsharp mask over RGB, leaving every alpha byte untouched.
    /// </summary>
    /// <param name="source">The resampled image.</param>
    /// <param name="amount">`15` §B4's 0.4.</param>
    /// <param name="radius">The uncalibrated radius the caller has stated.</param>
    public SKBitmap Sharpen(SKBitmap source, double amount, double radius) =>
        throw new NotImplementedException();
}
