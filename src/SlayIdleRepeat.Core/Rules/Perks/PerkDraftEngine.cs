using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// 🔒 M3-06 — one offered draft option: which perk, its rarity band (the drawn band, which is what
/// `06` §4's composition rules key on), its category, and whether taking it upgrades an
/// already-owned copy rather than granting a fresh one.
/// </summary>
/// <param name="PerkId">The `06` §3 perk id offered.</param>
/// <param name="Rarity">The rarity band this option was drawn under.</param>
/// <param name="Category">`06` §2's category — the (stubbed) diversity rule's own key.</param>
/// <param name="IsUpgrade">
/// True when the player already owns <paramref name="PerkId"/> below its max tier — `06` §1.1's
/// gold-border "UPGRADE" option — false for a fresh grant.
/// </param>
/// <param name="NewTier">The tier taking this option lands on: 1 for a fresh grant, else the owned tier + 1.</param>
internal readonly record struct DraftOption(
    string PerkId, PerkRarity Rarity, PerkCategory Category, bool IsUpgrade, int NewTier);

/// <summary>
/// 🔒 M3-06, `06` §1-§4 — draws one 3-option perk draft.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Deterministic, one draw per slot.</b> Each of the three slots draws exactly one rarity
/// (<see cref="DraftRarityWeights.For"/>, via <see cref="DeterministicRng.WeightedPick{T}"/>) and
/// exactly one perk within that rarity (a second <c>WeightedPick</c>, uniform over the eligible
/// pool) — two draw-stream indices per slot, six for a full draft. Calling this against the same
/// <c>draft</c>-stream position with the same catalogue and the same owned perks always produces
/// the same three options, which is what lets <c>PICK_PERK</c>/<c>REROLL_DRAFT</c>/<c>SKIP_DRAFT</c>
/// regenerate "what the client is looking at" without persisting it (14 §8.1: combat, board and
/// drafting stay byte-identical for a given seed).
/// </para>
/// <para>
/// 🔒 <b>Tier III removal is real, not a composition-rule stub.</b> `06` §1.1's "once a perk is at
/// Tier III it is removed from that run's draft pool" is applied here directly — a perk owned at its
/// max tier is excluded from <see cref="EligiblePerks"/> — because it is a base drafting rule, not
/// one of `06` §4's eight LuckService-owned composition rules (<see cref="DraftCompositionRules"/>).
/// </para>
/// </remarks>
internal static class PerkDraftEngine
{
    /// <summary>`06` §1 — a draft always offers three options.</summary>
    internal const int OptionCount = 3;

    /// <summary>Draws <see cref="OptionCount"/> options from <paramref name="rng"/>.</summary>
    /// <param name="catalogue">`06` §3's authored perks.</param>
    /// <param name="owned">The run's currently-owned perks and their tiers.</param>
    /// <param name="rng">The run's <c>draft</c> stream (`14` §8.1), continued — never restarted.</param>
    /// <param name="stage">1, 2 or 3, ignored when <paramref name="isBoss"/> is true.</param>
    /// <param name="isElite">Whether the just-won battle was an Elite tile.</param>
    /// <param name="isBoss">Whether the just-won battle was the Boss tile.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Every perk in the catalogue is already owned at its max tier, so there is nothing left to
    /// offer — see this type's remarks for why that cannot happen against the full catalogue.
    /// </exception>
    internal static IReadOnlyList<DraftOption> GenerateOptions(
        PerkCatalogue catalogue,
        DraftedPerks owned,
        DeterministicRng rng,
        int stage,
        bool isElite,
        bool isBoss)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(owned);
        ArgumentNullException.ThrowIfNull(rng);

        var weights = DraftRarityWeights.For(stage, isElite, isBoss);
        var options = new DraftOption[OptionCount];

        for (var i = 0; i < OptionCount; i++)
        {
            var rarity = rng.WeightedPick(weights);
            options[i] = DrawOption(catalogue, owned, rng, rarity);
        }

        // 🔒 06 §4 / 24 §4.7's eight composition rules — every one a documented no-op until M4-01's
        // LuckService exists. Called so the seam is exercised (and greppable) even though nothing
        // here changes `options` yet; see DraftCompositionRules' own remarks for the consequence.
        _ = DraftCompositionRules.NoDuplicateOptions();
        _ = DraftCompositionRules.CategoryDiversity();
        _ = DraftCompositionRules.OwnedUpgradeBias();
        _ = DraftCompositionRules.LegendaryPity();
        _ = DraftCompositionRules.AntiBrickSustain();
        _ = DraftCompositionRules.QualityFloor();
        _ = DraftCompositionRules.CodexBias();
        _ = DraftCompositionRules.UpgradeFamine();

        return Array.AsReadOnly(options);
    }

    private static DraftOption DrawOption(
        PerkCatalogue catalogue, DraftedPerks owned, DeterministicRng rng, PerkRarity rarity)
    {
        var pool = EligiblePerks(catalogue, owned, rarity);

        if (pool.Count == 0)
        {
            // 🔒 Fallback across every rarity band, in enum-declaration (ascending) order, so the
            // draw stays deterministic even when the drawn band is exhausted. Only reachable once a
            // whole rarity band is owned at max tier, which the 46-row starter catalogue cannot
            // produce (see this type's remarks).
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
            candidates[i] = (pool[i], 1.0);
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
