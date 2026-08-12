namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// `15` §B4 step 3: <em>"Palette quantise -&gt; to the biome palette + neutrals (biome assets
/// only)"</em>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Biome assets only.</b> A row with <see cref="AssetSpec.IsBiomeScoped"/> false comes back
/// <see cref="StepOutcome.SkippedNotApplicable"/> with the reason stated, and its bitmap must come
/// out <b>pixel-identical</b> to the one that went in. Getting that wrong silently recolours the
/// whole UI kit and the currency icons, none of which name a biome.
/// </para>
/// <para>
/// Where it does apply: a nearest-colour snap to the six `15` §A5 hues plus the §A3 outline colour
/// plus <see cref="ThresholdKeys.PaletteNeutrals"/>, within
/// <see cref="ThresholdKeys.PaletteMatchTolerance"/>. Both of those are uncalibrated — §A5 says
/// "+ neutrals" and never enumerates them — so both throw when unstated.
/// </para>
/// </remarks>
public sealed class PaletteQuantiseStep : IAssetStep
{
    /// <inheritdoc/>
    public int Number => 3;

    /// <inheritdoc/>
    public string Id => "palette-quantise";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 3";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input) => throw new NotImplementedException();
}
