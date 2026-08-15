namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// Effect-id order, in one place: the ascending lexicographic order of effect ids, not draft order
/// — the mechanism that removes order-dependence between client and server.
/// </summary>
/// <remarks>
/// <para>
/// Ordinal, never culture-aware: culture-aware comparisons consult collation tables that differ by
/// locale and ICU version. Under <c>en-US</c>, <c>"PK_A"</c> sorts before <c>"PKA"</c> because the
/// underscore is a minor difference; ordinally it sorts after, since <c>'_'</c> is U+005F and
/// <c>'A'</c> is U+0041. A German phone and a Linux container would then fire the same two effects
/// in opposite orders.
/// </para>
/// <para>
/// It is one helper rather than a convention because this order is keyed on in several places
/// across the resolution pipeline — call sites that must agree, and each an independent chance to
/// write a culture-aware sort and be right only on the machine it was written on.
/// </para>
/// </remarks>
public static class EffectOrder
{
    /// <summary>The one comparer for effect ids.</summary>
    public static StringComparer IdComparer => StringComparer.Ordinal;

    /// <summary>The one comparer for effects, by <see cref="EffectDefinition.Id"/>.</summary>
    public static IComparer<EffectDefinition> ById { get; } = new ByIdComparer();

    /// <summary>The effects in ascending effect-id order.</summary>
    public static IEnumerable<EffectDefinition> InEffectIdOrder(this IEnumerable<EffectDefinition> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        return effects.OrderBy(e => e.Id, IdComparer);
    }

    /// <summary>The ids in ascending effect-id order.</summary>
    public static IEnumerable<string> InEffectIdOrder(this IEnumerable<string> effectIds)
    {
        ArgumentNullException.ThrowIfNull(effectIds);

        return effectIds.OrderBy(id => id, IdComparer);
    }

    private sealed class ByIdComparer : IComparer<EffectDefinition>
    {
        public int Compare(EffectDefinition? x, EffectDefinition? y)
        {
            // The null checks come first, before any reference-equality fast path: a
            // ReferenceEquals(x, y) shortcut would answer 0 for Compare(null, null), so the guard
            // would then reject a half-null collection while quietly accepting an all-null one.
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);

            // A null effect has no id to order by. Sorting it to one end would be an arbitrary
            // choice that reads as deliberate; the collection should not hold one.
            return ReferenceEquals(x, y) ? 0 : IdComparer.Compare(x.Id, y.Id);
        }
    }
}
