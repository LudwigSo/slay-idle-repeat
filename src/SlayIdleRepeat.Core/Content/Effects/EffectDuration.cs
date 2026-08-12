namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// `18` §6's <c>duration</c> block: <c>{"seconds": 4.0, "scope": "BATTLE"}</c>, optionally with an
/// early terminator.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Seconds"/> is optional because §7.10's Bog Air writes <c>{"scope": "PHASE"}</c> with
/// no timer at all — the scope alone ends it.
/// </para>
/// <para>
/// 🔒 <em>"<c>until</c> fields fire whichever comes first, terminator or timer."</em> Both may be
/// present (Ossify's <c>{"seconds": 6.0, "scope": "BATTLE", "until": "WARD_BROKEN"}</c>), and
/// neither overrides the other.
/// </para>
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
