namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// `15` §B4 step 1: <em>"Background removal -&gt; true alpha, no halo (matte decontamination on)"</em>.
/// </summary>
/// <remarks>
/// <para>
/// A border-seeded flood fill against the sampled border colour, within
/// <see cref="ThresholdKeys.BackgroundKeyTolerance"/>, then matte decontamination — un-mixing the
/// background colour back out of the partially transparent edge pixels — at
/// <see cref="ThresholdKeys.MatteDecontaminationStrength"/>.
/// </para>
/// <para>
/// 🔒 Both thresholds are uncalibrated. `15` §A3 states the outcome ("fully transparent. No shadow
/// baked in.") and no number, so asking for either without a stated value throws
/// <see cref="UncalibratedThresholdException"/> rather than picking something that looks right.
/// </para>
/// </remarks>
public sealed class BackgroundRemovalStep : IAssetStep
{
    /// <inheritdoc/>
    public int Number => 1;

    /// <inheritdoc/>
    public string Id => "background-removal";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 1";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input) => throw new NotImplementedException();
}
