namespace SlayIdleRepeat.Core.Model;

/// <summary>
/// 🔒 `30` §4 — the perks a run has drafted: perk id → the internal tier currently owned (1-3).
/// M3-06's answer to the <c>DraftedPerks</c> entry of
/// <c>SlayIdleRepeat.Architecture.Tests.GapRegister</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b><c>Model</c>, not <c>Content.Perks</c>.</b> `30` §11.4 puts <c>Model</c> below <c>Content</c>
/// in the internal layering (<c>Model</c> never references <c>Content</c>), so this type carries a
/// perk by its bare <c>string</c> id rather than a <see cref="Content.Perks.PerkCatalogueEntry"/> — the
/// same reason <c>Run.PendingTileKindValue</c> carries a tile kind as a bare <see cref="int"/>
/// rather than a <c>Rules.Board.TileKind</c>. The handler layer, which sits above both, is what
/// resolves an id against the catalogue.
/// </para>
/// <para>
/// A read-only wrapper over <c>Run</c>'s own owned-perk map, on <see cref="Model.PendingFork"/>'s
/// precedent: a live snapshot handed out by <c>Run.DraftedPerks</c>, never held across a mutation.
/// </para>
/// </remarks>
public sealed record DraftedPerks
{
    /// <summary>
    /// Builds a view over <paramref name="tiers"/>. <c>internal</c>, not <c>public</c> — `30` §11.2
    /// makes the domain's own state constructible only from inside <c>Core</c>, on
    /// <see cref="PendingFork"/>'s precedent; the only caller is <c>Run.DraftedPerks</c>.
    /// </summary>
    internal DraftedPerks(IReadOnlyDictionary<string, int> tiers) => Tiers = tiers;

    /// <summary>Perk id → owned tier (1-3). A perk absent from this map is not owned.</summary>
    public IReadOnlyDictionary<string, int> Tiers { get; }

    /// <summary>The tier owned for <paramref name="perkId"/>, or 0 when the perk is not owned.</summary>
    public int TierOf(string perkId) =>
        !string.IsNullOrWhiteSpace(perkId) && Tiers.TryGetValue(perkId, out var tier) ? tier : 0;
}
