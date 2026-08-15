using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Dice;

/// <summary>
/// The reroll-charge calculation: base 1/stage, extra charges from talents (up to +2), Campfire (+2
/// this stage), perks and a Reroll Token consumable, capped at 5 stored. Pure arithmetic; does not
/// decide whether a specific reroll is legal — see the remarks.
/// </summary>
/// <remarks>
/// Not wired to persisted "charges used this stage" state: no stage-boundary concept exists on
/// <c>Run</c> yet, so there's nowhere to persist a spent count today. This type computes the total a
/// stage grants and enforces the cap on that total; a caller with a real spent-count compares it
/// against <see cref="TotalCharges"/> to decide legality once one exists. Nudge is a separate,
/// cheaper mechanic — talent-granted, ±1 to a Pip result, 1/stage, never a reroll —
/// deliberately not folded into the charge count above, since conflating the two currencies would be
/// the mistake to avoid.
/// </remarks>
internal static class RerollEconomy
{
    /// <summary>Every stage grants at least this many reroll charges.</summary>
    public const int BaseChargesPerStage = 1;

    /// <summary>The Fortune-branch talent's ceiling on its own bonus.</summary>
    public const int MaxTalentBonus = 2;

    /// <summary>Campfire grants exactly this many extra charges, for the current stage only.</summary>
    public const int CampfireBonus = 2;

    /// <summary>The hard ceiling on stored charges, regardless of source.</summary>
    public const int MaxStoredCharges = 5;

    /// <summary>
    /// The total reroll charges a stage grants, before the player has spent any: base + every bonus,
    /// capped at <see cref="MaxStoredCharges"/>.
    /// </summary>
    /// <param name="talentBonus">0..<see cref="MaxTalentBonus"/>, from the Fortune talent branch.</param>
    /// <param name="campfireVisited">Whether this stage's Campfire granted its bonus.</param>
    /// <param name="perkBonus">Extra charges from run-scoped perks. Never negative.</param>
    /// <param name="rerollTokensUsed">
    /// Reroll Token consumables used this stage — each use grants +1 immediately rather than being
    /// held, so this is a count of grants already applied, not a stash. Never negative.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="talentBonus"/> is outside 0..<see cref="MaxTalentBonus"/>, or
    /// <paramref name="perkBonus"/>/<paramref name="rerollTokensUsed"/> is negative.
    /// </exception>
    public static int TotalCharges(
        int talentBonus, bool campfireVisited, int perkBonus, int rerollTokensUsed)
    {
        if (talentBonus is < 0 or > MaxTalentBonus)
        {
            throw new ArgumentOutOfRangeException(
                nameof(talentBonus), talentBonus,
                "04 §3 caps the Fortune-branch talent bonus at " +
                MaxTalentBonus.ToString(CultureInfo.InvariantCulture) + ".");
        }

        if (perkBonus < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(perkBonus), perkBonus, "A bonus is never negative.");
        }

        if (rerollTokensUsed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rerollTokensUsed), rerollTokensUsed, "A use count is never negative.");
        }

        var total = BaseChargesPerStage
                    + talentBonus
                    + (campfireVisited ? CampfireBonus : 0)
                    + perkBonus
                    + rerollTokensUsed;

        return Math.Min(total, MaxStoredCharges);
    }

    /// <summary>
    /// Whether a reroll is affordable: <paramref name="chargesSpentThisStage"/> is strictly below
    /// <paramref name="totalCharges"/>. The ad reroll never calls this, since it doesn't consume a
    /// charge.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is negative.</exception>
    public static bool CanAffordReroll(int chargesSpentThisStage, int totalCharges)
    {
        if (chargesSpentThisStage < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chargesSpentThisStage), chargesSpentThisStage, "A spent count is never negative.");
        }

        if (totalCharges < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCharges), totalCharges, "A charge total is never negative.");
        }

        return chargesSpentThisStage < totalCharges;
    }

    /// <summary>
    /// Nudge: ±1 applied to a Pip roll, clamped to 1..6. Talent-granted, 1/stage — the once-per-stage
    /// legality is the caller's (see the type remarks); this is the arithmetic alone.
    /// </summary>
    /// <param name="rolledPips">The Pip face's current value, 1..6.</param>
    /// <param name="direction">-1 or +1.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rolledPips"/> is outside 1..6, or <paramref name="direction"/> is neither -1 nor +1.
    /// </exception>
    public static int NudgePip(int rolledPips, int direction)
    {
        if (rolledPips is < 1 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(rolledPips), rolledPips, "04 §1's pips are 1..6.");
        }

        if (direction is not (-1 or 1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction), direction, "Nudge moves a Pip result by exactly one step, -1 or +1.");
        }

        return Math.Clamp(rolledPips + direction, 1, 6);
    }
}
