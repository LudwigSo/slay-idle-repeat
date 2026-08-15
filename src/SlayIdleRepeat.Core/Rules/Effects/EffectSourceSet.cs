using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The ten sources as one build hands them over: at most one <see cref="IEffectSource"/> per
/// <see cref="EffectSourceKind"/>, and nothing for the kinds this build has none of.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Collect"/> walks all ten catalogue rows and asks each in turn, so a kind with no
/// source contributes nothing rather than being silently dropped by an incomplete caller.
/// </para>
/// <para>None of the ten can be supplied from a real build today, since no data model exists yet — an empty set is the normal state.</para>
/// <para>
/// A duplicate kind is refused: two sources both claiming <c>GEAR</c> would have no defined order
/// between them, since <see cref="EffectResolutionOrder"/>'s tiebreak assumes a kind names exactly
/// one list. A build with two gear slots contributes one <c>GEAR</c> source listing both slots'
/// effects, not one source per slot.
/// </para>
/// </remarks>
internal sealed class EffectSourceSet
{
    private readonly Dictionary<EffectSourceKind, IEffectSource> _sources;

    private EffectSourceSet(Dictionary<EffectSourceKind, IEffectSource> sources) => _sources = sources;

    /// <summary>The empty build: ten declared sources, none of which contributes anything.</summary>
    internal static EffectSourceSet Empty { get; } = new(new Dictionary<EffectSourceKind, IEffectSource>());

    /// <summary>A build's sources. At most one per source kind.</summary>
    /// <exception cref="ArgumentException">
    /// Two sources claim the same kind, or an element is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A source's kind is outside the declared ten.</exception>
    internal static EffectSourceSet Of(params IEffectSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var byKind = new Dictionary<EffectSourceKind, IEffectSource>();

        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(sources));

            // Validated here rather than left to Collect(), which only walks the catalogue and would
            // never visit — and never flag — a source carrying an out-of-range kind.
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

    /// <summary>Collects all active effects from the build's sources.</summary>
    /// <remarks>
    /// Returned in collection order — not sorted. Collection order is kept apart from application
    /// (effect-id) order, which <see cref="EffectResolver"/> applies, so that arrival order can never
    /// leak into the arithmetic. An effect appearing in two different sources is collected twice, once
    /// per source — that is not a bug, since two sources contributing one id are two applications of
    /// it (the same affix on two gear slots is two bonuses).
    /// </remarks>
    internal IReadOnlyList<CollectedEffect> Collect()
    {
        var collected = new List<CollectedEffect>();

        // Walk the catalogue, not the dictionary directly: iterating _sources would be both
        // incomplete (most slots are absent today) and hash-ordered.
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

                // Checked here too, not only in ListEffectSource's constructor, since IEffectSource is
                // an extension point other implementations aren't obliged to validate themselves.
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
