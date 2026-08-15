namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// The <c>stacking</c> block: <c>{"mode": "ADDITIVE", "maxStacks": 5, "refreshOnReapply": true}</c>.
/// </summary>
/// <remarks>
/// <see cref="MaxStacks"/> is <c>null</c> for uncapped — a built-in status is multiplicative
/// stacking, uncapped, so the absence has to mean something other than one.
/// </remarks>
public sealed record EffectStacking
{
    /// <summary>How a second application combines with the first.</summary>
    public required StackingMode Mode { get; init; }

    /// <summary>The stack ceiling; <c>null</c> is uncapped.</summary>
    public int? MaxStacks { get; init; }

    /// <summary>Whether re-applying restarts the duration.</summary>
    public bool? RefreshOnReapply { get; init; }
}
