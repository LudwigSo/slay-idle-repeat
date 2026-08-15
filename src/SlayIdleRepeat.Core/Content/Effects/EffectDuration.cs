namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// The <c>duration</c> block: <c>{"seconds": 4.0, "scope": "BATTLE"}</c>, optionally with an early
/// terminator.
/// </summary>
/// <remarks>
/// <see cref="Seconds"/> is optional: some effects write only a scope, with no timer at all, and
/// the scope alone ends them. When both a timer and an <see cref="Until"/> terminator are present,
/// whichever fires first ends the effect; neither overrides the other.
/// </remarks>
public sealed record EffectDuration
{
    /// <summary>Which boundary ends the effect.</summary>
    public required DurationScope Scope { get; init; }

    /// <summary>Seconds of battle time, when the effect also carries a timer.</summary>
    public double? Seconds { get; init; }

    /// <summary>An early terminator that ends the effect before its timer or scope would.</summary>
    public DurationTerminator? Until { get; init; }
}
