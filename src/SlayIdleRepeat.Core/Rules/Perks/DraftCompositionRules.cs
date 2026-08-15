namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// The eight draft composition rules, stubbed pending a future luck-protection service. This type
/// is the seam: one call site per rule, each a documented, provably-inert pass-through today, so
/// filling them in later means eight method bodies rather than hunting for eight scattered call
/// sites.
/// </summary>
/// <remarks>
/// Because every rule below is a no-op, <see cref="PerkDraftEngine"/>'s draft can currently: offer
/// the same perk twice in one draft of three; offer three options from one category; and never
/// apply the owned-upgrade bias, the Legendary pity, the anti-brick Sustain guarantee, the quality
/// floor, the Codex bias, or the upgrade famine guarantee. This is documented scope, not an
/// introduced defect.
/// </remarks>
internal static class DraftCompositionRules
{
    /// <summary>No duplicate options within a single draft of 3. Stub: not enforced.</summary>
    internal static bool NoDuplicateOptions() => true;

    /// <summary>
    /// Category diversity: at least 2 distinct categories among the 3 options. Stub: not enforced.
    /// </summary>
    internal static bool CategoryDiversity() => true;

    /// <summary>
    /// Owned-upgrade bias: each option has a 30% chance of being drawn from the player's
    /// owned-but-not-maxed perks instead of the fresh pool. Stub: every option is drawn from the
    /// fresh pool; an option landing on an already-owned perk still upgrades it (the base tier
    /// rule, not this bias), but the 30% bias draw itself never fires.
    /// </summary>
    internal static bool OwnedUpgradeBias() => false;

    /// <summary>
    /// Legendary pity: if no Legendary has appeared by draft #14 of a run, force one into draft
    /// #15. Stub: needs a per-run "drafts since last Legendary" counter that does not exist yet.
    /// </summary>
    internal static bool LegendaryPity() => false;

    /// <summary>
    /// Anti-brick: if the player has no Sustain perk by the end of Stage 2, force one Sustain
    /// option into the next draft. Stub: needs the same pending counter.
    /// </summary>
    internal static bool AntiBrickSustain() => false;

    /// <summary>
    /// Quality floor: 3 consecutive drafts with no option above Common force a Rare-or-better
    /// option into the next draft. Stub: needs a consecutive-Common-drafts counter.
    /// </summary>
    internal static bool QualityFloor() => false;

    /// <summary>
    /// Codex bias: never-drafted perks carry a ×1.35 weight in the fresh-pool draw, capped at 1
    /// bias-selected option per draft. Stub: needs a per-player "ever drafted" set not yet authored.
    /// </summary>
    internal static bool CodexBias() => false;

    /// <summary>
    /// Upgrade famine: 5 consecutive drafts with no owned-perk upgrade offered (while a non-maxed
    /// owned perk exists) force one. Stub: needs a consecutive-drafts-without-upgrade counter.
    /// </summary>
    internal static bool UpgradeFamine() => false;
}
