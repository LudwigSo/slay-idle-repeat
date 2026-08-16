using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Perks;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// One offered draft option: which perk, that perk's own rarity band, its category, and whether
/// taking it upgrades an already-owned copy rather than granting a fresh one.
/// </summary>
/// <param name="PerkId">The perk id offered.</param>
/// <param name="Rarity">
/// The offered perk's own band — <em>not</em> the band the slot drew, which the two can differ on
/// whenever a guarantee floored the pool, the bias roll hit, or the drawn band was empty. The band a
/// player is actually offered is what the quality floor's counter reads, so it is the one carried.
/// </param>
/// <param name="Category">The perk's category — the diversity rule's own key.</param>
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
/// <para>
/// Deterministic, and <b>exactly three draw indices per slot</b>: the owned-upgrade bias roll, the
/// rarity band, then the perk. All three are taken on every slot whichever branch the slot ends up
/// on, so a resumed draft stream lands in the same place regardless of which guarantees fired —
/// which is what lets the client regenerate "what it's looking at" without persisting it.
/// </para>
/// <para>
/// Nothing here decides a guarantee. The luck façade answers which slots the <c>DRAFT</c> class has
/// floored and what weight the Codex bias puts on a perk; this type composes against those answers.
/// Forcing is expressed as <em>flooring the pool a slot draws from</em>, never as an extra draw, and
/// the composition rules are enforced the same way — by narrowing the pool rather than by drawing
/// and repairing. A narrowing that would empty the pool falls through instead, so an unsatisfiable
/// guarantee costs the draft nothing but the guarantee.
/// </para>
/// <para>
/// Tier III removal is applied directly here — a perk owned at its max tier never enters the pool —
/// since it is a base drafting rule rather than one of the composition rules.
/// </para>
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
    /// Refused before anything is drawn, so the stream is left where it stood.
    /// </exception>
    /// <param name="tuning">The pity registry, for the weights and the cap the draft draws under.</param>
    /// <param name="forces">
    /// The slots the <c>DRAFT</c> guarantees have floored, as the luck façade answered them. The
    /// draft composes against these; it never restates any of them.
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

        var draft = new DraftDraw(
            Draftable(catalogue, owned),
            owned,
            tuning,
            DraftRarityWeights.For(stage, isElite, isBoss),
            forces,
            everDraftedPerkIds);

        if (draft.Draftable.Count == 0)
        {
            throw new InvalidOperationException(
                "Every perk in 06 §3's authored catalogue is already owned at its max tier. 06 §1.1 " +
                "removes a maxed perk from the draft pool, and this content version authors only " +
                "46 rows (M3-07's starter subset) — the remaining rows are M3-07b's. This cannot " +
                "happen against the full catalogue.");
        }

        // How many of this draft's options the Codex bias is still allowed to select. Without the
        // cap a fresh account, whose whole catalogue is never-drafted, would see the bias on every
        // option and the rarity table would stop meaning anything.
        var codexBudget = LuckService.MaxCodexBiasedOptions(tuning);

        var options = new DraftOption[OptionCount];
        var chosenIds = new List<string>(OptionCount);
        var chosenCategories = new List<PerkCategory>(OptionCount);

        for (var slot = 0; slot < OptionCount; slot++)
        {
            var option = DrawOption(draft, rng, slot, chosenIds, chosenCategories, codexBudget > 0);

            options[slot] = option;
            chosenIds.Add(option.PerkId);
            chosenCategories.Add(option.Category);

            // An option the bias could have reached spends a unit of the cap. Which of the two pools
            // it came out of is not observable after the pick, so the budget is spent on the
            // observable half — a never-drafted perk offered while the bias was still live.
            if (codexBudget > 0 && !everDraftedPerkIds.Contains(option.PerkId))
            {
                codexBudget--;
            }
        }

        return Array.AsReadOnly(options);
    }

    /// <summary>Everything one draft slot draws against, beyond the slot's own position.</summary>
    /// <param name="Draftable">Every perk not already owned at its max tier, in catalogue order.</param>
    /// <param name="Owned">The run's owned perks and their tiers.</param>
    /// <param name="Tuning">The pity registry the façade is asked through.</param>
    /// <param name="Weights">The rarity table this battle's draft draws bands from.</param>
    /// <param name="Forces">The slots the <c>DRAFT</c> guarantees have floored.</param>
    /// <param name="EverDrafted">The Codex bias's input set.</param>
    private readonly record struct DraftDraw(
        IReadOnlyList<PerkCatalogueEntry> Draftable,
        DraftedPerks Owned,
        LuckTuning Tuning,
        IReadOnlyList<(PerkRarity Rarity, double Weight)> Weights,
        IReadOnlyList<DraftForce> Forces,
        IReadOnlySet<string> EverDrafted);

    /// <summary>One slot: three draws, and the pool the middle two are spent against.</summary>
    private static DraftOption DrawOption(
        DraftDraw draft,
        DeterministicRng rng,
        int slot,
        IReadOnlyList<string> chosenIds,
        IReadOnlyList<PerkCategory> chosenCategories,
        bool codexBiasAvailable)
    {
        // Draw 1 of 3. Taken on every slot whether or not an owned pool exists and whether or not a
        // guarantee has already claimed this slot, so the per-slot budget is fixed.
        var biasHits = DraftCompositionRules.OwnedUpgradeBiasHits(
            rng.NextDouble(), LuckService.OwnedUpgradeBias(draft.Tuning));

        // Draw 2 of 3, for the same reason: a forced slot takes its band from the force, and still
        // spends the index the unforced slot beside it would have.
        var band = rng.WeightedPick(draft.Weights);

        var available = WithoutClashingCategory(
            WithoutAlreadyChosen(draft.Draftable, chosenIds), chosenCategories, slot);

        var pool = SlotPool(available, band, ForceOn(draft.Forces, slot), biasHits, draft.Owned);

        var candidates = new (PerkCatalogueEntry Perk, double Weight)[pool.Count];
        for (var i = 0; i < pool.Count; i++)
        {
            candidates[i] = (
                pool[i],
                codexBiasAvailable
                    ? LuckService.DraftFreshPoolWeight(draft.Tuning, draft.EverDrafted.Contains(pool[i].Id))
                    : UnbiasedWeight);
        }

        // Draw 3 of 3.
        var perk = rng.WeightedPick(candidates);
        var ownedTier = draft.Owned.TierOf(perk.Id);
        var isUpgrade = ownedTier > 0;

        return new DraftOption(perk.Id, perk.Rarity, perk.Category, isUpgrade, isUpgrade ? ownedTier + 1 : 1);
    }

    /// <summary>The pool one slot draws its perk from, floored by whichever rule claims the slot.</summary>
    /// <remarks>
    /// A guarantee outranks the bias roll: the roll is a preference and the force is a promise. An
    /// unsatisfiable force falls through to the unforced pool rather than throwing — the guarantee
    /// goes unpaid this draft and its counter stays standing, which is a draft the player can still
    /// take rather than a run that cannot continue.
    /// </remarks>
    private static IReadOnlyList<PerkCatalogueEntry> SlotPool(
        IReadOnlyList<PerkCatalogueEntry> available,
        PerkRarity band,
        DraftForce? force,
        bool biasHits,
        DraftedPerks owned)
    {
        if (force is { } floored)
        {
            var forced = Matching(available, perk => Satisfies(perk, floored, owned));

            return forced.Count > 0 ? forced : available;
        }

        if (biasHits)
        {
            // Already free of maxed perks, so an owned one here is owned-but-not-maxed.
            var upgrades = Matching(available, perk => owned.TierOf(perk.Id) > 0);

            if (upgrades.Count > 0)
            {
                return upgrades;
            }
        }

        var banded = Matching(available, perk => perk.Rarity == band);

        return banded.Count > 0 ? banded : available;
    }

    /// <summary>Whether one perk pays the force assigned to a slot.</summary>
    private static bool Satisfies(PerkCatalogueEntry perk, DraftForce force, DraftedPerks owned) =>
        (force.RarityAtLeast is not { } floor || perk.Rarity >= floor) &&
        (force.Category is not { } category || perk.Category == category) &&
        (!force.RequiresOwnedUpgrade || owned.TierOf(perk.Id) > 0);

    /// <summary>The force assigned to a slot, or <see langword="null"/> when none is.</summary>
    private static DraftForce? ForceOn(IReadOnlyList<DraftForce> forces, int slot)
    {
        foreach (var force in forces)
        {
            if (force.SlotIndex == slot)
            {
                return force;
            }
        }

        return null;
    }

    /// <summary>The pool without the perks earlier slots already took.</summary>
    /// <remarks>
    /// This is the no-duplicate rule, enforced by construction. It falls through when it would empty
    /// the pool — a catalogue with fewer draftable perks than a draft has slots still owes three
    /// options — which is the only shape in which a repeat can reach a player.
    /// </remarks>
    private static IReadOnlyList<PerkCatalogueEntry> WithoutAlreadyChosen(
        IReadOnlyList<PerkCatalogueEntry> pool, IReadOnlyList<string> chosenIds)
    {
        if (chosenIds.Count == 0)
        {
            return pool;
        }

        var remaining = Matching(pool, perk => !Contains(chosenIds, perk.Id));

        return remaining.Count > 0 ? remaining : pool;
    }

    /// <summary>The pool without the one category the draft is about to be made entirely of.</summary>
    /// <remarks>
    /// Diversity can only fail on the last slot, and only when every option so far shares one
    /// category, so that is the only slot narrowed — excluding a category earlier would be a
    /// stricter rule than the two-distinct-categories one actually stated.
    /// </remarks>
    private static IReadOnlyList<PerkCatalogueEntry> WithoutClashingCategory(
        IReadOnlyList<PerkCatalogueEntry> pool,
        IReadOnlyList<PerkCategory> chosenCategories,
        int slot)
    {
        if (slot != OptionCount - 1 || chosenCategories.Count == 0)
        {
            return pool;
        }

        var clashing = chosenCategories[0];

        foreach (var category in chosenCategories)
        {
            if (category != clashing)
            {
                return pool;
            }
        }

        var diverse = Matching(pool, perk => perk.Category != clashing);

        return diverse.Count > 0 ? diverse : pool;
    }

    /// <summary>Every perk not already owned at its max tier, in the catalogue's own order.</summary>
    private static IReadOnlyList<PerkCatalogueEntry> Draftable(
        PerkCatalogue catalogue, DraftedPerks owned) =>
        Matching(catalogue.All, perk => owned.TierOf(perk.Id) < perk.TierCount);

    /// <summary>The rows of a pool a narrowing keeps, in the pool's order.</summary>
    private static IReadOnlyList<PerkCatalogueEntry> Matching(
        IReadOnlyList<PerkCatalogueEntry> pool, Func<PerkCatalogueEntry, bool> keep)
    {
        var kept = new List<PerkCatalogueEntry>(pool.Count);

        foreach (var perk in pool)
        {
            if (keep(perk))
            {
                kept.Add(perk);
            }
        }

        return kept;
    }

    private static bool Contains(IReadOnlyList<string> ids, string id)
    {
        foreach (var candidate in ids)
        {
            if (string.Equals(candidate, id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
