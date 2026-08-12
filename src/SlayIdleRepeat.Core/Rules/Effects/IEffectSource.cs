using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// 🔒 One of `18` §8 step 1's ten sources, as narrow as the step allows: <b>which
/// <see cref="EffectSourceKind"/> this is, and the effects it contributes.</b>
/// </summary>
/// <remarks>
/// <para>
/// `18` §8 step 1 is <em>"collect all active effects from: gear → affixes → … → perks"</em>. What a
/// source contributes is a list of effects; how it knows them — an equipped item's affix rolls, a
/// talent's rank, the perks drafted this run — is entirely that milestone's business and is
/// deliberately not expressible here.
/// </para>
/// <para>
/// 🔒 <b>Bound to its subject before it arrives, not asked about one.</b> There is no
/// <c>Collect(Player, Run, ContentSnapshot)</c>, and that is the whole design: <c>Player</c> is
/// M1-04 and <c>Run</c> is M1-05, neither exists on this branch, and naming them would be steering
/// S6's <em>"filling a hole with a plausible value"</em> nine times over. An implementation is
/// constructed by the layer that holds the build and simply reports what it holds — which is also
/// what keeps <c>Rules.Effects</c> at the bottom of R17's layering, since a signature naming an
/// aggregate would drag <c>Model</c> into it.
/// </para>
/// <para>
/// 🔒 <b>THE ORDERING OBLIGATION — the one thing an implementation can get wrong invisibly.</b>
/// <see cref="Effects"/> must be in an order that is a <b>function of the build</b>, identical on
/// client and server, and not of hash iteration, dictionary enumeration or object identity. `18` §8
/// exists to remove <em>"the last source of order-dependence between client and server"</em>, and
/// while <see cref="EffectResolutionOrder"/> re-sorts everything ordinally by effect id, its
/// <b>tiebreak</b> for two effects sharing one id is this list's index. A source that enumerated a
/// <c>HashSet</c> would put a device-dependent order back exactly there. <c>EffectSourceContract</c>
/// asserts a stable repeat read; only the implementation can make it deterministic <em>across
/// devices</em>, and that is why the obligation is written out rather than left to the suite.
/// </para>
/// <para>
/// ⚠️ <b>"Active" in step 1 does not mean "condition satisfied".</b> That is step 2, and it belongs
/// to <see cref="EffectResolver"/> — `18` §4's conditions are <em>"pure functions of current
/// state"</em> re-evaluated at every resolution pass, so a source that pre-filtered by condition
/// would be caching an answer that is wrong on the next tick. A source reports what the build
/// <em>holds</em>: an unequipped item contributes nothing, a held perk contributes its clauses
/// whatever their conditions say.
/// </para>
/// </remarks>
internal interface IEffectSource
{
    /// <summary>Which of `18` §8 step 1's ten sources this is.</summary>
    EffectSourceKind Kind { get; }

    /// <summary>
    /// The effects this source contributes, in a stable, build-determined order. Never <c>null</c>,
    /// and never containing a <c>null</c>.
    /// </summary>
    /// <remarks>See the type remarks for the ordering obligation, which is the load-bearing half.</remarks>
    IReadOnlyList<EffectDefinition> Effects { get; }
}

/// <summary>
/// The in-<c>Core</c> <see cref="IEffectSource"/>: a source that contributes exactly the effects it
/// was given.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Steering S7 — <em>"add the <c>InMemory</c> fake <b>and</b> the shared contract suite in the
/// same change as the port"</em>. This is the fake; <c>EffectSourceContract</c> is the suite, and it
/// runs this and every later implementation through the same rules.
/// </para>
/// <para>
/// ⚠️ It is not a placeholder for the nine absent sources and must not become one. It carries no
/// notion of gear, of a perk or of a draft; it is the degenerate implementation that lets `18` §8
/// steps 1 and 2 be built, tested and frozen now, and it is what M2-16a's balance harness (`05` §9)
/// hands synthetic builds through, since that harness has no <c>Player</c> either.
/// </para>
/// <para>
/// 🔒 The list is <b>copied</b> on construction. A source is a reading of the build at one resolution
/// pass; a caller that kept a handle on the list it passed in could otherwise mutate the collected
/// set between step 1 and step 2 of the same pass, which is precisely the order-dependence §8
/// removes.
/// </para>
/// </remarks>
internal sealed class ListEffectSource : IEffectSource
{
    private readonly EffectDefinition[] _effects;

    /// <summary>A source of the given kind, contributing the given effects in the given order.</summary>
    /// <exception cref="ArgumentException">An element is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The kind is outside `18` §8 step 1's ten.</exception>
    internal ListEffectSource(EffectSourceKind kind, IEnumerable<EffectDefinition> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        // 🔒 Refused here rather than at the resolver: a kind outside the ten has no position in
        //    `18` §8 step 1's order, so EffectResolutionOrder's tiebreak would be undefined for it.
        _ = EffectSourceCatalogue.RowFor(kind);

        Kind = kind;
        _effects = effects.ToArray();

        for (var i = 0; i < _effects.Length; i++)
        {
            if (_effects[i] is null)
            {
                throw new ArgumentException(
                    $"element {i.ToString(System.Globalization.CultureInfo.InvariantCulture)} of the " +
                    $"{kind} source is null. 18 §8 step 1 collects effects; a hole in the list would " +
                    "reach step 2 as an effect with no id and no op, and the resolution order is " +
                    "stated over ids.",
                    nameof(effects));
            }
        }
    }

    /// <inheritdoc />
    public EffectSourceKind Kind { get; }

    /// <inheritdoc />
    public IReadOnlyList<EffectDefinition> Effects => _effects;
}
