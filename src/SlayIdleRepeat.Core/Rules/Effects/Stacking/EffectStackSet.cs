using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Stacking;

/// <summary>🔒 `18` §6 — the applications of one effect on one owner, combined by its stacking mode.</summary>
internal sealed record EffectStackSet
{
    /// <summary>The effect's `18` §8 id — named in every failure (S2).</summary>
    internal required string EffectId { get; init; }

    /// <summary>`18` §6's stacking block.</summary>
    internal required EffectStacking Stacking { get; init; }

    /// <summary>The applications currently held, in application order.</summary>
    internal IReadOnlyList<double> Applications { get; init; } = [];

    /// <summary>How many stacks are held.</summary>
    internal int Count => Applications.Count;

    /// <summary>The applications combined per `18` §6's mode.</summary>
    internal double CombinedValue => throw new NotImplementedException();

    /// <summary>An empty set for one effect and one stacking block.</summary>
    internal static EffectStackSet Empty(string effectId, EffectStacking stacking) =>
        throw new NotImplementedException();

    /// <summary>Applies one more application of the effect.</summary>
    internal StackApplication Apply(double value) => throw new NotImplementedException();
}

/// <summary>What one application of an effect did to its stack set and to its duration.</summary>
/// <param name="Stacks">The set after the application.</param>
/// <param name="StackAdded">Whether the application became a stack.</param>
/// <param name="RefreshDuration">Whether `18` §6's <c>refreshOnReapply</c> asks for a refresh.</param>
internal readonly record struct StackApplication(
    EffectStackSet Stacks, bool StackAdded, bool RefreshDuration);
