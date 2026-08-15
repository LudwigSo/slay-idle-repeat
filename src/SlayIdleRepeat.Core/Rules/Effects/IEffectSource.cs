using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// One of the ten collection sources, as narrow as the step allows: which
/// <see cref="EffectSourceKind"/> this is, and the effects it contributes.
/// </summary>
/// <remarks>
/// <para>
/// What a source contributes is a list of effects; how it knows them — an equipped item's affix
/// rolls, a talent's rank, the perks drafted this run — is entirely that milestone's business and is
/// deliberately not expressible here. There is no <c>Collect(Player, Run, ContentSnapshot)</c>: an
/// implementation is constructed by the layer that holds the build and simply reports what it holds,
/// which also keeps <c>Rules.Effects</c> at the bottom of the layering without naming an aggregate.
/// </para>
/// <para>
/// The ordering obligation: <see cref="Effects"/> must be in an order that is a function of the
/// build, identical on client and server — never of hash iteration, dictionary enumeration or object
/// identity. <see cref="EffectResolutionOrder"/>'s tiebreak for two effects sharing one id is this
/// list's index, so a source that enumerated a <c>HashSet</c> would put a device-dependent order
/// back exactly there.
/// </para>
/// <para>
/// "Active" here does not mean "condition satisfied" — that's a separate filter step, since
/// conditions are pure functions of current state re-evaluated every pass, and a source that
/// pre-filtered by condition would cache an answer that's wrong on the next tick. A source reports
/// what the build holds: an unequipped item contributes nothing, a held perk contributes its clauses
/// whatever their conditions say.
/// </para>
/// </remarks>
internal interface IEffectSource
{
    /// <summary>Which of the ten sources this is.</summary>
    EffectSourceKind Kind { get; }

    /// <summary>
    /// The effects this source contributes, each with the holding it comes from, in a stable,
    /// build-determined order. Never <c>null</c>, and never containing a <c>null</c> effect.
    /// </summary>
    /// <remarks>See the type remarks for the ordering and identity obligations.</remarks>
    IReadOnlyList<SourcedEffect> Effects { get; }
}

/// <summary>One effect as a source reports it: the authored effect, and which holding it came from.</summary>
/// <param name="Effect">The authored effect.</param>
/// <param name="Instance">The holding — a perk in a draft slot, an affix on a gear item.</param>
/// <remarks>
/// <para>
/// The source supplies the identity rather than this layer deriving it, because the obvious
/// derivation — <c>(actorId, effectId)</c> — collapses two copies of one effect on one actor into a
/// single counter and can't survive a battle boundary. What's stable across a run is the holding,
/// and a source is precisely the layer that knows one (M4-03's gear source knows which slot an affix
/// rolled on, M3-07's perk source knows which draft slot a perk sits in).
/// </para>
/// <para>
/// Not the same as <see cref="CollectedEffect"/>'s <c>(Source, IndexInSource)</c> — that pair is an
/// ordering key, valid for one resolution pass; this is an identity key that must survive battle
/// boundaries. Two effects can share an ordering position across passes and be different holdings.
/// </para>
/// </remarks>
internal readonly record struct SourcedEffect(EffectDefinition Effect, EffectInstanceId Instance);

/// <summary>The in-<c>Core</c> <see cref="IEffectSource"/>: a source that contributes exactly the effects it was given.</summary>
/// <remarks>
/// <para>
/// Not a placeholder for the ten absent sources — it carries no notion of gear, a perk or a draft.
/// It's the degenerate implementation that lets collection and filtering be built, tested and frozen
/// now, and what the balance harness hands synthetic builds through, since that harness has no
/// <c>Player</c> either.
/// </para>
/// <para>
/// The list is copied on construction: a source is a reading of the build at one resolution pass, so
/// a caller that kept a handle on the list it passed in could otherwise mutate the collected set
/// mid-pass.
/// </para>
/// </remarks>
internal sealed class ListEffectSource : IEffectSource
{
    private readonly ReadOnlyCollection<SourcedEffect> _effects;

    /// <summary>A source of the given kind, contributing the given holdings in the given order.</summary>
    /// <exception cref="ArgumentException">
    /// An element carries a <c>null</c> effect, or an <see cref="EffectInstanceId"/> naming no holding.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The kind is outside the declared ten.</exception>
    internal ListEffectSource(EffectSourceKind kind, IEnumerable<SourcedEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        // Refused here rather than at the resolver: a kind outside the ten has no position in the
        // collection order, so EffectResolutionOrder's tiebreak would be undefined for it.
        _ = EffectSourceCatalogue.RowFor(kind);

        Kind = kind;

        // Copied and wrapped: the copy stops a caller mutating the list it passed in; the wrapper
        // stops the reverse — an array returned as IReadOnlyList<T> can be cast back and written
        // through, letting a consumer edit the build mid-pass.
        var copy = effects.ToArray();
        _effects = new ReadOnlyCollection<SourcedEffect>(copy);

        for (var i = 0; i < copy.Length; i++)
        {
            var position = i.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (copy[i].Effect is null)
            {
                throw new ArgumentException(
                    $"element {position} of the {kind} source has a null effect. 18 §8 step 1 collects " +
                    "effects; a hole in the list would reach step 2 as an effect with no id and no op, " +
                    "and the resolution order is stated over ids.",
                    nameof(effects));
            }

            if (!copy[i].Instance.NamesAHolding)
            {
                throw new ArgumentException(
                    $"element {position} of the {kind} source ('{copy[i].Effect.Id}') names no holding. " +
                    "EffectInstanceId is the effects layer's one instance identity, and every instance " +
                    "registered without a name would share one counter — 18 §3's per-instance rule " +
                    "failing in the direction that looks like it works.",
                    nameof(effects));
            }
        }
    }

    /// <summary>
    /// A source whose holdings are synthetic: each instance id is derived from the kind and the list
    /// position. For the balance harness and for tests, neither of which has a build to read a real
    /// holding from.
    /// </summary>
    /// <remarks>
    /// Named so no production caller reaches for it by accident: a list position is not stable across
    /// battle boundaries the way a run-scoped counter needs — equipping one more item renumbers every
    /// affix behind it. Correct for a single synthetic evaluation, wrong for anything spanning
    /// battles. The ten real sources supply the holding instead — see <see cref="SourcedEffect"/>.
    /// </remarks>
    internal static ListEffectSource Synthetic(EffectSourceKind kind, params EffectDefinition[] effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        var sourced = new SourcedEffect[effects.Length];

        for (var i = 0; i < effects.Length; i++)
        {
            var position = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var id = effects[i]?.Id ?? "?";

            sourced[i] = new SourcedEffect(
                effects[i]!, EffectInstanceId.Of($"synthetic:{kind}:{position}:{id}"));
        }

        return new ListEffectSource(kind, sourced);
    }

    /// <inheritdoc />
    public EffectSourceKind Kind { get; }

    /// <inheritdoc />
    public IReadOnlyList<SourcedEffect> Effects => _effects;
}
