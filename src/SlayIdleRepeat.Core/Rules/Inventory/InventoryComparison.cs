using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Gear;

namespace SlayIdleRepeat.Core.Rules.Inventory;

/// <summary>One stat of a side-by-side comparison: what the candidate gives, what is worn, and the difference.</summary>
/// <param name="Stat">The authored stat token, e.g. <c>ATK</c>.</param>
/// <param name="Candidate">What the candidate item derives for it.</param>
/// <param name="Equipped">What the worn item derives for it, or zero when the slot is empty.</param>
/// <param name="Delta">
/// <see cref="Candidate"/> minus <see cref="Equipped"/>, rounded. Carried rather than left to the
/// caller: a subtraction of two rounded numbers is not itself guaranteed to be rounded, and this
/// figure reaches a screen and a wire response.
/// </param>
/// <param name="IsPercent">
/// Whether the figures are fractions feeding a capped percentage rather than flat amounts. A screen
/// that added the two kinds together would show a number nothing in the game computes.
/// </param>
internal readonly record struct GearStatDelta(
    string Stat, double Candidate, double Equipped, double Delta, bool IsPercent);

/// <summary>
/// The side-by-side delta: what a candidate item would change, stat by stat, against whatever is
/// worn in its slot.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The figures come from the same derivation the hero screen reads.</b> A second derivation
/// here would be a second answer to "what does this item give", and the two would eventually
/// disagree by a rounding step — which is precisely the difference a player would read as an
/// upgrade.
/// </para>
/// <para>
/// The Core seam only. Which item is <em>equipped</em> belongs to the task that owns the loadout, and
/// the arrows a screen draws from these numbers belong to the forge screen; this answers the
/// arithmetic they both need and neither should reimplement.
/// </para>
/// </remarks>
internal static class InventoryComparison
{
    /// <summary>What an empty slot contributes: nothing, so the whole candidate figure is the gain.</summary>
    private const double NothingWorn = 0.0;

    /// <summary>Compares a candidate against what is worn in its slot.</summary>
    /// <param name="par">The par table.</param>
    /// <param name="drops">The gear tables.</param>
    /// <param name="candidate">The item being considered.</param>
    /// <param name="equipped">The item worn in the same slot, or <c>null</c> when the slot is empty.</param>
    /// <returns>The primary stat then the secondary, the order the derivation itself answers in.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="par"/>, <paramref name="drops"/> or <paramref name="candidate"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The two items are worn in different slots. A boot and a blade have no stat in common to
    /// subtract, so the "delta" would report each item's own value as a gain and a loss of two
    /// unrelated quantities.
    /// </exception>
    internal static IReadOnlyList<GearStatDelta> Compare(
        ParPowerTuning par,
        DropsTuning drops,
        ForgeTuning forge,
        GearInstance candidate,
        GearInstance? equipped)
    {
        ArgumentNullException.ThrowIfNull(par);
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(forge);
        ArgumentNullException.ThrowIfNull(candidate);

        if (equipped is not null && equipped.Slot != candidate.Slot)
        {
            throw new ArgumentException(
                "A " + equipped.Slot + " item cannot be compared against a " + candidate.Slot +
                " one: the two slots author different stats, so there is nothing to subtract. The " +
                "comparison is against what is worn IN THE CANDIDATE'S SLOT, which is the caller's " +
                "to look up.",
                nameof(equipped));
        }

        // 🔒 Both sides AS WORN — enhancement folded in by the same helper the hero build uses — so a
        // +10 blade beats a +0 blade here exactly as it does in the fight. The raw derivation would
        // call the two a tie, and a player reading that would salvage the wrong one.
        return Array.AsReadOnly(new[]
        {
            Delta(
                Worn(GearStatDerivation.Primary(par, drops, candidate), forge, candidate),
                equipped is null ? null : Worn(GearStatDerivation.Primary(par, drops, equipped), forge, equipped)),
            Delta(
                Worn(GearStatDerivation.Secondary(par, drops, candidate), forge, candidate),
                equipped is null ? null : Worn(GearStatDerivation.Secondary(par, drops, equipped), forge, equipped)),
        });
    }

    private static DerivedGearStat Worn(DerivedGearStat derived, ForgeTuning forge, GearInstance item) =>
        GearStatDerivation.AsWorn(derived, forge, item.EnhanceLevel);

    /// <summary>One stat's three figures, with the difference rounded like the two it came from.</summary>
    private static GearStatDelta Delta(DerivedGearStat candidate, DerivedGearStat? equipped)
    {
        var worn = equipped?.Value ?? NothingWorn;

        return new GearStatDelta(
            candidate.Stat,
            candidate.Value,
            worn,
            DeterminismRounding.Round(candidate.Value - worn),
            candidate.IsPercent);
    }
}
