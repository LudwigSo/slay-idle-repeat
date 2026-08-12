using System.Collections.ObjectModel;
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
/// S6's <em>"filling a hole with a plausible value"</em> ten times over. An implementation is
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
    /// The effects this source contributes, each with the holding it comes from, in a stable,
    /// build-determined order. Never <c>null</c>, and never containing a <c>null</c> effect.
    /// </summary>
    /// <remarks>See the type remarks for the ordering and identity obligations.</remarks>
    IReadOnlyList<SourcedEffect> Effects { get; }
}

/// <summary>
/// One effect as a `18` §8 step 1 source reports it: the authored effect, and
/// <b>which holding it came from</b>.
/// </summary>
/// <param name="Effect">The authored effect.</param>
/// <param name="Instance">
/// 🔒 <see cref="EffectInstanceId"/> — <em>"the effects layer's <b>one</b> instance identity"</em>,
/// naming <em>"the holding — the perk in a draft slot, the affix on a gear item"</em>.
/// </param>
/// <remarks>
/// <para>
/// 🔒 <b>Why the source supplies it, and why that is not a widening for its own sake.</b>
/// <see cref="EffectInstanceId"/> states in terms that this layer must never <em>derive</em> the
/// identity: <c>(actorId, effectId)</c> <em>"collapses two copies of one effect on one actor into a
/// single counter"</em> and <em>"cannot be stable across a battle boundary, which the same sentence
/// requires of <c>ON_KILL</c>"</em>, so <c>PK_MIDAS</c>'s every-6th-kill counter would restart every
/// fight. What is stable across a run is the holding, and <b>a `18` §8 step 1 source is precisely
/// the layer that knows one</b> — M4-03's gear source knows which slot an affix rolled on, M3-07's
/// perk source knows which draft slot a perk sits in. Making the source report it is the seam doing
/// its job rather than seven later milestones each inventing an answer.
/// </para>
/// <para>
/// ⚠️ <b>This is NOT <see cref="CollectedEffect"/>'s <c>(Source, IndexInSource)</c>, and the two must
/// never be conflated.</b> That pair is an <b>ordering</b> key: it is per-resolution-pass, it exists
/// only to make `18` §8's effect-id order total, and it changes the moment a build gains a gear slot.
/// This is an <b>identity</b> key: it must survive battle boundaries, and
/// <c>TriggerRegistry.Register</c> refuses a duplicate. Two effects can share an ordering position
/// across passes and be different holdings, and one holding keeps its identity while its ordering
/// position moves.
/// </para>
/// </remarks>
internal readonly record struct SourcedEffect(EffectDefinition Effect, EffectInstanceId Instance);

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
/// ⚠️ It is not a placeholder for the ten absent sources and must not become one. It carries no
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
    private readonly ReadOnlyCollection<SourcedEffect> _effects;

    /// <summary>A source of the given kind, contributing the given holdings in the given order.</summary>
    /// <exception cref="ArgumentException">
    /// An element carries a <c>null</c> effect, or an <see cref="EffectInstanceId"/> naming no holding.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The kind is outside `18` §8 step 1's ten.</exception>
    internal ListEffectSource(EffectSourceKind kind, IEnumerable<SourcedEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        // 🔒 Refused here rather than at the resolver: a kind outside the ten has no position in
        //    `18` §8 step 1's order, so EffectResolutionOrder's tiebreak would be undefined for it.
        _ = EffectSourceCatalogue.RowFor(kind);

        Kind = kind;

        // 🔒 Copied AND wrapped. The copy stops a caller mutating the list it passed in; the wrapper
        //    stops the reverse — an `EffectDefinition[]` returned as `IReadOnlyList<T>` can be cast
        //    back and written through, which would let a consumer edit the build between step 1 and
        //    step 2 of one resolution pass.
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
    /// ⚠️ A source whose holdings are <b>synthetic</b>: each instance id is derived from the kind and
    /// the list position. For `05` §9's balance harness and for tests, neither of which has a build to
    /// read a real holding from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Named so that no production caller reaches for it by accident.</b>
    /// <see cref="EffectInstanceId"/> requires an id stable <em>across battle boundaries</em>, because
    /// `18` §3's <c>ON_KILL</c> counters persist for the run — and a list position is not: equipping
    /// one more item renumbers every affix behind it and <c>PK_MIDAS</c>'s counter restarts. These ids
    /// are correct for a single synthetic evaluation and wrong for anything spanning battles, which is
    /// exactly what `05` §9's harness does and does not do.
    /// </para>
    /// <para>
    /// The ten real sources supply the holding instead — see <see cref="SourcedEffect"/>.
    /// </para>
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
