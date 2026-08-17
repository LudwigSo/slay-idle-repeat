namespace SlayIdleRepeat.Core.Model;

/// <summary>The perks a run has drafted: perk id to the internal tier currently owned (1-3).</summary>
/// <remarks>
/// <para>
/// Carries a perk by its bare <c>string</c> id rather than a catalogue entry, since <c>Model</c> may
/// not reference <c>Content</c>. A live view over <c>Run</c>'s own owned-perk map — never held across
/// a mutation.
/// </para>
/// <para>
/// 🔒 <b>A <c>class</c>, not a <c>record</c>, and that is steering S17 rather than a style choice.</b>
/// A synthesized record <c>Equals</c> compares an <c>IReadOnlyDictionary</c> component <b>by
/// reference</b>, so two views over equal maps would have compared unequal and two views over one map
/// would have compared equal whatever it held — an equality that means nothing while looking like it
/// means something, which is the shape M1's two Criticals had. This type used none of what a record
/// buys: no positional syntax, no <c>with</c>, an explicit constructor and one get-only property. So
/// the meaningless equality is <em>deleted</em> rather than documented, and identity comparison —
/// which is what a live view actually supports — is what is left. Aggregate state is compared by
/// `14` §16.6's canonical bytes, never by a value comparison on a view. <c>FeatCounters</c> carries
/// the same remark for the same reason: kickoff §A2 rules the pair "fix both or neither".
/// </para>
/// </remarks>
public sealed class DraftedPerks
{
    /// <summary>Builds a view over <paramref name="tiers"/>. <c>internal</c>: constructible only from inside <c>Core</c>.</summary>
    internal DraftedPerks(IReadOnlyDictionary<string, int> tiers) => Tiers = tiers;

    /// <summary>Perk id → owned tier (1-3). A perk absent from this map is not owned.</summary>
    public IReadOnlyDictionary<string, int> Tiers { get; }

    /// <summary>The tier owned for <paramref name="perkId"/>, or 0 when the perk is not owned.</summary>
    public int TierOf(string perkId) =>
        !string.IsNullOrWhiteSpace(perkId) && Tiers.TryGetValue(perkId, out var tier) ? tier : 0;
}
