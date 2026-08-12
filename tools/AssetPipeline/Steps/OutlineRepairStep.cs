namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// `15` §B4 step 4: <em>"Outline repair -&gt; ensure the outline is continuous and uniform
/// width"</em>.
/// </summary>
/// <remarks>
/// <para>
/// Builds the outline mask (within <see cref="ThresholdKeys.OutlineColourTolerance"/> of
/// <see cref="Doc15Authorised.OutlineColourHex"/>), closes gaps up to
/// <see cref="ThresholdKeys.OutlineGapClosureRadius"/> morphologically, and <b>measures</b> the
/// resulting width as a <see cref="StepMeasurement"/>.
/// </para>
/// <para>
/// 🔒 This step repairs <em>continuity</em>. <em>Width conformance</em> against `15` §A3's 3-4 px
/// band is QA item 3's job, and the two are kept apart on purpose: a step that both changed the
/// width and judged it would be marking its own homework.
/// </para>
/// </remarks>
public sealed class OutlineRepairStep : IAssetStep
{
    /// <inheritdoc/>
    public int Number => 4;

    /// <inheritdoc/>
    public string Id => "outline-repair";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 4";

    /// <summary>The measurement key this step records the measured outline width under.</summary>
    public const string OutlineWidthMeasurement = "outlineWidthPx";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input) => throw new NotImplementedException();
}
