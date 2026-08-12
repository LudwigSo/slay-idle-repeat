using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// 🔒 `18` §8 step 1's ten sources as one build hands them over: at most one
/// <see cref="IEffectSource"/> per <see cref="EffectSourceKind"/>, and nothing for the kinds this
/// build has none of.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Ten slots, always — not "the sources somebody remembered to pass".</b>
/// <see cref="Collect"/> walks <see cref="EffectSourceCatalogue.Rows"/>, which is `18` §8 step 1's
/// own sentence, and asks each of the ten in turn. A kind with no source contributes nothing. That is
/// the difference between a collector that <em>implements step 1</em> and one that happens to
/// enumerate whatever list it was given: the second silently drops a source the day a caller forgets
/// one, and step 1 is the step whose whole content is <em>which</em> sources.
/// </para>
/// <para>
/// ⚠️ <b>None of the ten can be supplied from a real build today</b>, because no data model exists — see
/// <see cref="EffectSourceCatalogue"/> for the milestone that lands each and the
/// <c>SubjectSetFloorTests.Pending</c> entry that fires when it does. An empty set is therefore the
/// normal state in M2, and <see cref="Collect"/> returning nothing from an unfilled slot is the correct
/// answer rather than a gap.
/// </para>
/// <para>
/// 🔒 <b>A duplicate kind is refused.</b> Two sources both claiming <c>GEAR</c> would have no defined
/// order between them, and <see cref="EffectResolutionOrder"/>'s tiebreak is
/// <c>(source, index-within-source)</c> — which is total only because a kind identifies exactly one
/// list. Accepting both and concatenating them would put arrival order back underneath the tiebreak,
/// one level down, where it would be much harder to find. A build with two gear slots contributes one
/// <c>GEAR</c> source listing both slots' effects, in the slot order `08` §2 fixes.
/// </para>
/// </remarks>
internal sealed class EffectSourceSet
{
    private readonly Dictionary<EffectSourceKind, IEffectSource> _sources;

    private EffectSourceSet(Dictionary<EffectSourceKind, IEffectSource> sources) => _sources = sources;

    /// <summary>The empty build: ten declared sources, none of which contributes anything.</summary>
    internal static EffectSourceSet Empty { get; } = new(new Dictionary<EffectSourceKind, IEffectSource>());

    /// <summary>A build's sources. At most one per `18` §8 step 1 kind.</summary>
    /// <exception cref="ArgumentException">
    /// Two sources claim the same kind, or an element is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A source's kind is outside `18` §8 step 1's ten.
    /// </exception>
    internal static EffectSourceSet Of(params IEffectSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var byKind = new Dictionary<EffectSourceKind, IEffectSource>();

        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(sources));

            // 🔒 Refused HERE, not left to Collect(). Collect walks the catalogue, so a source
            //    carrying a kind outside `18` §8 step 1's ten would be stored, never visited, and
            //    contribute nothing — silently, which is the exact failure this type's remarks say
            //    it exists to prevent. ListEffectSource validates in its own constructor, but the
            //    interface is the extension point for ten implementations by seven milestones and
            //    none of them is obliged to.
            _ = EffectSourceCatalogue.RowFor(source.Kind);

            if (!byKind.TryAdd(source.Kind, source))
            {
                throw new ArgumentException(
                    $"two sources claim 18 §8 step 1's '{source.Kind}' slot. The resolution order's " +
                    "tiebreak for two effects sharing one id is (source, index within source), which " +
                    "is total only because a kind names exactly one list — see EffectResolutionOrder. " +
                    "A build with several gear slots contributes ONE GEAR source listing them in slot " +
                    "order, not one source per slot.",
                    nameof(sources));
            }
        }

        return new EffectSourceSet(byKind);
    }

    /// <summary>The source for one of the ten kinds, or <c>null</c> where this build has none.</summary>
    internal IEffectSource? For(EffectSourceKind kind) =>
        _sources.TryGetValue(kind, out var source) ? source : null;

    /// <summary>
    /// 🔒 `18` §8 <b>step 1</b> — <em>"collect all active effects from: gear → affixes → set bonuses →
    /// talents → pet auras → mount → run buffs → shrine buffs → curses → perks (in draft order)"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returned in `18` §8 step 1's <b>collection</b> order — the order of the sentence — and
    /// <em>not</em> sorted. R5: collection order is which effects to gather; application order is
    /// effect-id order, and <see cref="EffectResolver"/> applies it. Keeping the two apart here is
    /// what lets <c>EffectResolverTests</c> show that the arrival order genuinely cannot reach the
    /// arithmetic, by handing the same build in two collection orders.
    /// </para>
    /// <para>
    /// ⚠️ An effect that appears in two <em>different</em> sources is collected twice, once per
    /// source. That is not de-duplication's job and it is not a bug: two sources contributing one
    /// authored id are two applications of it (the same affix on two gear slots is two bonuses), and
    /// which order they resolve in is <see cref="EffectResolutionOrder"/>'s ruling.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<CollectedEffect> Collect()
    {
        var collected = new List<CollectedEffect>();

        // 🔒 Over the CATALOGUE, not over _sources. The catalogue is `18` §8 step 1's ten in its own
        //    order; iterating the dictionary would be both incomplete (most slots absent today) and
        //    hash-ordered, which is exactly the device-dependence §8 exists to remove.
        foreach (var row in EffectSourceCatalogue.Rows)
        {
            if (For(row.Kind) is not { } source)
            {
                continue;
            }

            var effects = source.Effects ?? throw new InvalidOperationException(
                $"the {row.Kind} source reports a null effect list. 18 §8 step 1 collects effects from " +
                "ten sources; a source with nothing to contribute reports an EMPTY list, because null " +
                "and empty are the same answer here and only one of them is greppable.");

            for (var i = 0; i < effects.Count; i++)
            {
                var position = i.ToString(System.Globalization.CultureInfo.InvariantCulture);

                var effect = effects[i].Effect ?? throw new InvalidOperationException(
                    $"element {position} of the {row.Kind} source has a null effect. The resolution " +
                    "order is stated over effect ids and a hole has none.");

                // 🔒 Checked here as well as in ListEffectSource's constructor: IEffectSource is the
                //    extension point ten later implementations satisfy, and an unnamed holding makes
                //    every instance share one `18` §3 counter — silently, in the direction that looks
                //    like it works.
                if (!effects[i].Instance.NamesAHolding)
                {
                    throw new InvalidOperationException(
                        $"element {position} of the {row.Kind} source ('{effect.Id}') names no holding. " +
                        "EffectInstanceId is the effects layer's one instance identity and a 18 §8 " +
                        "step 1 source is the layer that knows one — see SourcedEffect.");
                }

                collected.Add(new CollectedEffect(effect, effects[i].Instance, row.Kind, i));
            }
        }

        return collected;
    }
}
