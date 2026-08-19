using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>The <c>PERKS</c> source: the effects of the perks this run has drafted, at the tiers it owns them.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>This is the type that makes a drafted perk mean anything.</b> Before it, the draft wrote an
/// id and a tier into the run and nothing read the tier's effects — a player could take Sharp Edge
/// and fight exactly as hard as one who skipped it. The catalogue was authored, the interpreter was
/// built, and the wire between them was the piece missing.
/// </para>
/// <para>
/// 🔒 <b>Catalogue order, not the owned map's.</b> The source contract requires an order that is a
/// function of the build and identical on client and server, and the run holds its perks in a
/// dictionary keyed by id — enumerating that would put hash order into the resolution tiebreak.
/// The draft order the source catalogue's label describes is not persisted anywhere (a run stores
/// <c>{perkId: tier}</c>, not a sequence), so the document's own order is the one deterministic
/// ordering actually available, and it is stable against everything except a content edit.
/// </para>
/// <para>
/// A perk the run owns that this content version no longer authors is skipped, not refused, on the
/// loadout reader's precedent: a content rollback across a live run must not leave that run unable
/// to fight. What it costs the player is the perk; what refusing would cost them is the run.
/// </para>
/// </remarks>
internal sealed class PerkEffectSource : IEffectSource
{
    private readonly ReadOnlyCollection<SourcedEffect> _effects;

    /// <summary>Reads the effects the run's drafted perks contribute at their owned tiers.</summary>
    /// <param name="content">The version-stamped snapshot the catalogue and its effects are read from.</param>
    /// <param name="owned">What the run has drafted, and at which tier.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidTunableException">An owned tier authors an effect in a shape the reader does not map.</exception>
    internal PerkEffectSource(ContentSnapshot content, DraftedPerks owned)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(owned);

        var effects = new List<SourcedEffect>();

        // A build holding no perks does not read the catalogue at all. Not an optimisation: a hero
        // screen outside a run is built against whatever content the caller loaded, and demanding a
        // perk document from a build that could not use one would make an absent catalogue fatal to
        // callers that never drafted anything.
        foreach (var perk in owned.Tiers.Count == 0
            ? Array.Empty<PerkCatalogueEntry>()
            : PerkCatalogue.Read(content).All)
        {
            var tier = owned.TierOf(perk.Id);

            // Tier 0 is "not owned" rather than "owned at the bottom rung": DraftedPerks answers 0
            // for a perk absent from the map, and 06 §1.1 numbers a perk's tiers from 1.
            if (tier < 1)
            {
                continue;
            }

            // A tier above what the catalogue authors is the rollback case in the other direction —
            // a run that drafted a third rung off a content version that had one. Reading the top
            // authored tier keeps the perk working rather than throwing mid-fight.
            var owning = Math.Min(tier, perk.TierCount);

            foreach (var effect in PerkEffects.Of(content, perk.Id, owning))
            {
                effects.Add(new SourcedEffect(effect, PerkHolding(perk.Id, effect.Id)));
            }
        }

        _effects = new ReadOnlyCollection<SourcedEffect>(effects);
    }

    /// <summary>
    /// The holding one perk effect is registered under.
    /// </summary>
    /// <remarks>
    /// The perk id and not the tier: a perk upgraded mid-run is the same holding, so an
    /// <c>everyNth</c> counter or a once-per-run save carries across the upgrade instead of being
    /// reset by it. The effect id is included because a tier may author several, and two effects
    /// sharing one instance id would share one counter.
    /// </remarks>
    internal static EffectInstanceId PerkHolding(string perkId, string effectId) =>
        EffectInstanceId.Of($"perk:{perkId}:{effectId}");

    /// <inheritdoc />
    public EffectSourceKind Kind => EffectSourceKind.PERKS;

    /// <inheritdoc />
    public IReadOnlyList<SourcedEffect> Effects => _effects;
}
