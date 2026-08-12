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
    /// <inheritdoc/>
    public int Number => 2;

    /// <inheritdoc/>
    public string Id => "trim-to-canvas";

    /// <inheritdoc/>
    public string DocReference => "15 §B4 step 2";

    /// <inheritdoc/>
    public AssetStepResult Run(AssetStepInput input) => throw new NotImplementedException();
}
