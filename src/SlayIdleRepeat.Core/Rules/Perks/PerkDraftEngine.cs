using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// One offered draft option: which perk, its rarity band (the drawn band, which is what the
/// composition rules key on), its category, and whether taking it upgrades an already-owned copy
/// rather than granting a fresh one.
/// </summary>
/// <param name="PerkId">The perk id offered.</param>
/// <param name="Rarity">The rarity band this option was drawn under.</param>
/// <param name="Category">The perk's category — the (stubbed) diversity rule's own key.</param>
/// <param name="IsUpgrade">
/// True when the player already owns <paramref name="PerkId"/> below its max tier — a gold-border
/// "UPGRADE" option — false for a fresh grant.
/// </param>
/// <param name="NewTier">The tier taking this option lands on: 1 for a fresh grant, else the owned tier + 1.</param>
internal readonly record struct DraftOption(
    string PerkId, PerkRarity Rarity, PerkCategory Category, bool IsUpgrade, int NewTier);

/// <summary>
/// Draws one 3-option perk draft.
/// </summary>
/// <remarks>
/// Deterministic, one draw per slot: each slot draws exactly one rarity and exactly one perk within
/// it, two draw-stream indices per slot. Calling this against the same stream position with the same
/// catalogue and owned perks always produces the same three options, which is what lets the client
/// regenerate "what it's looking at" without persisting it. Tier III removal is applied directly here
/// — a perk owned at its max tier is excluded from <see cref="EligiblePerks"/> — since it's a base
/// drafting rule, not one of the composition rules in <see cref="DraftCompositionRules"/>.
/// </remarks>
internal static class PerkDraftEngine
{
    /// <summary>A draft always offers three options.</summary>
    internal const int OptionCount = 3;

    /// <summary>The weight a candidate carries before the Codex bias touches it.</summary>
    private const double UnbiasedWeight = 1.0;

    /// <summary>Draws <see cref="OptionCount"/> options from <paramref name="rng"/>.</summary>
    /// <param name="catalogue">The authored perks.</param>
    /// <param name="owned">The run's currently-owned perks and their tiers.</param>
    /// <param name="rng">The run's draft RNG stream, continued — never restarted.</param>
    /// <param name="stage">1, 2 or 3, ignored when <paramref name="isBoss"/> is true.</param>
    /// <param name="isElite">Whether the just-won battle was an Elite tile.</param>
    /// <param name="isBoss">Whether the just-won battle was the Boss tile.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Every perk in the catalogue is already owned at its max tier, so there is nothing left to
    /// offer — see this type's remarks for why that cannot happen against the full catalogue.
    /// </exception>
    /// <param name="tuning">The pity registry, for the weights and the cap the draft draws under.</param>
    /// <param name="forces">
    /// The slots the <c>DRAFT</c> guarantees have floored, as the luck façade answered them. The
    /// draft composes against these; it never restates any of the five rules itself.
    /// </param>
    /// <param name="everDraftedPerkIds">
    /// Perk ids known to have been drafted before — the Codex bias's input. ⚠️ Incomplete today: no
    /// player-lifetime Codex exists (<b>M4-11</b> owns it), so the only set available is the run's
    /// own, a genuine subset. The rule is exact against whatever this carries.
    /// </param>
    internal static IReadOnlyList<DraftOption> GenerateOptions(
        PerkCatalogue catalogue,
        DraftedPerks owned,
        DeterministicRng rng,
        LuckTuning tuning,
        IReadOnlyList<DraftForce> forces,
        IReadOnlySet<string> everDraftedPerkIds,
        int stage,
        bool isElite,
        bool isBoss)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(owned);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(forces);
        ArgumentNullException.ThrowIfNull(everDraftedPerkIds);

        var weights = DraftRarityWeights.For(stage, isElite, isBoss);
        var options = new DraftOption[OptionCount];

        // M4-01b Phase 3 owns the composition: the per-slot bias roll, the floored pools of `forces`,
        // the duplicate and diversity narrowing, and the Codex cap read here. The draw below is
        // M3-06's, unchanged, and is what Phase 3 replaces.
        _ = LuckService.MaxCodexBiasedOptions(tuning);

        for (var i = 0; i < OptionCount; i++)
        {
            var rarity = rng.WeightedPick(weights);
            options[i] = DrawOption(catalogue, owned, rng, tuning, everDraftedPerkIds, rarity);
        }

        return Array.AsReadOnly(options);
    }

    private static DraftOption DrawOption(
        PerkCatalogue catalogue,
        DraftedPerks owned,
        DeterministicRng rng,
        LuckTuning tuning,
        IReadOnlySet<string> everDraftedPerkIds,
        PerkRarity rarity)
    {
        // Read here rather than at the call site because Phase 3's bias roll is per slot, and the
        // roll belongs beside the pool it chooses between.
        _ = LuckService.OwnedUpgradeBias(tuning);

        var pool = EligiblePerks(catalogue, owned, rarity);

        if (pool.Count == 0)
        {
            // Fallback across every rarity band, in enum-declaration (ascending) order, so the draw
            // stays deterministic even when the drawn band is exhausted.
            foreach (var candidate in Enum.GetValues<PerkRarity>())
            {
                pool = EligiblePerks(catalogue, owned, candidate);
                if (pool.Count > 0)
                {
                    rarity = candidate;
                    break;
                }
            }
        }

        if (pool.Count == 0)
        {
            throw new InvalidOperationException(
                "Every perk in 06 §3's authored catalogue is already owned at its max tier. 06 §1.1 " +
                "removes a maxed perk from the draft pool, and this content version authors only " +
                "46 rows (M3-07's starter subset) — the remaining rows are M3-07b's. This cannot " +
                "happen against the full catalogue.");
        }

        var candidates = new (PerkCatalogueEntry item, double weight)[pool.Count];
        for (var i = 0; i < pool.Count; i++)
        {
            candidates[i] = (pool[i], UnbiasedWeight);
        }

        var perk = rng.WeightedPick(candidates);
        var ownedTier = owned.TierOf(perk.Id);
        var isUpgrade = ownedTier > 0;

        return new DraftOption(perk.Id, perk.Rarity, perk.Category, isUpgrade, isUpgrade ? ownedTier + 1 : 1);
    }

    /// <summary>Every perk of <paramref name="rarity"/> not already owned at its max tier.</summary>
    private static IReadOnlyList<PerkCatalogueEntry> EligiblePerks(
        PerkCatalogue catalogue, DraftedPerks owned, PerkRarity rarity)
    {
        var eligible = new List<PerkCatalogueEntry>();

        foreach (var perk in catalogue.OfRarity(rarity))
        {
            if (owned.TierOf(perk.Id) < perk.TierCount)
            {
                eligible.Add(perk);
            }
        }

        return eligible;
    }
}
