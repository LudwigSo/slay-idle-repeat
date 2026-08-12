namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// 🔒 <b>Effect-id order</b>, in one place — `18` §8: <em>"the ascending lexicographic order of
/// effect IDs, not draft order. This removes the last source of order-dependence between client and
/// server."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Ordinal, never culture-aware.</b> <see cref="StringComparer.Ordinal"/> compares UTF-16 code
/// units; <see cref="StringComparer.CurrentCulture"/>, <c>string.CompareTo</c> and a bare
/// <c>OrderBy(x =&gt; x.Id)</c> (which resolves to <c>Comparer&lt;string&gt;.Default</c>, and so to
/// <c>CompareTo</c>) all consult collation tables that differ by locale and by ICU version. Under
/// <c>en-US</c>, <c>"PK_A"</c> sorts <em>before</em> <c>"PKA"</c> because the underscore is treated
/// as a minor difference; ordinally it sorts <em>after</em>, because <c>'_'</c> is U+005F and
/// <c>'A'</c> is U+0041. A German phone and a Linux container would then fire the same two effects
/// in opposite orders — the precise divergence `18` §8 and `14` §8.2 exist to prevent.
/// </para>
/// <para>
/// It is one helper rather than a convention because `05` §3.1 keys on this order in five places
/// (the pre-tick <c>ON_BATTLE_START</c> sweep, status expiry, <c>PERIODIC</c> firing, on-hit trigger
/// cascades and death resolution), `05` §4 step 6 in the <c>DAMAGE_TAKEN_MULT</c> product, `05`
/// §4.2 in <c>ATTACK_MULT_NEXT</c> consumption, and `18` §8 in steps 6, 7 and 8. Nine call sites
/// that must agree, spread over five milestone tasks, is nine chances to write
/// <c>OrderBy(e =&gt; e.Id)</c> and be right on the machine it was written on.
/// </para>
/// </remarks>
public static class EffectOrder
{
    /// <summary>🔒 The one comparer for effect ids.</summary>
    public static StringComparer IdComparer => StringComparer.Ordinal;

    /// <summary>🔒 The one comparer for effects, by <see cref="EffectDefinition.Id"/>.</summary>
    public static IComparer<EffectDefinition> ById { get; } = new ByIdComparer();

    /// <summary>The effects in ascending effect-id order (`18` §8).</summary>
    public static IEnumerable<EffectDefinition> InEffectIdOrder(this IEnumerable<EffectDefinition> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        return effects.OrderBy(e => e.Id, IdComparer);
    }

    /// <summary>The ids in ascending effect-id order (`18` §8).</summary>
    public static IEnumerable<string> InEffectIdOrder(this IEnumerable<string> effectIds)
    {
        ArgumentNullException.ThrowIfNull(effectIds);

        return effectIds.OrderBy(id => id, IdComparer);
    }

    private sealed class ByIdComparer : IComparer<EffectDefinition>
    {
        public int Compare(EffectDefinition? x, EffectDefinition? y)
        {
            // 🔒 The null checks come FIRST, before any reference-equality fast path. A
            // `ReferenceEquals(x, y)` shortcut would answer 0 for `Compare(null, null)` — the one
            // pair where the comment below is most true — and the guard would then reject a
            // half-null collection while quietly accepting an all-null one.
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);

            // A null effect has no id to order by. Sorting it to one end would be an arbitrary
            // choice that reads as deliberate; the collection should not hold one.
            return ReferenceEquals(x, y) ? 0 : IdComparer.Compare(x.Id, y.Id);
        }
    }
}
