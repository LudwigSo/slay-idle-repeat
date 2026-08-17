using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Gear;

/// <summary>One set that is currently contributing: which set, how many pieces, and how far it has got.</summary>
/// <param name="Set">
/// The set, named by its family axis. There are exactly four sets, one per axis, so the axis is the
/// set's identity — no separate id is authored anywhere, and none is stored on an item.
/// </param>
/// <param name="Pieces">How many pieces of it are equipped.</param>
/// <param name="BreakpointsMet">
/// The authored piece counts this many pieces has reached, ascending. Empty when the set is worn but
/// has not reached its first breakpoint.
/// </param>
internal readonly record struct ActiveSet(
    GearFamilyAxis Set, int Pieces, IReadOnlyList<int> BreakpointsMet);

/// <summary>
/// The four SS set engines: which sets a loadout is wearing, and which of their breakpoints it has
/// reached.
/// </summary>
/// <remarks>
/// <para>
/// <b>The set is derived, never stored.</b> Every family belongs to exactly one axis and there is
/// exactly one set per axis, so an SS item's set is a lookup from its family through the base-item
/// catalogue. Storing a set id on the instance would be the same value written twice, and the design
/// set is explicit that a value which can be computed is not persisted.
/// </para>
/// <para>
/// <b>SS only.</b> A set piece is an SS item; the same family at any lower band contributes nothing,
/// which is what makes a full set an endgame goal rather than something a mid-game player assembles
/// by accident. Lower-band pieces are counted out here rather than filtered by the caller, so the
/// rule lives with the engine that states it.
/// </para>
/// <para>
/// ⚠️ <b>What this resolves is the tier, not the bonus.</b> The design set names the four sets and
/// describes their two-, four- and six-piece effects in prose, and no effect id, op or magnitude for
/// any of them is authored anywhere — <c>drops.json</c> still carries a null id on all four rows. So
/// the breakpoints a loadout has met are answered here and the effects they grant are deliberately
/// absent rather than invented; the task that authors set-bonus content picks them up from this
/// answer.
/// </para>
/// </remarks>
internal static class SetBonusResolver
{
    /// <summary>The only band whose pieces count towards a set.</summary>
    internal const Rarity SetBand = Rarity.SS;

    /// <summary>
    /// The four family axes, in declaration order — the order <see cref="Resolve"/> answers its sets
    /// in.
    /// </summary>
    /// <remarks>
    /// Cached rather than re-read per call, on <c>Content.ProfanityLexicon.Languages</c>' precedent:
    /// <see cref="Enum.GetValues{TEnum}()"/> allocates a fresh array every time it is called, and
    /// <see cref="Resolve"/> runs once per loadout derivation. Read off the enum rather than
    /// transcribed, so a fifth axis is carried here without an edit.
    /// </remarks>
    private static readonly GearFamilyAxis[] FamilyAxes = Enum.GetValues<GearFamilyAxis>();

    /// <summary>Every set the loadout is wearing at least one piece of, in family-axis order.</summary>
    /// <param name="catalogue">The base-item catalogue, which maps a family to its axis.</param>
    /// <param name="drops">The gear tables, for the authored breakpoints.</param>
    /// <param name="equipped">The items currently equipped. Never null; may be empty.</param>
    /// <returns>The active sets. Empty when no SS piece is equipped.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="InvalidTunableException">An equipped item's family has no catalogue row.</exception>
    internal static IReadOnlyList<ActiveSet> Resolve(
        GearCatalogue catalogue, DropsTuning drops, IReadOnlyList<GearInstance> equipped)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(equipped);

        var pieces = new Dictionary<GearFamilyAxis, int>();

        foreach (var item in equipped)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(equipped));

            if (item.Rarity != SetBand)
            {
                continue;
            }

            var axis = catalogue.Definition(item.Family).Axis;

            pieces[axis] = pieces.TryGetValue(axis, out var worn) ? worn + 1 : 1;
        }

        var active = new List<ActiveSet>(pieces.Count);

        foreach (var axis in FamilyAxes)
        {
            if (pieces.TryGetValue(axis, out var worn))
            {
                active.Add(new ActiveSet(axis, worn, BreakpointsMet(drops.SetBreakpoints, worn)));
            }
        }

        return active.AsReadOnly();
    }

    /// <summary>
    /// The authored breakpoints a piece count has reached, ascending.
    /// </summary>
    /// <remarks>
    /// Every breakpoint at or below the count, not only the highest: the tiers escalate rather than
    /// replace, so a six-piece set is still granting its two- and four-piece bonuses.
    /// </remarks>
    private static IReadOnlyList<int> BreakpointsMet(IReadOnlyList<int> breakpoints, int pieces)
    {
        var met = new List<int>(breakpoints.Count);

        foreach (var breakpoint in breakpoints)
        {
            if (pieces >= breakpoint)
            {
                met.Add(breakpoint);
            }
        }

        return met.AsReadOnly();
    }
}
