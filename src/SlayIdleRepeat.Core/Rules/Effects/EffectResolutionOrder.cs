using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>One effect as collected: the effect, the source it came from, and its index within that source.</summary>
/// <param name="Effect">The authored effect.</param>
/// <param name="Instance">
/// The holding it came from, carried through so a resolution-pass consumer (e.g.
/// <c>TriggerRegistry</c>) has the identity per-instance counters need, without deriving one.
/// </param>
/// <param name="Source">Which collection source contributed it.</param>
/// <param name="IndexInSource">Its position within that source — the tiebreak's second half.</param>
/// <remarks>
/// Two keys answering different questions: <see cref="Instance"/> is identity (which holding this
/// is, stable across battles); <see cref="Source"/> plus <see cref="IndexInSource"/> is ordering
/// (valid for one resolution pass, renumbered on every build change). Conflating them would either
/// restart a run-scoped counter on every re-equip, or put an authored id into a stable sort key.
/// </remarks>
internal readonly record struct CollectedEffect(
    EffectDefinition Effect, EffectInstanceId Instance, EffectSourceKind Source, int IndexInSource);

/// <summary>
/// Effect-id order, made total. Primary key: ascending ordinal order of effect ids. Tiebreak, for two
/// effects sharing one id: collection source order, then index within that source.
/// </summary>
/// <remarks>
/// <para>
/// A build can collect two effects sharing one id (the same affix rolled on two gear slots), and a
/// plain stable sort would leave arrival order deciding between them — observable anywhere
/// last-writer-wins applies (e.g. <c>STAT_SET</c>), where two same-id ops at different values would
/// resolve differently depending on collection order. This comparer makes the order a property of
/// the data instead: both tiebreak components are pinned by the build (source declaration order,
/// then the source's own list position), so two clients with the same build always agree.
/// </para>
/// <para>Two same-id effects both apply — neither is dropped. Only the order between them is fixed.</para>
/// <para>
/// <c>StatAggregation</c> relies on this order surviving a stable downstream sort, so effects handed
/// to it in this order keep it among equal ids.
/// </para>
/// </remarks>
internal static class EffectResolutionOrder
{
    /// <summary>The one comparer for collected effects. Total.</summary>
    private static IComparer<CollectedEffect> Comparer { get; } = new TotalOrder();

    /// <summary>The collected effects in resolution order.</summary>
    internal static IReadOnlyList<CollectedEffect> Sort(IEnumerable<CollectedEffect> collected)
    {
        ArgumentNullException.ThrowIfNull(collected);

        var ordered = collected.ToArray();

        // Array.Sort with a total comparer, not OrderBy: the order must be a property of the data,
        // not of the sort algorithm's incidental stability at small sizes (introsort runs a stable
        // insertion sort below 16 elements, which can mask a missing tiebreak in a small test).
        Array.Sort(ordered, Comparer);

        return ordered;
    }

    /// <summary>Compares two collected effects: id, then collection source, then index within source.</summary>
    internal static int Compare(CollectedEffect x, CollectedEffect y)
    {
        ArgumentNullException.ThrowIfNull(x.Effect, nameof(x));
        ArgumentNullException.ThrowIfNull(y.Effect, nameof(y));

        var byId = EffectOrder.IdComparer.Compare(x.Effect.Id, y.Effect.Id);

        if (byId != 0)
        {
            return byId;
        }

        var bySource = ((int)x.Source).CompareTo((int)y.Source);

        return bySource != 0 ? bySource : x.IndexInSource.CompareTo(y.IndexInSource);
    }

    private sealed class TotalOrder : IComparer<CollectedEffect>
    {
        public int Compare(CollectedEffect x, CollectedEffect y) => EffectResolutionOrder.Compare(x, y);
    }
}
