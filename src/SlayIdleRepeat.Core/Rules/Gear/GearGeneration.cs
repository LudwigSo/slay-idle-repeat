using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Rules.Gear;

/// <summary>
/// The in-run gear grant paths: an ordinary, Elite or boss kill's drop, and the session floor a
/// completed run owes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing protected is decided here.</b> The band of a drop and the size of a floor grant both
/// come back from the luck façade, which is the one place a guarantee can fire. A generator that drew
/// its own rarity would skip the dry-streak counters, and a skipped counter is invisible until a
/// player has killed six Elites for nothing — which is exactly why this type carries no exemption
/// from the routing rule and fails the day it stops calling the façade.
/// </para>
/// <para>
/// It writes nothing. Both entry points answer what the roll produced and, for a drop, the counter
/// changes it implies; whether the drop is kept is the caller's decision, on the same stateless
/// contract the façade itself keeps.
/// </para>
/// </remarks>
internal static class GearGeneration
{
    /// <summary>Rolls one in-run gear drop.</summary>
    /// <param name="instanceId">The identity the server minted for this item.</param>
    /// <param name="catalogue">The twenty-four base items.</param>
    /// <param name="tuning">The pity registry.</param>
    /// <param name="dropRun">The in-run drop protections.</param>
    /// <param name="drops">The gear tables.</param>
    /// <param name="chapterOrigin">The chapter the item is scaled against, from 1.</param>
    /// <param name="trigger">What produced the drop.</param>
    /// <param name="counters">The player's counters as they stand before this drop.</param>
    /// <param name="draws">The already-opened drop stream, continued.</param>
    /// <returns>The item, whether a breaker forced its band, and the counter changes to apply.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The chapter is below 1, or the trigger is undeclared.</exception>
    internal static (GearInstance Item, bool FromPity, IReadOnlyList<PityCounterChange> Changes)
        RollRunDrop(
            GearInstanceId instanceId,
            GearCatalogue catalogue,
            LuckTuning tuning,
            DropRunTuning dropRun,
            DropsTuning drops,
            int chapterOrigin,
            RunDropTrigger trigger,
            PityCounters counters,
            DeterministicRng draws)
    {
        var resolution = LuckService.ResolveRunDrop(
            tuning, dropRun, drops, chapterOrigin, trigger, counters, draws);

        var item = GearMinting.Mint(
            instanceId, catalogue, drops, chapterOrigin, resolution.Outcome, draws);

        return (item, resolution.FromPity, resolution.Changes);
    }

    /// <summary>
    /// The items a completed run's session floor owes the player, if any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The floor is not a draw: it is the run-end payout noticing that a qualifying run produced
    /// nothing worth keeping and adding an item at the authored band. So the <em>count</em> comes from
    /// the façade and only the base item, the quality and the affixes are drawn.
    /// </para>
    /// <para>
    /// The chapter the floor grants at is the caller's, not the run's — the design set reads it when
    /// the floor fires, from how far the player has cleared, rather than from where the run happened
    /// to end.
    /// </para>
    /// </remarks>
    /// <param name="instanceIds">
    /// The identities the server minted, one per possible grant. At least as many as the floor can
    /// grant; the surplus is unused.
    /// </param>
    /// <param name="catalogue">The twenty-four base items.</param>
    /// <param name="dropRun">The in-run drop protections.</param>
    /// <param name="drops">The gear tables.</param>
    /// <param name="chapterOrigin">The chapter the floor grants at, from 1.</param>
    /// <param name="qualified">Whether the run ended the way the floor requires.</param>
    /// <param name="itemsAtOrAboveFloor">How many items at or above the floor's band the run produced.</param>
    /// <param name="grantsAlreadyToday">How many floor grants the player has already taken today.</param>
    /// <param name="draws">The already-opened drop stream, continued.</param>
    /// <returns>The granted items. Empty when the floor does not fire.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException">Fewer identities were minted than the floor grants.</exception>
    internal static IReadOnlyList<GearInstance> RollSessionFloor(
        IReadOnlyList<GearInstanceId> instanceIds,
        GearCatalogue catalogue,
        DropRunTuning dropRun,
        DropsTuning drops,
        int chapterOrigin,
        bool qualified,
        int itemsAtOrAboveFloor,
        int grantsAlreadyToday,
        DeterministicRng draws)
    {
        ArgumentNullException.ThrowIfNull(instanceIds);
        ArgumentNullException.ThrowIfNull(dropRun);

        var count = LuckService.SessionFloorGrant(
            dropRun, qualified, itemsAtOrAboveFloor, grantsAlreadyToday);

        if (count == 0)
        {
            return Array.Empty<GearInstance>();
        }

        if (instanceIds.Count < count)
        {
            throw new ArgumentException(
                "The session floor grants more items than identities were minted for it. An item with " +
                "no id cannot be stored, equipped or salvaged, so the shortfall is refused rather than " +
                "silently paying out fewer items than the floor owes.",
                nameof(instanceIds));
        }

        var granted = new GearInstance[count];

        for (var i = 0; i < count; i++)
        {
            granted[i] = GearMinting.Mint(
                instanceIds[i],
                catalogue,
                drops,
                chapterOrigin,
                dropRun.SessionFloor.GrantRarity,
                draws);
        }

        return Array.AsReadOnly(granted);
    }
}
