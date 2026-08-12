namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// `18` §6's <c>stacking</c> block: <c>{"mode": "ADDITIVE", "maxStacks": 5, "refreshOnReapply": true}</c>.
/// </summary>
/// <remarks>
/// <see cref="MaxStacks"/> is <c>null</c> for uncapped — `05` §3.1's built-in <c>SYS_ENRAGE</c> is
/// <em>"multiplicative stacking, uncapped"</em>, so the absence has to mean something other than
/// one. M2-06 owns what each mode does with the count.
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
