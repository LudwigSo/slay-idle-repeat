namespace SlayIdleRepeat.Core.Rules.Perks;

/// <summary>
/// 🔒 M3-06 — `06` §4's eight composition rules, stubbed. `24_LUCK_PROTECTION.md` §4.7 is their
/// authority and rules that they "resolve through <c>LuckService</c>, not in the draft code" —
/// and <c>LuckService</c> does not exist until M4-01. This type is the seam: one call site per
/// rule, each a documented, provably-inert pass-through today, so M4-01 fills in eight method
/// bodies rather than hunting for eight scattered call sites.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>The consequence, stated rather than hidden.</b> Because every rule below is a no-op,
/// <see cref="PerkDraftEngine"/>'s draft can, today: offer the same perk twice in one draft of
/// three; offer three options from one category; never apply the owned-upgrade bias, the
/// Legendary pity, the anti-brick Sustain guarantee, the quality floor, the Codex bias or the
/// upgrade famine guarantee. `06` §4's own text is explicit that all eight "resolve through
/// LuckService", so this is the milestone's documented scope, not a defect this task introduced.
/// </para>
/// <para>
/// Each stub is named for the rule it stands in for and each says, in its own remarks, what state
/// M4-01 will need that does not exist yet (mostly: <c>LuckService</c>'s own pity/streak counters,
/// which are themselves a separate <c>GapRegister</c> entry — <c>PityCounters</c>/M4-01).
/// </para>
/// </remarks>
internal static class DraftCompositionRules
{
    /// <summary>`06` §4 — "No duplicate options within a single draft of 3." Stub: not enforced.</summary>
    internal static bool NoDuplicateOptions() => true;

    /// <summary>
    /// `06` §4 — "Category diversity: at least 2 distinct categories among the 3 options."
    /// Stub: not enforced.
    /// </summary>
    internal static bool CategoryDiversity() => true;

    /// <summary>
    /// `06` §4 — "Owned-upgrade bias: each option has a 30% chance of being drawn from the
    /// player's owned-but-not-maxed perks instead of the fresh pool." Stub: every option is drawn
    /// from the fresh pool; an option that happens to land on an already-owned perk still upgrades
    /// it (that is `06` §1.1's base tier rule, not this bias), but the 30% bias draw itself never
    /// fires.
    /// </summary>
    internal static bool OwnedUpgradeBias() => false;

    /// <summary>
    /// `06` §4 — "Legendary pity: if no Legendary has appeared by draft #14 of a run, force one
    /// into draft #15." Stub: needs a per-run "drafts since last Legendary" counter that does not
    /// exist yet — <c>LuckService</c>'s (24 §11).
    /// </summary>
    internal static bool LegendaryPity() => false;

    /// <summary>
    /// `06` §4 — "Anti-brick: if the player has no Sustain perk by the end of Stage 2, force one
    /// Sustain option into the next draft." Stub: needs the same LuckService-owned counter.
    /// </summary>
    internal static bool AntiBrickSustain() => false;

    /// <summary>
    /// `06` §4 — "Quality floor: 3 consecutive drafts with no option above Common force a
    /// Rare-or-better option into the next draft." Stub: needs a consecutive-Common-drafts
    /// counter.
    /// </summary>
    internal static bool QualityFloor() => false;

    /// <summary>
    /// `06` §4 — "Codex bias: never-drafted perks carry a ×1.35 weight in the fresh-pool draw,
    /// capped at 1 bias-selected option per draft." Stub: needs a per-player "ever drafted" set,
    /// which is Codex state this milestone does not author.
    /// </summary>
    internal static bool CodexBias() => false;

    /// <summary>
    /// `06` §4 — "Upgrade famine: 5 consecutive drafts with no owned-perk upgrade offered (while a
    /// non-maxed owned perk exists) force one." Stub: needs a consecutive-drafts-without-upgrade
    /// counter.
    /// </summary>
    internal static bool UpgradeFamine() => false;
}
