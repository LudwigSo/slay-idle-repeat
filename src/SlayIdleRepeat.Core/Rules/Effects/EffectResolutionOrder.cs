using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// One effect as `18` §8 <b>step 1</b> collected it: the effect, the source it came from, and its
/// index within that source.
/// </summary>
/// <param name="Effect">The authored effect.</param>
/// <param name="Source">Which of `18` §8 step 1's ten sources contributed it.</param>
/// <param name="IndexInSource">
/// Its position in <see cref="IEffectSource.Effects"/> — the second half of
/// <see cref="EffectResolutionOrder"/>'s tiebreak.
/// </param>
/// <remarks>
/// 🔒 <b>The provenance is carried, not discarded, and it is not a log label.</b> It is what makes
/// the resolution order <b>total</b>; see <see cref="EffectResolutionOrder"/> for the ruling and why
/// the alternative — rewriting ids to be instance-unique — was rejected.
/// </remarks>
internal readonly record struct CollectedEffect(
    EffectDefinition Effect, EffectSourceKind Source, int IndexInSource);

/// <summary>
/// 🔒 <b>`18` §8's effect-id order, made <em>total</em>.</b> Primary key: the ascending ordinal order
/// of effect ids, exactly as §8 states it. Tiebreak, for two effects sharing one id: `18` §8 step 1's
/// own source order, then the index within that source.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>THE RULING, and the hole it closes.</b> `18` §8 says <em>"effect-id order means the
/// ascending lexicographic order of effect IDs"</em> and says nothing about two effects with the
/// <b>same</b> id — which a <em>build</em> produces routinely, because the same authored affix rolled
/// on two gear slots is two collected effects carrying one authored id. A plain stable sort leaves
/// arrival order deciding between them, and that is observable: `18` §8 step 8 is <em>"apply
/// <c>STAT_SET</c> — <b>last writer wins</b>, in effect-id order"</em>, so two same-id
/// <c>STAT_SET</c>s at different values resolve to whichever the collector happened to visit second.
/// A client that walked its inventory in a different order from the server's would then compute a
/// different stat block with every `18` §8 step implemented exactly — the precise divergence §8
/// exists to remove. M2-07 found this, declined to invent an answer, and handed it here.
/// </para>
/// <para>
/// 🔒 <b>Why the tiebreak rather than instance-unique ids.</b> The other available rule — rewrite each
/// collected effect's id to something unique, <c>"AFF_KEEN#GEAR:3"</c> — was rejected on three
/// counts. It changes the <b>primary</b> key, so two effects the author wrote as one would no longer
/// sort adjacently and `18` §8's own sentence would no longer describe what runs. It leaks a
/// synthesised id into `05` §7's combat log and `05` §4.2's <em>"consumed in ascending effect-id
/// order"</em> charge list, where the id is the thing a replay matches on. And it would have to
/// encode the same provenance anyway, in a string, where it could not be compared without parsing.
/// The tiebreak keeps `18` §8's stated order as the primary key and adds a total, documented,
/// device-independent order <em>underneath</em> it.
/// </para>
/// <para>
/// 🔒 <b>Why this tiebreak is device-independent.</b> Both components are determined by the build and
/// by `18` §8 step 1's own sentence: the source order is
/// <see cref="EffectSourceCatalogue.Step1SourceList"/>, transcribed once in
/// <see cref="EffectSourceKind"/>'s declaration order and pinned against the document; the index is
/// the source's own list position, which <see cref="IEffectSource"/> obliges every implementation to
/// make a function of the build. Neither can differ between a phone and a container. `18` §8's
/// closing sentence is therefore true of the whole order and not only of its first component.
/// </para>
/// <para>
/// ⚠️ <b>Two same-id effects both apply. Neither is dropped and neither is rejected.</b> That is not
/// a concession: two copies of one affix are two bonuses, and de-duplicating them would silently
/// halve a legitimate build. What the ruling fixes is only <em>which order</em> they resolve in.
/// </para>
/// <para>
/// 🔒 <b>The handoff to <c>StatAggregation</c> relies on a stable sort, deliberately.</b> That method
/// re-sorts its whole input through <c>EffectOrder.InEffectIdOrder</c>, which is
/// <see cref="Enumerable.OrderBy{TSource, TKey}(IEnumerable{TSource}, Func{TSource, TKey}, IComparer{TKey})"/>
/// and is documented stable — so effects handed to it already in <em>this</em> order keep it among
/// equal ids, and `18` §8 steps 4-8 see the total order without <c>Rules.Stats</c> needing to know
/// this type exists (which R17 forbids anyway).
/// <c>EffectResolverTests.Two_effects_with_one_id_resolve_the_same_way_from_either_arrival_order</c>
/// is that claim run end-to-end through the real aggregation.
/// </para>
/// </remarks>
internal static class EffectResolutionOrder
{
    /// <summary>🔒 The one comparer for collected effects. Total.</summary>
    internal static IComparer<CollectedEffect> Comparer { get; } = new TotalOrder();

    /// <summary>The collected effects in `18` §8's resolution order.</summary>
    internal static IReadOnlyList<CollectedEffect> Sort(IEnumerable<CollectedEffect> collected)
    {
        ArgumentNullException.ThrowIfNull(collected);

        var ordered = collected.ToArray();

        // 🔒 Array.Sort with a TOTAL comparer, not OrderBy. OrderBy's stability would hide a
        //    tiebreak that had stopped working — the pairs it is meant to separate would keep their
        //    arrival order and look correct for whichever arrival order the test happened to use.
        //    Array.Sort is unstable, so a comparer that returned 0 for two distinct effects would
        //    show up as a flapping order rather than as a passing test.
        Array.Sort(ordered, Comparer);

        return ordered;
    }

    /// <summary>
    /// 🔒 Compares two collected effects: id, then `18` §8 step 1 source, then index within source.
    /// </summary>
    internal static int Compare(CollectedEffect x, CollectedEffect y)
    {
        ArgumentNullException.ThrowIfNull(x.Effect, nameof(x));
        ArgumentNullException.ThrowIfNull(y.Effect, nameof(y));

        // 🔒 `18` §8's stated key, through the one comparer M2-01 declared for it. Ordinal, never
        //    culture-aware — see EffectOrder for what a bare OrderBy(x => x.Id) costs.
        var byId = EffectOrder.IdComparer.Compare(x.Effect.Id, y.Effect.Id);

        if (byId != 0)
        {
            return byId;
        }

        // ── Tiebreak 1 · `18` §8 step 1's source order. See the type remarks for the ruling.
        var bySource = ((int)x.Source).CompareTo((int)y.Source);

        return bySource != 0 ? bySource : x.IndexInSource.CompareTo(y.IndexInSource);
    }

    private sealed class TotalOrder : IComparer<CollectedEffect>
    {
        public int Compare(CollectedEffect x, CollectedEffect y) => EffectResolutionOrder.Compare(x, y);
    }
}
