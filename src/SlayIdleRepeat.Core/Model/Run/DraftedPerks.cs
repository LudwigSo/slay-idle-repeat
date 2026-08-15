namespace SlayIdleRepeat.Core.Model;

/// <summary>The perks a run has drafted: perk id to the internal tier currently owned (1-3).</summary>
/// <remarks>
/// Carries a perk by its bare <c>string</c> id rather than a catalogue entry, since <c>Model</c> may
/// not reference <c>Content</c>. A live view over <c>Run</c>'s own owned-perk map — never held across
/// a mutation.
/// </remarks>
public sealed record DraftedPerks
{
    /// <summary>Builds a view over <paramref name="tiers"/>. <c>internal</c>: constructible only from inside <c>Core</c>.</summary>
    internal DraftedPerks(IReadOnlyDictionary<string, int> tiers) => Tiers = tiers;

    /// <summary>Perk id → owned tier (1-3). A perk absent from this map is not owned.</summary>
    public IReadOnlyDictionary<string, int> Tiers { get; }

    /// <summary>The tier owned for <paramref name="perkId"/>, or 0 when the perk is not owned.</summary>
    public int TierOf(string perkId) =>
        !string.IsNullOrWhiteSpace(perkId) && Tiers.TryGetValue(perkId, out var tier) ? tier : 0;
}
