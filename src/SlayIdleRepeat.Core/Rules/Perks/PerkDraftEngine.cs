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

/// <summary>Everything one draft is drawn against, beyond the stream it draws from.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>A parameter object at the boundary, not inside it.</b> <c>PerkDraftEngine.GenerateOptions</c>
/// took nine positional parameters, six of which it immediately bundled into a private struct as its
/// first statement — so the shape this type describes already existed and only the <em>call sites</em>
/// were paying for its absence. Four of the nine (<c>tuning</c>, <c>forces</c>,
/// <c>everDraftedPerkIds</c>, <c>owned</c>) are reference types and three
/// (<c>stage</c>, <c>isElite</c>, <c>isBoss</c>) were adjacent same-typed flags, which is the
/// argument-transposition hazard a positional list cannot state away.
/// </para>
/// <para>
/// <see cref="PerkDraftEngine"/>'s draftable pool is the <b>only</b> member still derived inside the
/// engine, because it is a function of two members of this type and of nothing a caller knows better.
/// The rarity table is <em>not</em>: it is a function of the battle the draft follows, which is the
/// caller's fact, and deriving it here would have re-stated <c>DraftRarityWeights</c>' three-way
/// branch in a second place.
/// </para>
/// </remarks>
/// <param name="Catalogue">The authored perks.</param>
/// <param name="Owned">The run's currently-owned perks and their tiers.</param>
/// <param name="Tuning">The pity registry, for the weights and the cap the draft draws under.</param>
/// <param name="Weights">
/// The rarity table this battle's draft draws bands from — <c>DraftRarityWeights.For</c>'s answer for
/// the stage and battle kind the draft follows.
/// </param>
/// <param name="Forces">
/// The slots the <c>DRAFT</c> guarantees have floored, as the luck façade answered them. The draft
/// composes against these; it never restates any of them.
/// </param>
/// <param name="EverDrafted">
/// Perk ids known to have been drafted before — the Codex bias's input. ⚠️ Incomplete today: no
/// player-lifetime Codex exists (<b>M4-11</b> owns it), so the only set available is the run's own, a
/// genuine subset. The rule is exact against whatever this carries.
/// </param>
internal readonly record struct DraftRequest(
    PerkCatalogue Catalogue,
    DraftedPerks Owned,
    LuckTuning Tuning,
    IReadOnlyList<(PerkRarity Rarity, double Weight)> Weights,
    IReadOnlyList<DraftForce> Forces,
    IReadOnlySet<string> EverDrafted);

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
    /// <param name="request">Everything the draft draws against — see <see cref="DraftRequest"/>.</param>
    /// <param name="rng">The run's draft RNG stream, continued — never restarted.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="rng"/> is null, or a reference member of <paramref name="request"/> is.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Every perk in the catalogue is already owned at its max tier, so there is nothing left to
    /// offer — see this type's remarks for why that cannot happen against the full catalogue.
    /// Refused before anything is drawn, so the stream is left where it stood.
    /// </exception>
    /// <remarks>
    /// The members of <paramref name="request"/> are null-guarded here rather than in the request's
    /// own constructor: it is a <c>record struct</c>, so <c>default(DraftRequest)</c> is reachable
    /// without running any constructor at all and a guard there would be one a caller can step past.
    /// </remarks>
    internal static IReadOnlyList<DraftOption> GenerateOptions(
        DraftRequest request, DeterministicRng rng)
    {
        ArgumentNullException.ThrowIfNull(request.Catalogue);
        ArgumentNullException.ThrowIfNull(request.Owned);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(request.Tuning);
        ArgumentNullException.ThrowIfNull(request.Weights);
        ArgumentNullException.ThrowIfNull(request.Forces);
        ArgumentNullException.ThrowIfNull(request.EverDrafted);

        var draft = new DraftDraw(
            Draftable(request.Catalogue, request.Owned),
            request.Owned,
            request.Tuning,
            request.Weights,
            request.Forces,
            request.EverDrafted);

        if (draft.Draftable.Count == 0)
        {
            throw new InvalidOperationException(
                "Every perk the loaded content version authors is already owned at its max tier, so " +
                "06 §1.1's removal of maxed perks has emptied the draft pool. Reachable only against " +
                "a catalogue small enough for one run to exhaust — a hermetic fixture, or a truncated " +
                "content version — never against the shipped one, which M3-07b only widens further. " +
                "Refused before anything is drawn, so the stream is left where it stood.");
        }

        // How many of this draft's options the Codex bias is still allowed to select. Without the
        // cap a fresh account, whose whole catalogue is never-drafted, would see the bias on every
        // option and the rarity table would stop meaning anything.
        var codexBudget = LuckService.MaxCodexBiasedOptions(request.Tuning);

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
            if (codexBudget > 0 && !request.EverDrafted.Contains(option.PerkId))
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

    /// <summary>The pool without the categories the draft has already taken, where diversity needs it.</summary>
    /// <remarks>
    /// Whether this slot owes a new category is <see cref="DraftCompositionRules"/>'s answer, not
    /// this type's: the threshold and the narrowing that serves it are one rule, and restating the
    /// narrowing here as "the last slot of a mono-category draft" made the threshold a constant the
    /// engine agreed with by coincidence rather than one it read.
    /// </remarks>
    private static IReadOnlyList<PerkCatalogueEntry> WithoutClashingCategory(
        IReadOnlyList<PerkCatalogueEntry> pool,
        IReadOnlyList<PerkCategory> chosenCategories,
        int slot)
    {
        if (!DraftCompositionRules.MustContributeNewCategory(
                DistinctCount(chosenCategories), OptionCount - slot))
        {
            return pool;
        }

        var diverse = Matching(pool, perk => !Contains(chosenCategories, perk.Category));

        return diverse.Count > 0 ? diverse : pool;
    }

    /// <summary>How many distinct categories a draft has taken so far.</summary>
    private static int DistinctCount(IReadOnlyList<PerkCategory> categories)
    {
        var distinct = 0;

        for (var i = 0; i < categories.Count; i++)
        {
            if (!Contains(categories, categories[i], upTo: i))
            {
                distinct++;
            }
        }

        return distinct;
    }

    private static bool Contains(
        IReadOnlyList<PerkCategory> categories, PerkCategory category, int? upTo = null)
    {
        var end = upTo ?? categories.Count;

        for (var i = 0; i < end; i++)
        {
            if (categories[i] == category)
            {
                return true;
            }
        }

        return false;
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
